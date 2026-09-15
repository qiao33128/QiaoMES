<template>
  <div class="sn-page">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="SN">
          <el-input
            v-model="query.keyword"
            placeholder="序列号前缀"
            clearable
            style="width: 220px"
            @keyup.enter="handleSearch"
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="query.status" placeholder="全部" clearable style="width: 130px">
            <el-option v-for="(item, key) in SerialNumberStatusMap" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="工单">
          <el-select v-model="query.workOrderId" placeholder="全部" clearable filterable style="width: 220px">
            <el-option
              v-for="order in workOrders"
              :key="order.id"
              :label="`${order.orderNumber} ${order.productCode}`"
              :value="order.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
          <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-button type="primary" :icon="Plus" @click="openGenerate">批量生成 SN</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-row :gutter="16">
      <el-col :span="13">
        <el-card shadow="never">
          <el-table :data="list" v-loading="loading" stripe highlight-current-row @row-click="handleSelect">
            <el-table-column prop="sn" label="SN" min-width="200" />
            <el-table-column prop="productCode" label="产品" width="130" />
            <el-table-column label="当前工序" min-width="120">
              <template #default="{ row }">{{ row.currentOperationName || '-' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="SerialNumberStatusMap[row.status]?.type" size="small">
                  {{ SerialNumberStatusMap[row.status]?.label }}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>

          <el-pagination
            class="pagination"
            layout="total, prev, pager, next"
            :total="total"
            v-model:current-page="query.page"
            v-model:page-size="query.pageSize"
            @current-change="loadData"
          />
        </el-card>
      </el-col>

      <el-col :span="11">
        <el-card shadow="never">
          <template #header>
            <div class="detail-header">
              <span>过站 / 追溯</span>
              <el-tag v-if="detail" size="small">{{ detail.serialNumber.sn }}</el-tag>
            </div>
          </template>

          <el-empty v-if="!detail" description="点击左侧 SN 查看轨迹" :image-size="80" />

          <div v-else>
            <el-descriptions :column="1" size="small" border>
              <el-descriptions-item label="工单">{{ detail.serialNumber.orderNumber }}</el-descriptions-item>
              <el-descriptions-item label="产品">{{ detail.serialNumber.productCode }}</el-descriptions-item>
              <el-descriptions-item label="当前工序">
                {{ detail.serialNumber.currentOperationName || '不在工序中' }}
              </el-descriptions-item>
              <el-descriptions-item label="状态">
                {{ SerialNumberStatusMap[detail.serialNumber.status]?.label }}
              </el-descriptions-item>
            </el-descriptions>

            <div class="track-actions">
              <el-select v-model="trackForm.operationTaskId" placeholder="选择工序" style="width: 200px">
                <el-option
                  v-for="op in operations"
                  :key="op.id"
                  :label="`${op.sequence} · ${op.operationName}`"
                  :value="op.id"
                />
              </el-select>
              <el-button type="primary" :loading="submitting" @click="handleTrackIn">进站</el-button>
              <el-button type="success" :loading="submitting" @click="handleTrackOut(1)">出站(合格)</el-button>
              <el-button type="danger" :loading="submitting" @click="handleTrackOut(2)">出站(不合格)</el-button>
            </div>

            <el-divider content-position="left">过站轨迹（{{ detail.trackings.length }}）</el-divider>

            <el-timeline>
              <el-timeline-item
                v-for="item in detail.trackings"
                :key="item.id"
                :timestamp="formatTime(item.trackedAt)"
                :type="item.result === 2 ? 'danger' : item.action === 0 ? 'primary' : 'success'"
              >
                {{ item.operationName }} · {{ WipActionMap[item.action] }}
                <span v-if="item.result">（{{ WipResultMap[item.result] }}）</span>
              </el-timeline-item>
            </el-timeline>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="generateVisible" title="批量生成 SN" width="440px">
      <el-form label-width="80px">
        <el-form-item label="工单">
          <el-select v-model="generateForm.workOrderId" filterable placeholder="选择已下达的工单" style="width: 100%">
            <el-option
              v-for="order in workOrders"
              :key="order.id"
              :label="`${order.orderNumber} ${order.productCode}（计划 ${order.plannedQuantity}）`"
              :value="order.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="数量">
          <el-input-number v-model="generateForm.quantity" :min="1" :max="500" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="generateVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleGenerate">生成</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { serialNumberApi, SerialNumberStatusMap, WipActionMap, WipResultMap } from '@/api/production'
