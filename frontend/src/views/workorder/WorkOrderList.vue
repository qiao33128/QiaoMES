<template>
  <div class="workorder-page">
    <!-- 筛选工具栏 -->
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="状态">
          <el-select v-model="query.status" placeholder="全部状态" clearable style="width: 140px">
            <el-option
              v-for="(item, key) in WorkOrderStatusMap"
              :key="key"
              :label="item.label"
              :value="Number(key)"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="工单号/产品编码/产品名称"
            clearable
            style="width: 220px"
            @keyup.enter="loadData"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
          <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-button type="success" :icon="Plus" @click="openCreateDialog">新建工单</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 工单列表 -->
    <el-card shadow="never">
      <el-table :data="list" v-loading="loading" stripe>
        <el-table-column prop="orderNumber" label="工单号" width="180" />
        <el-table-column prop="productCode" label="产品编码" width="120" />
        <el-table-column prop="productName" label="产品名称" min-width="150" />
        <el-table-column prop="workCenter" label="工作中心" width="110" />
        <el-table-column label="进度" width="160">
          <template #default="{ row }">
            <div class="progress-cell">
              <el-progress
                :percentage="progressPercent(row)"
                :status="progressStatus(row)"
              />
              <span class="progress-text">{{ row.completedQuantity }} / {{ row.plannedQuantity }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="WorkOrderStatusMap[row.status]?.type">
              {{ WorkOrderStatusMap[row.status]?.label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="170">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="260" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" plain @click="openReportDialog(row)">报工</el-button>
            <el-button size="small" type="success" plain :disabled="row.status !== 0" @click="handleRelease(row)">下达</el-button>
            <el-button size="small" type="warning" plain :disabled="row.status !== 1" @click="handleStart(row)">开始</el-button>
            <el-dropdown trigger="click" @command="(cmd) => handleMore(cmd, row)">
              <el-button size="small" plain>
                更多<el-icon class="el-icon--right"><ArrowDown /></el-icon>
              </el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item command="complete" :disabled="row.status !== 2">完成</el-dropdown-item>
                  <el-dropdown-item command="cancel" :disabled="![0,1,2].includes(row.status)">取消</el-dropdown-item>
                  <el-dropdown-item divided command="delete">删除</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        class="pagination"
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[10, 20, 50]"
        layout="total, sizes, prev, pager, next"
        @change="loadData"
      />
    </el-card>

    <!-- 新建工单对话框 -->
    <el-dialog v-model="createVisible" title="新建工单" width="520px">
      <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="90px">
        <el-form-item label="产品编码" prop="productCode">
          <el-input v-model="createForm.productCode" placeholder="如 P001" />
        </el-form-item>
        <el-form-item label="产品名称" prop="productName">
          <el-input v-model="createForm.productName" placeholder="产品名称" />
        </el-form-item>
        <el-form-item label="计划数量" prop="plannedQuantity">
          <el-input-number v-model="createForm.plannedQuantity" :min="1" :max="100000" />
        </el-form-item>
        <el-form-item label="工作中心">
          <el-input v-model="createForm.workCenter" placeholder="如 WC-01" />
        </el-form-item>
        <el-form-item label="计划开始">
          <el-date-picker v-model="createForm.plannedStart" type="datetime" placeholder="选择日期时间" />
        </el-form-item>
        <el-form-item label="计划结束">
          <el-date-picker v-model="createForm.plannedEnd" type="datetime" placeholder="选择日期时间" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="createForm.remark" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleCreate">确定</el-button>
      </template>
    </el-dialog>

    <!-- 报工对话框 -->
    <el-dialog v-model="reportVisible" title="生产报工" width="420px">
      <el-form label-width="90px">
        <el-form-item label="工单号">
          <el-input :model-value="currentRow?.orderNumber" disabled />
        </el-form-item>
        <el-form-item label="已完成">
          <el-input :model-value="`${currentRow?.completedQuantity} / ${currentRow?.plannedQuantity}`" disabled />
        </el-form-item>
        <el-form-item label="本次数量">
          <el-input-number v-model="reportQuantity" :min="1" :max="remainQuantity" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="reportVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleReport">确认报工</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Search, Refresh, Plus, ArrowDown } from '@element-plus/icons-vue'
import { workOrderApi, WorkOrderStatusMap } from '@/api/workorder'

const loading = ref(false)
const list = ref([])
const total = ref(0)

const query = reactive({
  page: 1,
  pageSize: 20,
  status: null,
  keyword: '',
})

const createVisible = ref(false)
const reportVisible = ref(false)
const submitting = ref(false)
const createFormRef = ref()
const currentRow = ref(null)
const reportQuantity = ref(1)

const createForm = reactive({
  productCode: '',
  productName: '',
  plannedQuantity: 10,
  workCenter: '',
  plannedStart: null,
  plannedEnd: null,
  remark: '',
})

const createRules = {
  productCode: [{ required: true, message: '请输入产品编码', trigger: 'blur' }],
  productName: [{ required: true, message: '请输入产品名称', trigger: 'blur' }],
}

const remainQuantity = computed(
  () => (currentRow.value ? currentRow.value.plannedQuantity - currentRow.value.completedQuantity : 1)
)

function progressPercent(row) {
  if (!row.plannedQuantity) return 0
  return Math.min(100, Math.round((row.completedQuantity / row.plannedQuantity) * 100))
}

function progressStatus(row) {
  if (row.status === 3) return 'success'
  if (row.status === 4) return 'exception'
  if (row.status === 2) return 'warning'
  return undefined
}

function formatTime(time) {
  if (!time) return '-'
  const d = new Date(time)
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

async function loadData() {
  loading.value = true
  try {
    const data = await workOrderApi.list({
      page: query.page,
      pageSize: query.pageSize,
      status: query.status ?? undefined,
      keyword: query.keyword || undefined,
    })
    list.value = data.items
    total.value = data.totalCount
  } catch (e) {
    // 错误已统一处理
  } finally {
    loading.value = false
  }
}

function handleSearch() {
  query.page = 1
  loadData()
}

function resetQuery() {
  query.status = null
  query.keyword = ''
  query.page = 1
  loadData()
}

function openCreateDialog() {
  Object.assign(createForm, {
    productCode: '',
    productName: '',
    plannedQuantity: 10,
    workCenter: '',
    plannedStart: null,
    plannedEnd: null,
    remark: '',
  })
  createVisible.value = true
}

async function handleCreate() {
  await createFormRef.value.validate()
  submitting.value = true
  try {
    await workOrderApi.create({
      ...createForm,
      plannedStart: createForm.plannedStart ? new Date(createForm.plannedStart).toISOString() : null,
      plannedEnd: createForm.plannedEnd ? new Date(createForm.plannedEnd).toISOString() : null,
    })
    ElMessage.success('工单创建成功')
    createVisible.value = false
    loadData()
  } finally {
    submitting.value = false
  }
}

async function handleRelease(row) {
  await ElMessageBox.confirm(`确定下达工单 ${row.orderNumber} 吗？`, '提示', { type: 'warning' })
  await workOrderApi.release(row.id)
  ElMessage.success('工单已下达')
  loadData()
}

async function handleStart(row) {
  await ElMessageBox.confirm(`确定开始生产工单 ${row.orderNumber} 吗？`, '提示', { type: 'warning' })
  await workOrderApi.start(row.id)
  ElMessage.success('生产已开始')
  loadData()
}

function openReportDialog(row) {
  currentRow.value = row
  reportQuantity.value = 1
  reportVisible.value = true
}

async function handleReport() {
  submitting.value = true
  try {
    await workOrderApi.report(currentRow.value.id, reportQuantity.value)
    ElMessage.success('报工成功')
    reportVisible.value = false
    loadData()
  } finally {
    submitting.value = false
  }
}

async function handleMore(command, row) {
  if (command === 'complete') {
    await ElMessageBox.confirm(`确定完成工单 ${row.orderNumber} 吗？`, '提示', { type: 'warning' })
    await workOrderApi.complete(row.id)
    ElMessage.success('工单已完成')
  } else if (command === 'cancel') {
    await ElMessageBox.confirm(`确定取消工单 ${row.orderNumber} 吗？`, '提示', { type: 'warning' })
    await workOrderApi.cancel(row.id)
    ElMessage.success('工单已取消')
  }
  loadData()
}

onMounted(loadData)
</script>

<style scoped>
.workorder-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.filter-card :deep(.el-form-item) {
  margin-bottom: 0;
}

.progress-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.progress-text {
  font-size: 12px;
  color: #909399;
  white-space: nowrap;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
