<template>
  <div class="spc-panel">
    <el-card shadow="never">
      <el-form :inline="true" :model="query">
        <el-form-item label="检验项">
          <el-select
            v-model="query.itemName"
            filterable
            allow-create
            default-first-option
            placeholder="选择或输入检验项名称"
            style="width: 260px"
          >
            <el-option v-for="name in itemNames" :key="name" :label="name" :value="name" />
          </el-select>
        </el-form-item>
        <el-form-item label="数据点">
          <el-input-number v-model="query.points" :min="5" :max="200" :step="5" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="loadTrend">查询</el-button>
          <el-button :icon="Refresh" @click="loadItemNames">刷新检验项</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-alert
      v-if="trend && trend.hasSignal"
      :title="`过程判异：${trend.signalDescription}`"
      type="warning"
      show-icon
      :closable="false"
    />
    <el-alert
      v-else-if="trend && trend.sampleCount >= 2"
      title="未发现判异信号（单点未超 3σ，且无连续 7 点同侧）"
      type="success"
      show-icon
      :closable="false"
    />
    <el-alert
      v-else-if="trend"
      :title="trend.signalDescription || '样本不足，无法计算控制限（至少需要 2 个数值点）'"
      type="info"
      show-icon
      :closable="false"
    />

    <el-card v-if="trend" shadow="never">
      <template #header>
        <div class="chart-header">
          <span>控制图：{{ trend.itemName }}</span>
          <span class="chart-sub">样本数 {{ trend.sampleCount }}</span>
        </div>
      </template>

      <el-descriptions :column="5" border size="small" class="stats">
        <el-descriptions-item label="均值 CL">{{ format(trend.mean) }}</el-descriptions-item>
        <el-descriptions-item label="标准差 σ">{{ format(trend.stdDev) }}</el-descriptions-item>
        <el-descriptions-item label="控制上限 UCL">{{ format(trend.upperControlLimit) }}</el-descriptions-item>
        <el-descriptions-item label="控制下限 LCL">{{ format(trend.lowerControlLimit) }}</el-descriptions-item>
        <el-descriptions-item label="规格上下限">
          {{ format(trend.lowerLimit) }} ~ {{ format(trend.upperLimit) }}
        </el-descriptions-item>
      </el-descriptions>

      <svg v-if="points.length" :viewBox="`0 0 ${W} ${H}`" class="control-chart" preserveAspectRatio="xMidYMid meet">
        <!-- 网格与刻度 -->
        <template v-for="(tick, index) in ticks" :key="`t${index}`">
          <line :x1="PAD.left" :y1="tick.y" :x2="W - PAD.right" :y2="tick.y" class="grid-line" />
          <text :x="PAD.left - 8" :y="tick.y + 4" class="axis-text" text-anchor="end">{{ tick.label }}</text>
        </template>

        <!-- 规格限（若检验项配置了规格上下限） -->
        <line
          v-if="trend.upperLimit !== null"
          :x1="PAD.left"
          :y1="scale(trend.upperLimit)"
          :x2="W - PAD.right"
          :y2="scale(trend.upperLimit)"
          class="spec-line"
        />
        <line
          v-if="trend.lowerLimit !== null"
          :x1="PAD.left"
          :y1="scale(trend.lowerLimit)"
          :x2="W - PAD.right"
          :y2="scale(trend.lowerLimit)"
          class="spec-line"
        />

        <!-- 控制限 -->
        <line
          v-if="trend.upperControlLimit !== null"
          :x1="PAD.left"
          :y1="scale(trend.upperControlLimit)"
          :x2="W - PAD.right"
          :y2="scale(trend.upperControlLimit)"
          class="control-line"
        />
        <line
          v-if="trend.lowerControlLimit !== null"
          :x1="PAD.left"
          :y1="scale(trend.lowerControlLimit)"
          :x2="W - PAD.right"
          :y2="scale(trend.lowerControlLimit)"
          class="control-line"
        />
        <line
          v-if="trend.mean !== null"
          :x1="PAD.left"
          :y1="scale(trend.mean)"
          :x2="W - PAD.right"
          :y2="scale(trend.mean)"
          class="mean-line"
        />

        <!-- 折线与点 -->
        <polyline :points="polyline" class="trend-line" />
        <template v-for="(point, index) in points" :key="`p${index}`">
          <circle
            :cx="xAt(index)"
            :cy="scale(point.value)"
            r="4"
            :class="['trend-dot', point.isQualified === false ? 'trend-dot-bad' : '']"
          >
            <title>{{ point.label }} · {{ format(point.value) }} · {{ point.inspectionNumber }}</title>
          </circle>
        </template>

        <!-- 横轴标签（稀疏显示，避免重叠） -->
        <text
          v-for="(point, index) in axisLabels"
          :key="`x${index}`"
          :x="xAt(point.index)"
          :y="H - PAD.bottom + 16"
          class="axis-text"
          text-anchor="end"
          :transform="`rotate(-32 ${xAt(point.index)} ${H - PAD.bottom + 16})`"
        >
          {{ point.label }}
        </text>
      </svg>

      <el-empty v-else description="该检验项没有数值型数据" :image-size="70" />
    </el-card>

    <el-empty v-else-if="!loading" description="选择检验项后点击查询，查看均值 / ±3σ 控制图与判异结论" />
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import { spcApi } from '@/api/quality'