import { workOrderApi } from '@/api/workorder'

const list = ref([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)
const workOrders = ref([])
const detail = ref(null)
const operations = ref([])

const query = reactive({ page: 1, pageSize: 20, keyword: '', status: null, workOrderId: null })
const generateVisible = ref(false)
const generateForm = reactive({ workOrderId: null, quantity: 10 })
const trackForm = reactive({ operationTaskId: null })

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

async function loadData() {
  loading.value = true
  try {
    const data = await serialNumberApi.list({
      page: query.page,
      pageSize: query.pageSize,
      keyword: query.keyword || undefined,
      status: query.status ?? undefined,
      workOrderId: query.workOrderId ?? undefined,
    })
    list.value = data.items
    total.value = data.totalCount
  } finally {
    loading.value = false
  }
}

async function loadWorkOrders() {
  const data = await workOrderApi.list({ page: 1, pageSize: 100 })
  // 只保留已下达 / 生产中 / 已完成的工单（草稿不能生成 SN）
  workOrders.value = data.items.filter((item) => item.status >= 1)
}

function handleSearch() {
  query.page = 1
  loadData()
}

function resetQuery() {
  query.keyword = ''
  query.status = null
  query.workOrderId = null
  handleSearch()
}

function openGenerate() {
  generateForm.workOrderId = null
  generateForm.quantity = 10
  generateVisible.value = true
}

async function handleGenerate() {
  if (!generateForm.workOrderId) {
    ElMessage.warning('请选择工单')
    return
  }

  submitting.value = true
  try {
    const created = await serialNumberApi.generate({
      workOrderId: generateForm.workOrderId,
      quantity: generateForm.quantity,
    })
    ElMessage.success(`已生成 ${created.length} 个 SN`)
    generateVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

async function handleSelect(row) {
  detail.value = await serialNumberApi.getBySn(row.sn)
  trackForm.operationTaskId = row.currentOperationTaskId || null

  // 过站需要工序任务列表
  const order = await workOrderApi.getById(row.workOrderId)
  operations.value = order.operations || []
}

async function refreshDetail(sn) {
  detail.value = await serialNumberApi.getBySn(sn)
  await loadData()
}

async function handleTrackIn() {
  if (!trackForm.operationTaskId) {
    ElMessage.warning('请选择工序')
    return
  }

  submitting.value = true
  try {
    const sn = detail.value.serialNumber.sn
    await serialNumberApi.trackIn(sn, { operationTaskId: trackForm.operationTaskId })
    ElMessage.success('进站成功')
    await refreshDetail(sn)
  } finally {
    submitting.value = false
  }
}

async function handleTrackOut(result) {
  if (!trackForm.operationTaskId) {
    ElMessage.warning('请选择工序')
    return
  }

  submitting.value = true
  try {
    const sn = detail.value.serialNumber.sn
    await serialNumberApi.trackOut(sn, { operationTaskId: trackForm.operationTaskId, result })
    ElMessage.success(result === 1 ? '出站成功（合格）' : '已记录不合格，SN 停留在本工序')
    await refreshDetail(sn)
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  await Promise.all([loadData(), loadWorkOrders()])
})
</script>

<style scoped>
.sn-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.filter-card :deep(.el-form-item) {
  margin-bottom: 0;
}

.detail-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.track-actions {
  display: flex;
  gap: 8px;
  margin: 16px 0;
  flex-wrap: wrap;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
