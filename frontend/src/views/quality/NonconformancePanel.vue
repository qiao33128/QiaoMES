<template>
  <div class="panel">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="状态">
          <el-select v-model="query.status" placeholder="全部" clearable style="width: 130px">
            <el-option v-for="(item, key) in DispositionStatusMap" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="关键字">
          <el-input v-model="query.keyword" placeholder="单号 / SN / 不良代码" clearable style="width: 200px" @keyup.enter="handleSearch" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
          <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-button type="primary" :icon="Plus" @click="openCreate">新建处置单</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="list" v-loading="loading" stripe>
        <el-table-column prop="nonconformanceNumber" label="处置单号" width="180" />
        <el-table-column label="SN" width="150">
          <template #default="{ row }">{{ row.sn || '-' }}</template>
        </el-table-column>
        <el-table-column label="不良" min-width="160">
          <template #default="{ row }">
            <el-tag v-if="row.defectCode" size="small" type="danger">{{ row.defectCode }}</el-tag>
            <span class="desc">{{ row.defectDescription || '' }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="quantity" label="数量" width="70" />
        <el-table-column label="处置方式" width="110">
          <template #default="{ row }">
            <el-tag v-if="row.disposition !== null" size="small">{{ DispositionTypeMap[row.disposition] }}</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="DispositionStatusMap[row.status]?.type" size="small">
              {{ DispositionStatusMap[row.status]?.label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="维修记录" width="100">
          <template #default="{ row }">{{ row.repairs.length }} 次</template>
        </el-table-column>
        <el-table-column label="操作" width="290" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 0" size="small" type="primary" plain @click="openDecide(row)">决定处置</el-button>
            <template v-if="row.status === 1">
              <el-button size="small" type="primary" plain @click="openRepair(row)">登记维修</el-button>
              <el-button size="small" type="success" plain @click="openComplete(row)">完成维修</el-button>
              <el-button size="small" type="danger" plain @click="handleScrap(row)">报废</el-button>
            </template>
            <template v-if="row.status === 2">
              <el-button size="small" type="success" plain @click="handleReinspect(row, true)">复检合格</el-button>
              <el-button size="small" type="danger" plain @click="handleReinspect(row, false)">复检不合格</el-button>
            </template>
            <el-button size="small" plain @click="openHistory(row)">维修记录</el-button>
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

    <!-- 新建 -->
    <el-dialog v-model="createVisible" title="新建不合格处置单" width="520px">
      <el-form :model="createForm" label-width="90px">
        <el-form-item label="SN">
          <el-input v-model="createForm.sn" placeholder="单颗不良填 SN" />
        </el-form-item>
        <el-form-item label="不良代码">
          <el-input v-model="createForm.defectCode" placeholder="如 D-SIZE" />
        </el-form-item>
        <el-form-item label="不良描述">
          <el-input v-model="createForm.defectDescription" />
        </el-form-item>
        <el-form-item label="数量">
          <el-input-number v-model="createForm.quantity" :min="1" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleCreate">创建</el-button>
      </template>
    </el-dialog>

    <!-- 决定处置 -->
    <el-dialog v-model="decideVisible" title="决定处置方式" width="480px">
      <el-form label-width="100px">
        <el-form-item label="处置方式" required>
          <el-select v-model="decideForm.disposition" style="width: 100%">
            <el-option v-for="(label, key) in DispositionTypeMap" :key="key" :label="label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="需要复检">
          <el-switch v-model="decideForm.needReinspect" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="decideForm.remark" />
        </el-form-item>
      </el-form>
      <p class="hint">让步接收 / 报废 / 退货会直接关闭处置单；返工 / 返修进入处理中。</p>
      <template #footer>
        <el-button @click="decideVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleDecide">确定</el-button>
      </template>
    </el-dialog>

    <!-- 登记维修 -->
    <el-dialog v-model="repairVisible" title="登记维修 / 返工" width="520px">
      <el-form label-width="90px">
        <el-form-item label="维修内容" required>
          <el-input v-model="repairForm.description" type="textarea" :rows="3" placeholder="如 重新补焊并清洗" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="repairVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleStartRepair">提交</el-button>
      </template>
    </el-dialog>

    <!-- 完成维修 -->
    <el-dialog v-model="completeVisible" title="完成维修" width="520px">
      <el-form label-width="90px">
        <el-form-item label="维修结果">
          <el-input v-model="completeForm.result" type="textarea" :rows="3" placeholder="如 补焊完成，外观正常" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="completeVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleCompleteRepair">提交</el-button>
      </template>
    </el-dialog>

    <!-- 维修记录 -->
    <el-dialog v-model="historyVisible" title="维修记录" width="620px">
      <el-timeline v-if="current?.repairs?.length">
        <el-timeline-item
          v-for="repair in current.repairs"
          :key="repair.id"
          :timestamp="formatTime(repair.completedAt || repair.startedAt)"
          :type="repair.completedAt ? 'success' : 'primary'"
        >
          <div>{{ repair.description }}</div>
          <div v-if="repair.result" class="desc">结果：{{ repair.result }}</div>
        </el-timeline-item>
      </el-timeline>
      <el-empty v-else description="暂无维修记录" :image-size="70" />
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { DispositionStatusMap, DispositionTypeMap, nonconformanceApi } from '@/api/quality'

const list = ref([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)

const query = reactive({ page: 1, pageSize: 20, status: null, keyword: '' })
const createVisible = ref(false)
const decideVisible = ref(false)
const repairVisible = ref(false)
const completeVisible = ref(false)
const historyVisible = ref(false)
const current = ref(null)

const createForm = reactive({ sn: '', defectCode: '', defectDescription: '', quantity: 1 })
const decideForm = reactive({ disposition: 1, needReinspect: true, remark: '' })
const repairForm = reactive({ description: '' })
const completeForm = reactive({ result: '' })

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

async function loadData() {
  loading.value = true
  try {
    const data = await nonconformanceApi.list({
      page: query.page,
      pageSize: query.pageSize,
      status: query.status ?? undefined,
      keyword: query.keyword || undefined,
    })
    list.value = data.items
    total.value = data.totalCount
  } finally {
    loading.value = false
  }
}

async function refreshCurrent() {
  if (!current.value) return
  current.value = await nonconformanceApi.getById(current.value.id)
  await loadData()
}

function handleSearch() {
  query.page = 1
  loadData()
}

function resetQuery() {
  query.status = null
  query.keyword = ''
  handleSearch()
}

function openCreate() {
  Object.assign(createForm, { sn: '', defectCode: '', defectDescription: '', quantity: 1 })
  createVisible.value = true
}

async function handleCreate() {
  submitting.value = true
  try {
    await nonconformanceApi.create({
      quantity: createForm.quantity,
      sn: createForm.sn || null,
      defectCode: createForm.defectCode || null,
      defectDescription: createForm.defectDescription || null,
    })
    ElMessage.success('处置单已创建')
    createVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

function openDecide(row) {
  current.value = row
  Object.assign(decideForm, { disposition: 1, needReinspect: true, remark: '' })
  decideVisible.value = true
}

async function handleDecide() {
  submitting.value = true
  try {
    await nonconformanceApi.decide(current.value.id, {
      disposition: decideForm.disposition,
      needReinspect: decideForm.needReinspect,
      remark: decideForm.remark || null,
    })
    ElMessage.success('处置方式已确定')
    decideVisible.value = false
    await refreshCurrent()
  } finally {
    submitting.value = false
  }
}

function openRepair(row) {
  current.value = row
  repairForm.description = ''
  repairVisible.value = true
}

async function handleStartRepair() {
  if (!repairForm.description.trim()) {
    ElMessage.warning('请填写维修内容')
    return
  }

  submitting.value = true
  try {
    await nonconformanceApi.startRepair(current.value.id, { description: repairForm.description })
    ElMessage.success('维修已登记')
    repairVisible.value = false
    await refreshCurrent()
  } finally {
    submitting.value = false
  }
}

async function openComplete(row) {
  current.value = await nonconformanceApi.getById(row.id)
  const pending = current.value.repairs.find((repair) => !repair.completedAt)
  if (!pending) {
    ElMessage.warning('没有待完成的维修记录，请先登记维修')
    return
  }
  current.value.pendingRepairId = pending.id
  completeForm.result = ''
  completeVisible.value = true
}

async function handleCompleteRepair() {
  submitting.value = true
  try {
    await nonconformanceApi.completeRepair(current.value.id, {
      repairId: current.value.pendingRepairId,
      result: completeForm.result || null,
    })
    ElMessage.success('维修已完成')
    completeVisible.value = false
    await refreshCurrent()
  } finally {
    submitting.value = false
  }
}

async function handleReinspect(row, passed) {
  await ElMessageBox.confirm(
    passed ? '确认复检合格并关闭处置单？' : '确认复检不合格？（将退回处理中，可再次维修）',
    '提示',
    { type: 'warning' },
  )

  current.value = row
  await nonconformanceApi.reinspect(row.id, { passed })
  ElMessage.success(passed ? '已关闭' : '已退回处理中')
  await refreshCurrent()
}

async function handleScrap(row) {
  const { value } = await ElMessageBox.prompt('报废原因', '报废处置', { inputPlaceholder: '如 无法修复' })
  current.value = row
  await nonconformanceApi.scrap(row.id, value || '维修后仍不合格，报废')
  ElMessage.success('已报废关闭')
  await refreshCurrent()
}

async function openHistory(row) {
  current.value = await nonconformanceApi.getById(row.id)
  historyVisible.value = true
}

onMounted(loadData)
</script>

<style scoped>
.panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.filter-card :deep(.el-form-item) {
  margin-bottom: 0;
}

.desc {
  color: #909399;
  font-size: 12px;
}

.hint {
  color: #909399;
  font-size: 12px;
  margin: 0 0 0 100px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