const W = 880
const H = 340
const PAD = { left: 70, right: 24, top: 24, bottom: 84 }

const itemNames = ref([])
const trend = ref(null)
const loading = ref(false)
const query = reactive({ itemName: '', points: 30 })

const points = computed(() => {
  const raw = (trend.value?.points || []).map((point) => ({
    value: Number(point.value),
    raw: point.value,
    isQualified: point.isQualified,
    timestamp: point.timestamp,
    inspectionNumber: point.inspectionNumber,
    label: String(point.timestamp || '').replace('T', ' ').slice(5, 16),
  }))
  // 后端按时间倒序取点，这里统一成正序，控制图才读得通
  return raw.filter((point) => Number.isFinite(point.value)).sort((a, b) => (a.timestamp > b.timestamp ? 1 : -1))
})

const bounds = computed(() => {
  const values = points.value.map((point) => point.value)
  const candidates = [
    ...values,
    trend.value?.upperControlLimit,
    trend.value?.lowerControlLimit,
    trend.value?.upperLimit,
    trend.value?.lowerLimit,
  ].filter((value) => value !== null && value !== undefined && Number.isFinite(Number(value))).map(Number)

  if (!candidates.length) {
    return { min: 0, max: 1 }
  }

  const min = Math.min(...candidates)
  const max = Math.max(...candidates)
  const span = max - min || Math.abs(max) * 0.1 || 1
  return { min: min - span * 0.12, max: max + span * 0.12 }
})

const plotWidth = W - PAD.left - PAD.right
const plotHeight = H - PAD.top - PAD.bottom

const ticks = computed(() => {
  const { min, max } = bounds.value
  return Array.from({ length: 5 }, (_, index) => {
    const ratio = index / 4
    const value = min + (max - min) * ratio
    return { y: PAD.top + plotHeight * (1 - ratio), label: format(value) }
  })
})

function scale(value) {
  const { min, max } = bounds.value
  const ratio = (Number(value) - min) / (max - min || 1)
  return PAD.top + plotHeight * (1 - Math.max(0, Math.min(1, ratio)))
}

function xAt(index) {
  const count = Math.max(points.value.length - 1, 1)
  return PAD.left + (plotWidth / count) * index
}

const polyline = computed(() =>
  points.value.map((point, index) => `${xAt(index)},${scale(point.value)}`).join(' '),
)

const axisLabels = computed(() => {
  const list = points.value
  const step = Math.max(1, Math.ceil(list.length / 10))
  return list
    .map((point, index) => ({ index, label: point.label }))
    .filter((item) => item.index % step === 0)
})

function format(value) {
  if (value === null || value === undefined || value === '') {
    return '-'
  }
  const number = Number(value)
  if (!Number.isFinite(number)) {
    return String(value)
  }
  return Number.isInteger(number) ? String(number) : String(Number(number.toFixed(4)))
}

async function loadItemNames() {
  try {
    itemNames.value = await spcApi.items(200)
    if (!query.itemName && itemNames.value.length > 0) {
      query.itemName = itemNames.value[0]
    }
  } catch {
    itemNames.value = []
  }
}

async function loadTrend() {
  if (!query.itemName) {
    ElMessage.warning('请先选择检验项')
    return
  }

  loading.value = true
  try {
    trend.value = await spcApi.trend({ itemName: query.itemName, points: query.points })
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await loadItemNames()
  if (query.itemName) {
    await loadTrend()
  }
})
</script>

<style scoped>
.spc-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.chart-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
}

.chart-sub {
  font-size: 12px;
  color: #909399;
}

.stats {
  margin-bottom: 16px;
}

.control-chart {
  width: 100%;
  height: auto;
  max-height: 380px;
}

.grid-line {
  stroke: #ebeef5;
  stroke-width: 1;
}

.axis-text {
  font-size: 11px;
  fill: #909399;
}

.mean-line {
  stroke: #409eff;
  stroke-width: 1.5;
  stroke-dasharray: 6 4;
}

.control-line {
  stroke: #f56c6c;
  stroke-width: 1.2;
  stroke-dasharray: 4 4;
}

.spec-line {
  stroke: #e6a23c;
  stroke-width: 1;
  stroke-dasharray: 2 4;
}

.trend-line {
  fill: none;
  stroke: #409eff;
  stroke-width: 2;
  stroke-linejoin: round;
}

.trend-dot {
  fill: #fff;
  stroke: #409eff;
  stroke-width: 2;
}

.trend-dot-bad {
  fill: #f56c6c;
  stroke: #f56c6c;
}
</style>
