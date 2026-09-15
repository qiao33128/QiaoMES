<template>
  <div class="trace-page">
    <el-tabs v-model="activeTab">
      <el-tab-pane label="SN 追溯" name="sn">
        <el-card shadow="never" class="filter-card">
          <el-form :inline="true">
            <el-form-item label="SN">
              <el-input
                v-model="snInput"
                placeholder="输入序列号，如 WO-20260915-0001-0001"
                clearable
                style="width: 320px"
                @keyup.enter="handleQuerySn"
              />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :icon="Search" :loading="loading" @click="handleQuerySn">查询追溯</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-empty v-if="!report" description="输入 SN 查看「人机料法环」完整追溯报告" :image-size="90" />

        <div v-else class="report">
          <!-- SN 概要 -->
          <el-card shadow="never">
            <template #header>
              <div class="card-header">
                <span>产品身份</span>
                <el-tag size="small">{{ report.sn }}</el-tag>
              </div>
            </template>
            <el-descriptions :column="3" border size="small">
              <el-descriptions-item label="工单号">{{ report.workOrder?.orderNumber || '-' }}</el-descriptions-item>
              <el-descriptions-item label="产品">{{ report.workOrder?.productCode }} {{ report.workOrder?.productName }}</el-descriptions-item>
              <el-descriptions-item label="当前工序">
                {{ report.serialNumber?.serialNumber.currentOperationName || '不在工序中' }}
              </el-descriptions-item>
              <el-descriptions-item label="工艺路线版本">{{ report.workOrder?.routingVersion || '-' }}</el-descriptions-item>
              <el-descriptions-item label="BOM 版本">{{ report.workOrder?.bomVersion || '-' }}</el-descriptions-item>
              <el-descriptions-item label="SN 状态">
                {{ SerialNumberStatusMap[report.serialNumber?.serialNumber.status]?.label || '-' }}
              </el-descriptions-item>
            </el-descriptions>
          </el-card>

          <!-- 法：工艺路线 -->
          <el-card shadow="never">
            <template #header><span>法 · 工艺路线（{{ report.routing?.version }}）</span></template>
            <el-table :data="report.routing?.steps || []" size="small" border>
              <el-table-column prop="sequence" label="顺序" width="70" />
              <el-table-column prop="operationCode" label="工序编码" width="120" />
              <el-table-column prop="operationName" label="工序" min-width="140" />
              <el-table-column prop="workCenterName" label="工作中心" width="130">
                <template #default="{ row }">{{ row.workCenterName || '-' }}</template>
              </el-table-column>
              <el-table-column prop="standardSeconds" label="标准工时(秒)" width="120" />
              <el-table-column label="质检点" width="90">
                <template #default="{ row }">{{ row.isQualityGate ? '是' : '否' }}</template>
              </el-table-column>
            </el-table>
          </el-card>

          <!-- 机 / 人：过站轨迹 -->
          <el-card shadow="never">
            <template #header>
              <span>机与人 · 过站轨迹（{{ report.serialNumber?.trackings.length || 0 }}）</span>
            </template>
            <el-timeline v-if="report.serialNumber?.trackings.length">
              <el-timeline-item
                v-for="item in report.serialNumber.trackings"
                :key="item.id"
                :timestamp="formatTime(item.trackedAt)"
                :type="item.result === 2 ? 'danger' : item.action === 0 ? 'primary' : 'success'"
              >
                {{ item.operationName }} · {{ WipActionMap[item.action] }}
                <span v-if="item.result">（{{ WipResultMap[item.result] }}）</span>
                <div class="desc">设备：{{ item.equipmentId || '未记录' }}</div>
              </el-timeline-item>
            </el-timeline>
            <el-empty v-else description="暂无过站记录" :image-size="70" />
          </el-card>

          <!-- 料：BOM -->
          <el-card shadow="never">
            <template #header><span>料 · BOM（{{ report.bom?.version }}）</span></template>
            <el-table :data="report.bom?.items || []" size="small" border>
              <el-table-column prop="materialCode" label="物料编码" width="150" />
              <el-table-column prop="materialName" label="物料名称" min-width="160" />
              <el-table-column prop="quantity" label="单位用量" width="110" />
              <el-table-column prop="lossRate" label="损耗率" width="100" />
              <el-table-column prop="requiredQuantity" label="应领用量" width="110" />
            </el-table>
          </el-card>

          <!-- 环：质量记录 -->
          <el-card shadow="never">
            <template #header>
              <span>环 · 质量记录（检验 {{ report.inspections.length }} / 处置 {{ report.nonconformances.length }}）</span>
            </template>

            <el-table :data="report.inspections" size="small" border>
              <el-table-column prop="inspectionNumber" label="检验单号" width="180" />
              <el-table-column label="类型" width="100">
                <template #default="{ row }">{{ InspectionTypeMap[row.type] }}</template>
              </el-table-column>
              <el-table-column label="状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="InspectionStatusMap[row.status]?.type" size="small">
                    {{ InspectionStatusMap[row.status]?.label }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="inspectorName" label="检验员" width="110">
                <template #default="{ row }">{{ row.inspectorName || '-' }}</template>
              </el-table-column>
              <el-table-column label="不合格项" width="90" prop="failedItemCount" />
              <el-table-column label="时间" width="170">
                <template #default="{ row }">{{ formatTime(row.inspectedAt || row.createdAt) }}</template>
              </el-table-column>
            </el-table>

            <el-table :data="report.nonconformances" size="small" border class="mt-12">
              <el-table-column prop="nonconformanceNumber" label="处置单号" width="180" />
              <el-table-column label="不良代码" width="120">
                <template #default="{ row }">{{ row.defectCode || '-' }}</template>
              </el-table-column>
              <el-table-column label="处置方式" width="110">
                <template #default="{ row }">{{ row.disposition !== null ? DispositionTypeMap[row.disposition] : '-' }}</template>
              </el-table-column>
              <el-table-column label="状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="DispositionStatusMap[row.status]?.type" size="small">
                    {{ DispositionStatusMap[row.status]?.label }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="defectDescription" label="不良描述" min-width="160" />
              <el-table-column label="维修次数" width="100">
                <template #default="{ row }">{{ row.repairs.length }}</template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="批次影响范围" name="batch">
        <el-card shadow="never" class="filter-card">
          <el-form :inline="true">
            <el-form-item label="工单">
              <el-select v-model="workOrderId" filterable placeholder="选择工单" style="width: 320px">
                <el-option
                  v-for="order in workOrders"
                  :key="order.id"
                  :label="`${order.orderNumber} ${order.productCode}（计划 ${order.plannedQuantity}）`"
                  :value="order.id"
                />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :icon="Search" :loading="loading" @click="handleQueryBatch">查询影响范围</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-empty v-if="!batch" description="选择工单，查看该批次全部 SN 的影响范围" :image-size="90" />

        <div v-else>
          <el-row :gutter="12" class="summary-row">
            <el-col :span="4"><el-card shadow="never" class="summary-card"><div class="summary-value">{{ batch.totalCount }}</div><div class="summary-label">SN 总数</div></el-card></el-col>
            <el-col :span="4"><el-card shadow="never" class="summary-card"><div class="summary-value ok">{{ batch.completedCount }}</div><div class="summary-label">已完工</div></el-card></el-col>
            <el-col :span="4"><el-card shadow="never" class="summary-card"><div class="summary-value danger">{{ batch.scrappedCount }}</div><div class="summary-label">已报废</div></el-card></el-col>
            <el-col :span="4"><el-card shadow="never" class="summary-card"><div class="summary-value warn">{{ batch.nonconformanceCount }}</div><div class="summary-label">处置单</div></el-card></el-col>
            <el-col :span="4"><el-card shadow="never" class="summary-card"><div class="summary-value warn">{{ batch.inspectionFailCount }}</div><div class="summary-label">不合格检验</div></el-card></el-col>
          </el-row>

          <el-card shadow="never">
            <template #header><span>SN 明细（{{ batch.serialNumbers.length }}）</span></template>
            <el-table :data="batch.serialNumbers" size="small" stripe>
              <el-table-column prop="sn" label="SN" min-width="220" />
              <el-table-column label="当前工序" width="130">
                <template #default="{ row }">{{ row.currentOperationName || '-' }}</template>
              </el-table-column>
              <el-table-column label="状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="SerialNumberStatusMap[row.status]?.type" size="small">
                    {{ SerialNumberStatusMap[row.status]?.label }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="lastCompletedOperationName" label="最后完成工序" width="140">
                <template #default="{ row }">{{ row.lastCompletedOperationName || '-' }}</template>
              </el-table-column>
              <el-table-column label="操作" width="110">
                <template #default="{ row }">
                  <el-button size="small" plain @click="jumpToSn(row.sn)">查看追溯</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Search } from '@element-plus/icons-vue'
import { traceabilityApi } from '@/api/traceability'
import { workOrderApi } from '@/api/workorder'
import { SerialNumberStatusMap, WipActionMap, WipResultMap } from '@/api/production'
import { DispositionStatusMap, DispositionTypeMap, InspectionStatusMap, InspectionTypeMap } from '@/api/quality'

const activeTab = ref('sn')
const loading = ref(false)
const snInput = ref('')
const report = ref(null)

const workOrders = ref([])
const workOrderId = ref(null)
const batch = ref(null)

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

async function handleQuerySn() {
  if (!snInput.value.trim()) {
    ElMessage.warning('请输入 SN')
    return
  }

  loading.value = true
  try {
    report.value = await traceabilityApi.bySn(snInput.value.trim())
  } catch {
    report.value = null
  } finally {
    loading.value = false
  }
}

async function handleQueryBatch() {
  if (!workOrderId.value) {
    ElMessage.warning('请选择工单')
    return
  }

  loading.value = true
  try {
    batch.value = await traceabilityApi.byWorkOrder(workOrderId.value)
  } finally {
    loading.value = false
  }
}

function jumpToSn(sn) {
  activeTab.value = 'sn'
  snInput.value = sn
  handleQuerySn()
}

onMounted(async () => {
  const data = await workOrderApi.list({ page: 1, pageSize: 100 })
  workOrders.value = data.items.filter((item) => item.status >= 1)
})
</script>

<style scoped>
.trace-page {
  display: flex;
  flex-direction: column;
}

.filter-card :deep(.el-form-item) {
  margin-bottom: 0;
}

.report {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.desc {
  color: #909399;
  font-size: 12px;
}

.mt-12 {
  margin-top: 12px;
}

.summary-row {
  margin-bottom: 16px;
}

.summary-card {
  text-align: center;
}

.summary-value {
  font-size: 22px;
  font-weight: 700;
}

.summary-value.ok {
  color: #67c23a;
}

.summary-value.danger {
  color: #f56c6c;
}

.summary-value.warn {
  color: #e6a23c;
}

.summary-label {
  font-size: 12px;
  color: #909399;
}
</style>
