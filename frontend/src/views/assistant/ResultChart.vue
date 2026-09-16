<template>
  <div class="result-chart">
    <el-empty v-if="!points.length" description="没有可绘图的数据" :image-size="70" />

    <!-- 饼图：用 conic-gradient 画，零依赖且清晰 -->
    <div v-else-if="chart === 'pie'" class="pie-wrap">
      <div class="pie" :style="{ background: pieGradient }"></div>
      <ul class="legend">
        <li v-for="(point, index) in points" :key="index">
          <span class="dot" :style="{ background: colorAt(index) }"></span>
          <span class="legend-label">{{ point.label }}</span>
          <span class="legend-value">{{ formatNumber(point.value) }}</span>
        </li>
      </ul>
    </div>

    <svg v-else :viewBox="`0 0 ${W} ${H}`" class="svg-chart" preserveAspectRatio="xMidYMid meet">
      <!-- 网格与 Y 轴刻度 -->
      <g>
        <template v-for="(tick, index) in ticks" :key="`t${index}`">
          <line :x1="PAD.left" :y1="tick.y" :x2="W - PAD.right" :y2="tick.y" class="grid-line" />
          <text :x="PAD.left - 8" :y="tick.y + 4" class="axis-text" text-anchor="end">
            {{ tick.label }}
          </text>
        </template>
      </g>

      <!-- 折线 -->
      <template v-if="chart === 'line'">
        <polyline :points="linePoints" class="line-path" />
        <circle
          v-for="(point, index) in geometry"
          :key="`p${index}`"
          :cx="point.x"
          :cy="point.y"
          r="4"
          class="line-dot"
        />
      </template>

      <!-- 柱状 -->
      <template v-else>
        <rect
          v-for="(bar, index) in geometry"
          :key="`b${index}`"
          :x="bar.x"
          :y="bar.y"
          :width="bar.width"
          :height="bar.height"
          rx="3"
          :fill="colorAt(index)"
        >
          <title>{{ points[index].label }}: {{ formatNumber(points[index].value) }}</title>
        </rect>
      </template>

      <!-- X 轴标签 -->
      <text
        v-for="(point, index) in geometry"
        :key="`x${index}`"
        :x="point.x + point.width / 2"
        :y="H - PAD.bottom + 16"
        class="axis-text"
        text-anchor="end"
        :transform="`rotate(-32 ${point.x + point.width / 2} ${H - PAD.bottom + 16})`"
      >
        {{ truncateLabel(points[index].label) }}
      </text>
    </svg>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  columns: { type: Array, default: () => [] },
  rows: { type: Array, default: () => [] },
  chart: { type: String, default: 'bar' },
  xField: { type: String, default: '' },
  yField: { type: String, default: '' },
})

const W = 820
const H = 340
const PAD = { left: 66, right: 24, top: 22, bottom: 84 }

const palette = ['#409EFF', '#67C23A', '#E6A23C', '#F56C6C', '#909399', '#9F7AEA', '#00B4D8', '#FF85C0']

function colorAt(index) {
  return palette[index % palette.length]
}

/** 把「列 + 行」解析成 {label, value} 序列。 */
const points = computed(() => {
  const names = props.columns.map((c) => String(c.name))
  if (!names.length || !props.rows.length) {
    return []
  }

  const find = (field) =>
    field ? names.findIndex((n) => n.toLowerCase() === String(field).toLowerCase()) : -1

  let xi = find(props.xField)
  if (xi < 0) {
    // 退化为「第一个非数值列」当维度
    xi = props.columns.findIndex((_, i) => !props.rows.some((r) => typeof r[i] === 'number'))
  }
  if (xi < 0) {
    xi = 0
  }

  let yi = find(props.yField)
  if (yi < 0 || yi === xi) {
    yi = props.columns.findIndex((_, i) => i !== xi && props.rows.some((r) => typeof r[i] === 'number'))
  }
  if (yi < 0) {
    yi = Math.min(1, names.length - 1)
  }

  return props.rows.map((row) => {
    const raw = row[yi]
    const value = typeof raw === 'number' ? raw : Number(raw)
    return {
      label: row[xi] === null || row[xi] === undefined ? '(空)' : String(row[xi]),
      value: Number.isFinite(value) ? value : 0,
    }
  })
})

const maxValue = computed(() => {
  const max = Math.max(...points.value.map((p) => p.value), 0)
  return max > 0 ? max : 1
})

const plotWidth = W - PAD.left - PAD.right
const plotHeight = H - PAD.top - PAD.bottom

const ticks = computed(() => {
  const count = 5
  return Array.from({ length: count + 1 }, (_, index) => {
    const ratio = index / count
    return {
      y: PAD.top + plotHeight * (1 - ratio),
      label: formatNumber(maxValue.value * ratio),
    }
  })
})

/** 每个点的几何位置（柱状时带 width）。 */
const geometry = computed(() => {
  const list = points.value
  const slot = plotWidth / Math.max(list.length, 1)
  const barWidth = Math.max(4, slot * 0.62)

  return list.map((point, index) => {
    const ratio = Math.max(0, Math.min(1, point.value / maxValue.value))
    const height = ratio * plotHeight
    const centerX = PAD.left + slot * index + slot / 2
    return {
      x: props.chart === 'line' ? centerX : centerX - barWidth / 2,
      y: PAD.top + plotHeight - height,
      width: props.chart === 'line' ? 0 : barWidth,
      height: Math.max(height, point.value > 0 ? 1 : 0),
    }
  })
})

const linePoints = computed(() => geometry.value.map((g) => `${g.x},${g.y}`).join(' '))

const pieGradient = computed(() => {
  const list = points.value.filter((p) => p.value > 0)
  const total = list.reduce((sum, p) => sum + p.value, 0) || 1
  let cursor = 0
  const stops = list.map((point, index) => {
    const start = cursor
    cursor += (point.value / total) * 100
    return `${colorAt(index)} ${start.toFixed(3)}% ${cursor.toFixed(3)}%`
  })
  return `conic-gradient(${stops.join(', ')})`
})

function formatNumber(value) {
  if (value === null || value === undefined) {
    return '-'
  }
  const number = Number(value)
  if (!Number.isFinite(number)) {
    return String(value)
  }
  if (Math.abs(number) >= 1000) {
    return number.toLocaleString('zh-CN', { maximumFractionDigits: 2 })
  }
  return Number.isInteger(number) ? String(number) : String(Number(number.toFixed(3)))
}

function truncateLabel(label) {
  return label.length > 10 ? `${label.slice(0, 10)}…` : label
}
</script>

<style scoped>
.result-chart {
  width: 100%;
}

.svg-chart {
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

.line-path {
  fill: none;
  stroke: #409eff;
  stroke-width: 2;
  stroke-linejoin: round;
}

.line-dot {
  fill: #fff;
  stroke: #409eff;
  stroke-width: 2;
}

.pie-wrap {
  display: flex;
  align-items: center;
  gap: 28px;
  flex-wrap: wrap;
}

.pie {
  width: 220px;
  height: 220px;
  border-radius: 50%;
  box-shadow: inset 0 0 0 1px #ebeef5;
}

.legend {
  list-style: none;
  margin: 0;
  padding: 0;
  min-width: 220px;
}

.legend li {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 3px 0;
  font-size: 13px;
}

.dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex: 0 0 auto;
}

.legend-label {
  color: #606266;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 180px;
}

.legend-value {
  margin-left: auto;
  font-weight: 600;
  color: #303133;
}
</style>
