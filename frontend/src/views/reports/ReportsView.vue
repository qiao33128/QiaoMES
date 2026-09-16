<template>
  <div class="reports-page">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="生产日">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="~"
            start-placeholder="开始"
            end-placeholder="结束"
            style="width: 260px"
          />
        </el-form-item>
        <el-form-item>
          <el-button-group>
            <el-button size="small" @click="quickRange(0)">今日</el-button>
            <el-button size="small" @click="quickRange(6)">近 7 天</el-button>
            <el-button size="small" @click="quickRange(29)">近 30 天</el-button>
          </el-button-group>
        </el-form-item>
        <el-form-item label="产线">
          <el-input v-model="query.lineName" placeholder="留空=全部" clearable style="width: 140px" @keyup.enter="loadAll" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" :loading="loading" @click="loadAll">查询</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-dropdown @command="handleCommand">
            <el-button :icon="Download">导出 / 打印<el-icon class="el-icon--right"><ArrowDown /></el-icon></el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="export:oee">导出 CSV · OEE 报表</el-dropdown-item>
                <el-dropdown-item command="export:shift">导出 CSV · 按班次产量良率</el-dropdown-item>
                <el-dropdown-item command="export:quality">导出 CSV · 质量指标 + 不良 TOP</el-dropdown-item>
                <el-dropdown-item command="export:achievement">导出 CSV · 工单达成率</el-dropdown-item>
                <el-dropdown-item command="export:downtime">导出 CSV · 停机 Pareto</el-dropdown-item>
                <el-dropdown-item divided command="print:oee">打印 / 另存 PDF · OEE</el-dropdown-item>
                <el-dropdown-item command="print:shift">打印 / 另存 PDF · 按班次</el-dropdown-item>
                <el-dropdown-item command="print:quality">打印 / 另存 PDF · 质量</el-dropdown-item>
                <el-dropdown-item command="print:achievement">打印 / 另存 PDF · 达成率</el-dropdown-item>
                <el-dropdown-item command="print:downtime">打印 / 另存 PDF · 停机</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </el-form-item>
      </el-form>
    </el-card>

    <el-tabs v-model="activeTab">
      <!-- 指标总览 -->
      <el-tab-pane label="指标总览" name="metrics">
        <div class="metrics">
          <el-card shadow="never" class="oee-card">
            <template #header>
              <div class="card-header">
                <span>OEE = 可用率 × 性能 × 良率</span>
                <el-tag size="small" type="info">{{ query.from }} ~ {{ query.to }}</el-tag>
              </div>
            </template>
            <div class="oee-body">
              <el-progress
                type="dashboard"
                :percentage="Number(oee?.oee ?? 0)"
                :width="220"
                :stroke-width="16"
                :color="oeeColor"
              >
                <template #default="{ percentage }">
                  <div class="oee-value">{{ percentage }}%</div>
                  <div class="oee-label">OEE</div>
                </template>
              </el-progress>

              <div class="oee-factors">
                <div v-for="factor in oeeFactors" :key="factor.label" class="factor">
                  <div class="factor-head">
                    <span>{{ factor.label }}</span>
                    <span class="factor-value" :style="{ color: factor.color }">{{ factor.value }}%</span>
                  </div>
                  <el-progress :percentage="Number(factor.value)" :stroke-width="10" :show-text="false" :color="factor.color" />
                  <div class="factor-hint">{{ factor.hint }}</div>
                </div>
              </div>
            </div>

            <el-descriptions :column="4" size="small" border class="oee-desc">
              <el-descriptions-item label="计划生产时间">{{ oee?.plannedHours ?? 0 }} h</el-descriptions-item>
              <el-descriptions-item label="故障停机">{{ oee?.downtimeHours ?? 0 }} h</el-descriptions-item>
              <el-descriptions-item label="运行时间">{{ oee?.runHours ?? 0 }} h</el-descriptions-item>
              <el-descriptions-item label="投产 / 完工 / 报废">
                {{ oee?.totalSn ?? 0 }} / {{ oee?.completedSn ?? 0 }} / {{ oee?.scrappedSn ?? 0 }}
              </el-descriptions-item>
              <el-descriptions-item label="理论工时">{{ formatHours(oee?.theoreticalSeconds) }}</el-descriptions-item>
              <el-descriptions-item label="实际工时">{{ formatHours(oee?.actualSeconds) }}</el-descriptions-item>
              <el-descriptions-item label="一次合格率 FPY">{{ quality?.fpy ?? 0 }}%</el-descriptions-item>
              <el-descriptions-item label="工单达成率">{{ achievement?.achievementRate ?? 0 }}%</el-descriptions-item>
            </el-descriptions>
          </el-card>

          <el-card shadow="never">
            <template #header><span>按班次汇总（跨天夜班归属其生产日）</span></template>
            <el-table :data="shiftMetrics" v-loading="loading" size="small" stripe>
              <el-table-column prop="productionDate" label="生产日" width="120" />
              <el-table-column prop="shiftCode" label="班次" width="110" />
              <el-table-column label="时间窗" width="200">
                <template #default="{ row }">
                  {{ formatLocal(row.startAtUtc) }} ~ {{ formatLocal(row.endAtUtc) }}
                </template>
              </el-table-column>
              <el-table-column prop="totalSn" label="投产" width="80" />
              <el-table-column prop="completedSn" label="完工" width="80" />
              <el-table-column prop="scrappedSn" label="报废" width="80" />
              <el-table-column label="良率" width="160">
                <template #default="{ row }">
                  <el-progress :percentage="Number(row.yieldRate)" :stroke-width="10" :color="rateColor(row.yieldRate)" />
                </template>
              </el-table-column>
              <el-table-column prop="inspectionTotal" label="检验" width="80" />
              <el-table-column prop="inspectionPassed" label="合格" width="80" />
              <el-table-column prop="inspectionFailed" label="不合格" width="90" />
              <el-table-column label="FPY" width="160">
                <template #default="{ row }">
                  <el-progress :percentage="Number(row.fpy)" :stroke-width="10" :color="rateColor(row.fpy)" />
                </template>
              </el-table-column>
            </el-table>
            <el-empty v-if="!shiftMetrics.length" description="暂无班次数据（请先在「班次配置」中定义班次）" :image-size="70" />
          </el-card>

          <el-row :gutter="16">
            <el-col :span="12">
              <el-card shadow="never">
                <template #header><span>不良 TOP（按不良代码）</span></template>
                <el-table :data="quality?.topDefects || []" size="small" stripe>
                  <el-table-column prop="defectCode" label="不良代码" min-width="150" />
                  <el-table-column prop="count" label="次数" width="90" />
                  <el-table-column label="占比" width="180">
                    <template #default="{ row }">
                      <el-progress :percentage="defectPercent(row.count)" :stroke-width="10" />
                    </template>
                  </el-table-column>
                </el-table>
                <el-empty v-if="!quality?.topDefects?.length" description="暂无不良记录" :image-size="60" />
              </el-card>
            </el-col>
            <el-col :span="12">
              <el-card shadow="never">
                <template #header><span>停机 Pareto（按原因，含时长）</span></template>
                <el-table :data="downtime?.byReason || []" size="small" stripe>
                  <el-table-column prop="reasonCode" label="停机原因" min-width="150" />
                  <el-table-column label="时长" width="110">
                    <template #default="{ row }">{{ Math.round(row.totalSeconds / 60) }} 分</template>
                  </el-table-column>
                  <el-table-column prop="count" label="次数" width="90" />
                  <el-table-column label="占比" width="160">
                    <template #default="{ row }">
                      <el-progress :percentage="downtimePercent(row.totalSeconds)" :stroke-width="10" status="exception" />
                    </template>
                  </el-table-column>
                </el-table>
                <el-empty v-if="!downtime?.byReason?.length" description="暂无停机记录" :image-size="60" />
              </el-card>
            </el-col>
          </el-row>

          <el-card shadow="never">
            <template #header><span>工单达成率（计划 vs 完工）</span></template>
            <el-table :data="achievement?.orders || []" size="small" stripe>
              <el-table-column prop="orderNumber" label="工单号" width="190" />
              <el-table-column prop="productCode" label="产品编码" min-width="150" />
              <el-table-column prop="plannedQuantity" label="计划" width="90" />
              <el-table-column prop="completedQuantity" label="完工" width="90" />
              <el-table-column label="达成率" width="200">
                <template #default="{ row }">
                  <el-progress :percentage="Math.min(Number(row.achievementRate), 100)" :stroke-width="10" :color="rateColor(row.achievementRate)" />
                </template>
              </el-table-column>
              <el-table-column prop="achievementRate" label="%" width="90" />
            </el-table>
            <el-empty v-if="!achievement?.orders?.length" description="该区间没有工单" :image-size="70" />
          </el-card>
        </div>
      </el-tab-pane>

      <!-- 班次配置 -->
      <el-tab-pane label="班次配置" name="shifts">
        <div class="metrics">
          <el-card shadow="never">
            <template #header>
              <div class="card-header">
                <span>班次定义（统计口径基础：跨天夜班归属开始日）</span>
                <el-button type="primary" size="small" :icon="Plus" @click="openShiftForm()">新增班次</el-button>
              </div>
            </template>
            <el-table :data="shifts" v-loading="loading" size="small" stripe>
              <el-table-column prop="code" label="代码" width="120" />
              <el-table-column prop="name" label="名称" min-width="130" />
              <el-table-column label="时间" width="180">
                <template #default="{ row }">{{ row.startTime }} ~ {{ row.endTime }}</template>
              </el-table-column>
              <el-table-column label="跨天" width="90">
                <template #default="{ row }">
                  <el-tag v-if="row.crossesMidnight" type="warning" size="small">跨天夜班</el-tag>
                  <span v-else>-</span>
                </template>
              </el-table-column>
              <el-table-column prop="lineName" label="产线" width="130">
                <template #default="{ row }">{{ row.lineName || '全局' }}</template>
              </el-table-column>
              <el-table-column prop="sequence" label="排序" width="80" />
              <el-table-column label="状态" width="90">
                <template #default="{ row }">
                  <el-tag :type="row.isActive ? 'success' : 'info'" size="small">{{ row.isActive ? '启用' : '停用' }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="180" fixed="right">
                <template #default="{ row }">
                  <el-button size="small" plain @click="openShiftForm(row)">编辑</el-button>
                  <el-button size="small" :type="row.isActive ? 'danger' : 'success'" plain @click="toggleShift(row)">
                    {{ row.isActive ? '停用' : '启用' }}
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card shadow="never">
            <template #header><span>当前班次</span></template>
            <el-descriptions v-if="currentShift" :column="4" border size="small">
              <el-descriptions-item label="生产日">{{ currentShift.productionDate }}</el-descriptions-item>
              <el-descriptions-item label="班次">{{ currentShift.shiftCode }} {{ currentShift.shiftName }}</el-descriptions-item>
              <el-descriptions-item label="时间窗">
                {{ formatLocal(currentShift.startAt) }} ~ {{ formatLocal(currentShift.endAt) }}
              </el-descriptions-item>
              <el-descriptions-item label="时长">{{ currentShift.durationHours }} h</el-descriptions-item>
            </el-descriptions>
          </el-card>
        </div>
      </el-tab-pane>

      <!-- 生产日历 -->
      <el-tab-pane label="生产日历" name="calendar">
        <div class="metrics">
          <el-card shadow="never">
            <template #header>
              <div class="card-header">
                <span>节假日 / 调休（非生产日不计入计划生产时间）</span>
                <el-button type="primary" size="small" :icon="Plus" @click="openCalendarForm">设置日期</el-button>
              </div>
            </template>
            <el-table :data="calendarDays" v-loading="loading" size="small" stripe>
              <el-table-column prop="date" label="日期" width="140" />
              <el-table-column label="类型" width="130">
                <template #default="{ row }">
                  <el-tag :type="row.isWorkingDay ? 'success' : 'danger'" size="small">
                    {{ row.isWorkingDay ? '生产日（调休）' : '非生产日' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="name" label="名称" min-width="150">
                <template #default="{ row }">{{ row.name || '-' }}</template>
              </el-table-column>
              <el-table-column prop="remark" label="备注" min-width="150">
                <template #default="{ row }">{{ row.remark || '-' }}</template>
              </el-table-column>
            </el-table>
            <el-empty v-if="!calendarDays.length" description="暂无日历配置（默认全部按生产日统计）" :image-size="70" />
          </el-card>
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- 班次表单 -->
    <el-dialog v-model="shiftFormVisible" :title="shiftForm.id ? '编辑班次' : '新增班次'" width="520px">
      <el-form :model="shiftForm" label-width="100px">
        <el-form-item label="班次代码" required>
          <el-input v-model="shiftForm.code" :disabled="!!shiftForm.id" placeholder="如 DAY / NIGHT" />
        </el-form-item>
        <el-form-item label="名称" required>
          <el-input v-model="shiftForm.name" placeholder="如 白班" />
        </el-form-item>
        <el-form-item label="开始时间" required>
          <el-time-select v-model="shiftForm.startTime" start="00:00" step="00:30" end="23:30" placeholder="08:30" style="width: 100%" />
        </el-form-item>
        <el-form-item label="结束时间" required>
          <el-time-select v-model="shiftForm.endTime" start="00:00" step="00:30" end="23:30" placeholder="20:30" style="width: 100%" />
        </el-form-item>
        <el-form-item label="产线">
          <el-input v-model="shiftForm.lineName" placeholder="留空表示全局默认班次" />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="shiftForm.sequence" :min="0" style="width: 100%" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="shiftForm.remark" />
        </el-form-item>
      </el-form>
      <p class="hint">结束时间早于或等于开始时间即视为跨天班次（如 20:30 ~ 08:30），其产量归属「开始日」。</p>
      <template #footer>
        <el-button @click="shiftFormVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="saveShift">保存</el-button>
      </template>
    </el-dialog>

    <!-- 日历表单 -->
    <el-dialog v-model="calendarFormVisible" title="设置生产日历" width="460px">
      <el-form :model="calendarForm" label-width="100px">
        <el-form-item label="日期" required>
          <el-date-picker v-model="calendarForm.date" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item label="是否生产日">
          <el-switch v-model="calendarForm.isWorkingDay" active-text="生产日" inactive-text="非生产日（节假日/停产）" />
        </el-form-item>
        <el-form-item label="名称">
          <el-input v-model="calendarForm.name" placeholder="如 国庆节 / 调休上班" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="calendarForm.remark" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="calendarFormVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="saveCalendarDay">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { ArrowDown, Download, Plus, Search } from '@element-plus/icons-vue'
import { reportApi, shiftApi } from '@/api/reporting'

const activeTab = ref('metrics')
const loading = ref(false)
const submitting = ref(false)

const query = reactive({ from: '', to: '', lineName: '' })
const dateRange = ref([])

const oee = ref(null)
const shiftMetrics = ref([])
const quality = ref(null)
const achievement = ref(null)
const downtime = ref(null)
const shifts = ref([])
const currentShift = ref(null)
const calendarDays = ref([])

const shiftFormVisible = ref(false)
const calendarFormVisible = ref(false)
const shiftForm = reactive({
  id: null,
  code: '',
  name: '',
  startTime: '08:30',
  endTime: '20:30',
  lineName: '',
  sequence: 0,
  remark: '',
})
const calendarForm = reactive({ date: '', isWorkingDay: false, name: '', remark: '' })

const oeeColor = computed(() => {
  const value = Number(oee.value?.oee ?? 0)
  if (value >= 85) return '#67c23a'
  if (value >= 70) return '#e6a23c'
  return '#f56c6c'
})

const oeeFactors = computed(() => [
  {
    label: '可用率（运行 / 计划）',
    value: oee.value?.availability ?? 0,
    hint: `故障停机 ${oee.value?.downtimeHours ?? 0} h`,
    color: rateColor(oee.value?.availability),
  },
  {
    label: '性能（理论 / 实际工时）',
    value: oee.value?.performance ?? 0,
    hint: `理论 ${formatHours(oee.value?.theoreticalSeconds)} / 实际 ${formatHours(oee.value?.actualSeconds)}`,
    color: rateColor(oee.value?.performance),
  },
  {
    label: '良率（完工 / 完工+报废）',
    value: oee.value?.quality ?? 0,
    hint: `完工 ${oee.value?.completedSn ?? 0} / 报废 ${oee.value?.scrappedSn ?? 0}`,
    color: rateColor(oee.value?.quality),
  },
])

function rateColor(value) {
  const number = Number(value ?? 0)
  if (number >= 95) return '#67c23a'
  if (number >= 85) return '#e6a23c'
  return '#f56c6c'
}

function formatHours(seconds) {
  if (!seconds) return '0 h'
  return `${(seconds / 3600).toFixed(2)} h`
}

function formatLocal(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

function defectPercent(count) {
  const max = Math.max(...(quality.value?.topDefects || []).map((item) => item.count), 1)
  return Math.round((count / max) * 100)
}

function downtimePercent(seconds) {
  const max = Math.max(...(downtime.value?.byReason || []).map((item) => item.totalSeconds), 1)
  return Math.round((seconds / max) * 100)
}

function quickRange(daysBack) {
  const end = new Date()
  const start = new Date()
  start.setDate(start.getDate() - daysBack)
  const format = (date) => date.toISOString().slice(0, 10)
  dateRange.value = [format(start), format(end)]
  loadAll()
}

async function loadAll() {
  if (dateRange.value?.length === 2) {
    query.from = dateRange.value[0]
    query.to = dateRange.value[1]
  }

  const params = {
    from: query.from || undefined,
    to: query.to || undefined,
    lineName: query.lineName || undefined,
  }

  loading.value = true
  try {
    const [oeeData, shiftData, qualityData, achievementData, downtimeData] = await Promise.all([
      reportApi.oee(params),
      reportApi.shiftMetrics(params),
      reportApi.quality(params),
      reportApi.achievement(params),
      reportApi.downtime(params),
    ])

    oee.value = oeeData
    shiftMetrics.value = shiftData.items
    quality.value = qualityData
    achievement.value = achievementData
    downtime.value = downtimeData
  } finally {
    loading.value = false
  }
}

async function loadShiftConfig() {
  const [shiftData, current] = await Promise.all([
    shiftApi.list({ page: 1, pageSize: 100 }),
    shiftApi.current(query.lineName || undefined),
  ])
  shifts.value = shiftData.items
  currentShift.value = current

  const calendar = await shiftApi.calendar({ page: 1, pageSize: 100 })
  calendarDays.value = calendar.items
}

function openShiftForm(row) {
  Object.assign(shiftForm, {
    id: row?.id || null,
    code: row?.code || '',
    name: row?.name || '',
    startTime: row ? row.startTime.slice(0, 5) : '08:30',
    endTime: row ? row.endTime.slice(0, 5) : '20:30',
    lineName: row?.lineName || '',
    sequence: row?.sequence ?? 0,
    remark: row?.remark || '',
  })
  shiftFormVisible.value = true
}

async function saveShift() {
  if (!shiftForm.code.trim() || !shiftForm.name.trim()) {
    ElMessage.warning('班次代码与名称不能为空')
    return
  }

  const payload = {
    name: shiftForm.name,
    startTime: `${shiftForm.startTime}:00`,
    endTime: `${shiftForm.endTime}:00`,
    lineName: shiftForm.lineName || null,
    sequence: shiftForm.sequence,
    remark: shiftForm.remark || null,
  }

  submitting.value = true
  try {
    if (shiftForm.id) {
      await shiftApi.update(shiftForm.id, payload)
    } else {
      await shiftApi.create({ code: shiftForm.code, ...payload })
    }
    ElMessage.success('保存成功')
    shiftFormVisible.value = false
    await loadShiftConfig()
  } finally {
    submitting.value = false
  }
}

async function toggleShift(row) {
  await shiftApi.setActive(row.id, !row.isActive)
  ElMessage.success(row.isActive ? '已停用' : '已启用')
  await loadShiftConfig()
}

function openCalendarForm() {
  Object.assign(calendarForm, {
    date: new Date().toISOString().slice(0, 10),
    isWorkingDay: false,
    name: '',
    remark: '',
  })
  calendarFormVisible.value = true
}

async function saveCalendarDay() {
  if (!calendarForm.date) {
    ElMessage.warning('请选择日期')
    return
  }

  submitting.value = true
  try {
    await shiftApi.upsertCalendarDay({
      date: calendarForm.date,
      isWorkingDay: calendarForm.isWorkingDay,
      name: calendarForm.name || null,
      remark: calendarForm.remark || null,
    })
    ElMessage.success('日历已更新')
    calendarFormVisible.value = false
    await loadShiftConfig()
  } finally {
    submitting.value = false
  }
}

function handleCommand(command) {
  const [action, type] = String(command).split(':')
  if (action === 'print') {
    printReport(type)
    return
  }
  exportCsv(type)
}

/** 打印 / 另存 PDF：先带 JWT 取回自包含 HTML，再写入新窗口唤起打印（服务端不引入 PDF 库） */
async function printReport(type) {
  const html = await reportApi.printHtml(type, {
    from: query.from || undefined,
    to: query.to || undefined,
    lineName: query.lineName || undefined,
  })

  const target = window.open('', '_blank')
  if (!target) {
    ElMessage.warning('浏览器阻止了新窗口，请允许弹窗后重试')
    return
  }

  target.document.open()
  target.document.write(html)
  target.document.close()

  // 等样式与中文字体就绪再唤起打印，否则容易打出空白页
  setTimeout(() => target.print(), 400)
}

async function exportCsv(type) {
  const blob = await reportApi.exportCsv(type, {
    from: query.from || undefined,
    to: query.to || undefined,
    lineName: query.lineName || undefined,
  })

  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `report-${type}-${new Date().toISOString().slice(0, 10)}.csv`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
  ElMessage.success('已导出 CSV')
}

onMounted(async () => {
  quickRangeWithoutLoad(6)
  await Promise.all([loadAll(), loadShiftConfig()])
})

function quickRangeWithoutLoad(daysBack) {
  const end = new Date()
  const start = new Date()
  start.setDate(start.getDate() - daysBack)
  const format = (date) => date.toISOString().slice(0, 10)
  dateRange.value = [format(start), format(end)]
  query.from = format(start)
  query.to = format(end)
}
</script>

<style scoped>
.reports-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.filter-card :deep(.el-form-item) {
  margin-bottom: 0;
}

.metrics {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.oee-body {
  display: flex;
  gap: 32px;
  align-items: center;
}

.oee-value {
  font-size: 40px;
  font-weight: 800;
}

.oee-label {
  font-size: 13px;
  color: #909399;
}

.oee-factors {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.factor-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 14px;
  margin-bottom: 4px;
}

.factor-value {
  font-weight: 700;
}

.factor-hint {
  font-size: 12px;
  color: #909399;
  margin-top: 4px;
}

.oee-desc {
  margin-top: 16px;
}

.hint {
  color: #909399;
  font-size: 12px;
  margin: 0 0 0 100px;
}
</style>
