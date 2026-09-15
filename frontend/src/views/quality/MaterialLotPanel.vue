<template>
  <div class="panel">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="关键字">
          <el-input v-model="query.keyword" placeholder="批次号 / 物料 / 供应商" clearable style="width: 200px" @keyup.enter="handleSearch" />
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="query.status" placeholder="全部" clearable style="width: 130px">
            <el-option v-for="(item, key) in MaterialLotStatusMap" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
          <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-button :icon="Link" @click="openBind">绑定 SN 用料</el-button>
          <el-button type="primary" :icon="Plus" @click="openCreate">批次入库</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="list" v-loading="loading" stripe>
        <el-table-column prop="lotNumber" label="批次号" width="170" />
        <el-table-column label="物料" min-width="180">
          <template #default="{ row }">
            <div>{{ row.materialCode }}</div>
            <div class="desc">{{ row.materialName || '' }}</div>
          </template>
        </el-table-column>
        <el-table-column prop="supplier" label="供应商" width="140">
          <template #default="{ row }">{{ row.supplier || '-' }}</template>
        </el-table-column>
        <el-table-column label="来料日期" width="120">
          <template #default="{ row }">{{ formatDate(row.receivedAt) }}</template>
        </el-table-column>
        <el-table-column label="数量 / 余量" width="150">
          <template #default="{ row }">
            <span>{{ row.quantity }}</span>
            <span class="desc"> / </span>
            <span :class="{ 'text-warn': row.remainingQuantity > 0 }">{{ row.remainingQuantity }}</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-tag :type="MaterialLotStatusMap[row.status]?.type" size="small">
              {{ MaterialLotStatusMap[row.status]?.label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="已流向" width="120">
          <template #default="{ row }">{{ row.consumedSnCount }} 颗 / {{ row.consumedQuantity }}</template>
        </el-table-column>
        <el-table-column label="操作" width="290" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" plain @click="openTrace(row)">流向追溯</el-button>
            <el-button v-if="row.status === 0" size="small" type="success" plain @click="openInspect(row)">IQC 判定</el-button>
            <el-button
              v-if="row.status !== 4 && row.status !== 2"
              size="small"
              :type="row.status === 3 ? 'success' : 'warning'"
              plain
              @click="toggleFrozen(row)"
            >
              {{ row.status === 3 ? '解冻' : '冻结' }}
            </el-button>
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

    <!-- 批次入库 -->
    <el-dialog v-model="createVisible" title="来料批次入库" width="560px">
      <el-form :model="createForm" label-width="100px">
        <el-form-item label="批次号" required>
          <el-input v-model="createForm.lotNumber" placeholder="如 LOT-20260915-001" />
        </el-form-item>
        <el-form-item label="物料编码" required>
          <el-input v-model="createForm.materialCode" />
        </el-form-item>
        <el-form-item label="物料名称">
          <el-input v-model="createForm.materialName" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="来料数量" required>
              <el-input-number v-model="createForm.quantity" :min="0.0001" :precision="4" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="单位">
              <el-input v-model="createForm.unit" placeholder="PCS / KG" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="供应商">
              <el-input v-model="createForm.supplier" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="供应商批号">
              <el-input v-model="createForm.supplierLotNumber" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="备注">
          <el-input v-model="createForm.remark" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleCreate">入库</el-button>
      </template>
    </el-dialog>

    <!-- IQC 判定 -->
    <el-dialog v-model="inspectVisible" :title="`IQC 判定 · ${current?.lotNumber || ''}`" width="480px">
      <el-form label-width="100px">
        <el-form-item label="判定结果" required>
          <el-radio-group v-model="inspectForm.passed">
            <el-radio :value="true">合格（可投产）</el-radio>
            <el-radio :value="false">不合格（禁止投产）</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="!inspectForm.passed" label="不合格原因">
          <el-input v-model="inspectForm.reason" placeholder="如 尺寸超差 / 外观不良" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="inspectVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleInspect">提交</el-button>
      </template>
    </el-dialog>

    <!-- 绑定 SN 用料 -->
    <el-dialog v-model="bindVisible" title="绑定 SN 用料（建立来料 → 成品谱系）" width="560px">
      <el-form :model="bindForm" label-width="100px">
        <el-form-item label="SN" required>
          <el-input v-model="bindForm.sn" placeholder="扫描 / 输入成品序列号" />
        </el-form-item>
        <el-form-item label="物料编码" required>
          <el-input v-model="bindForm.materialCode" />
        </el-form-item>
        <el-form-item label="批次号" required>
          <el-select v-model="bindForm.lotNumber" filterable allow-create placeholder="选择或输入批次号" style="width: 100%">
            <el-option
              v-for="lot in bindableLots"
              :key="lot.id"
              :label="`${lot.lotNumber}（余 ${lot.remainingQuantity}）`"
              :value="lot.lotNumber"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="消耗数量" required>
          <el-input-number v-model="bindForm.quantity" :min="0.0001" :precision="4" style="width: 100%" />
        </el-form-item>
        <el-form-item label="工序">
          <el-input v-model="bindForm.operationName" placeholder="如 贴片 / 组装" />
        </el-form-item>
      </el-form>
      <p class="hint">绑定即扣减批次余量；同一 SN + 批次 + 物料重复提交不会重复扣减。</p>
      <template #footer>
        <el-button @click="bindVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleBind">绑定</el-button>
      </template>
    </el-dialog>

    <!-- 批次流向追溯 -->
    <el-drawer v-model="traceVisible" :title="`批次流向 · ${trace?.lot.lotNumber || ''}`" size="720px">
      <div v-if="trace">
        <el-descriptions :column="2" border size="small">
          <el-descriptions-item label="物料">{{ trace.lot.materialCode }} {{ trace.lot.materialName || '' }}</el-descriptions-item>
          <el-descriptions-item label="供应商">{{ trace.lot.supplier || '-' }}</el-descriptions-item>
          <el-descriptions-item label="来料数量">{{ trace.lot.quantity }}</el-descriptions-item>
          <el-descriptions-item label="剩余">{{ trace.lot.remainingQuantity }}</el-descriptions-item>
          <el-descriptions-item label="状态">{{ MaterialLotStatusMap[trace.lot.status]?.label }}</el-descriptions-item>
          <el-descriptions-item label="IQC 单号">{{ trace.lot.iqcInspectionNumber || '-' }}</el-descriptions-item>
          <el-descriptions-item label="流向 SN 数">{{ trace.snCount }}</el-descriptions-item>
          <el-descriptions-item label="累计消耗">{{ trace.consumedQuantity }}</el-descriptions-item>
        </el-descriptions>

        <el-table :data="trace.consumptions" size="small" border class="mt-12" max-height="480">
          <el-table-column prop="sn" label="SN" min-width="200" />
          <el-table-column prop="materialCode" label="物料" width="130" />
          <el-table-column prop="quantity" label="用量" width="90" />
          <el-table-column prop="operationName" label="工序" width="110">
            <template #default="{ row }">{{ row.operationName || '-' }}</template>
          </el-table-column>
          <el-table-column label="绑定时间" width="170">
            <template #default="{ row }">{{ formatTime(row.boundAt) }}</template>
          </el-table-column>
        </el-table>
      </div>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Link, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { materialLotApi, MaterialLotStatusMap } from '@/api/quality'

const list = ref([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)
const current = ref(null)

const query = reactive({ page: 1, pageSize: 20, keyword: '', status: null })
const createVisible = ref(false)
const inspectVisible = ref(false)
const bindVisible = ref(false)
const traceVisible = ref(false)
const trace = ref(null)

const createForm = reactive({
  lotNumber: '',
  materialCode: '',
  materialName: '',
  quantity: 100,
  unit: 'PCS',
  supplier: '',
  supplierLotNumber: '',
  remark: '',
})

const inspectForm = reactive({ passed: true, reason: '' })
const bindForm = reactive({ sn: '', materialCode: '', lotNumber: '', quantity: 1, operationName: '' })

// 仅「合格可用 / 待检」的批次可用于绑定
const bindableLots = computed(() => list.value.filter((lot) => lot.status === 0 || lot.status === 1))

function formatDate(value) {
  return value ? new Date(value).toLocaleDateString('zh-CN') : '-'
}

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

async function loadData() {
  loading.value = true
  try {
    const data = await materialLotApi.list({
      page: query.page,
      pageSize: query.pageSize,
      keyword: query.keyword || undefined,
      status: query.status ?? undefined,
    })
    list.value = data.items
    total.value = data.totalCount
  } finally {
    loading.value = false
  }
}

function handleSearch() {
  query.page = 1
  loadData()
}

function resetQuery() {
  query.keyword = ''
  query.status = null
  handleSearch()
}

function openCreate() {
  Object.assign(createForm, {
    lotNumber: '',
    materialCode: '',
    materialName: '',
    quantity: 100,
    unit: 'PCS',
    supplier: '',
    supplierLotNumber: '',
    remark: '',
  })
  createVisible.value = true
}

async function handleCreate() {
  if (!createForm.lotNumber.trim() || !createForm.materialCode.trim()) {
    ElMessage.warning('批次号与物料编码不能为空')
    return
  }

  submitting.value = true
  try {
    await materialLotApi.create({
      lotNumber: createForm.lotNumber,
      materialCode: createForm.materialCode,
      materialName: createForm.materialName || null,
      quantity: createForm.quantity,
      unit: createForm.unit || null,
      supplier: createForm.supplier || null,
      supplierLotNumber: createForm.supplierLotNumber || null,
      remark: createForm.remark || null,
    })
    ElMessage.success('批次已入库（状态：待检）')
    createVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

function openInspect(row) {
  current.value = row
  Object.assign(inspectForm, { passed: true, reason: '' })
  inspectVisible.value = true
}

async function handleInspect() {
  submitting.value = true
  try {
    await materialLotApi.inspect(current.value.id, {
      passed: inspectForm.passed,
      reason: inspectForm.reason || null,
    })
    ElMessage.success(inspectForm.passed ? '批次已放行' : '批次已判不合格')
    inspectVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

async function toggleFrozen(row) {
  const frozen = row.status !== 3
  await materialLotApi.setFrozen(row.id, { frozen, reason: frozen ? '质量异常冻结' : null })
  ElMessage.success(frozen ? '批次已冻结' : '批次已解冻')
  await loadData()
}

function openBind() {
  Object.assign(bindForm, { sn: '', materialCode: '', lotNumber: '', quantity: 1, operationName: '' })
  bindVisible.value = true
}

async function handleBind() {
  if (!bindForm.sn.trim() || !bindForm.materialCode.trim() || !bindForm.lotNumber) {
    ElMessage.warning('SN / 物料编码 / 批次号均不能为空')
    return
  }

  submitting.value = true
  try {
    await materialLotApi.bind([
      {
        sn: bindForm.sn,
        materialCode: bindForm.materialCode,
        lotNumber: bindForm.lotNumber,
        quantity: bindForm.quantity,
        operationName: bindForm.operationName || null,
      },
    ])
    ElMessage.success('绑定成功，已建立来料 → 成品谱系')
    bindVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

async function openTrace(row) {
  trace.value = await materialLotApi.traceByLot(row.lotNumber)
  traceVisible.value = true
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

.text-warn {
  color: #e6a23c;
  font-weight: 600;
}

.hint {
  color: #909399;
  font-size: 12px;
  margin: 0 0 0 100px;
}

.mt-12 {
  margin-top: 12px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
