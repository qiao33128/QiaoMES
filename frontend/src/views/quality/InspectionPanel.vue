<template>
  <div class="panel">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="类型">
          <el-select v-model="query.type" placeholder="全部" clearable style="width: 150px">
            <el-option v-for="(label, key) in InspectionTypeMap" :key="key" :label="label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="query.status" placeholder="全部" clearable style="width: 130px">
            <el-option v-for="(item, key) in InspectionStatusMap" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="关键字">
          <el-input v-model="query.keyword" placeholder="单号 / SN / 料号" clearable style="width: 190px" @keyup.enter="handleSearch" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
          <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-button type="primary" :icon="Plus" @click="openCreate">新建检验单</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="list" v-loading="loading" stripe @row-click="openDetail">
        <el-table-column prop="inspectionNumber" label="检验单号" width="190" />
        <el-table-column label="类型" width="110">
          <template #default="{ row }">{{ InspectionTypeMap[row.type] }}</template>
        </el-table-column>
        <el-table-column label="受检对象" min-width="170">
          <template #default="{ row }">{{ row.sn || row.materialCode || row.productCode || '-' }}</template>
        </el-table-column>
        <el-table-column prop="sampleSize" label="抽样" width="80" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="InspectionStatusMap[row.status]?.type" size="small">
              {{ InspectionStatusMap[row.status]?.label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="不合格项" width="90">
          <template #default="{ row }">
            <span :class="{ 'text-danger': row.failedItemCount > 0 }">{{ row.failedItemCount }}</span>
          </template>
        </el-table-column>
        <el-table-column label="检验员" width="110">
          <template #default="{ row }">{{ row.inspectorName || '-' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" plain @click.stop="openDetail(row)">
              {{ row.status <= 1 ? '录入/判定' : '查看' }}
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

    <!-- 新建检验单 -->
    <el-dialog v-model="createVisible" title="新建检验单" width="820px">
      <el-form :model="createForm" label-width="90px">
        <el-row :gutter="12">
          <el-col :span="8">
            <el-form-item label="检验类型" required>
              <el-select v-model="createForm.type" style="width: 100%">
                <el-option v-for="(label, key) in InspectionTypeMap" :key="key" :label="label" :value="Number(key)" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="抽样数" required>
              <el-input-number v-model="createForm.sampleSize" :min="1" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="抽样标准">
              <el-input v-model="createForm.aqlLevel" placeholder="如 AQL 1.0" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="SN">
              <el-input v-model="createForm.sn" placeholder="成品 / 过程检验填 SN" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="物料编码">
              <el-input v-model="createForm.materialCode" placeholder="IQC 填物料编码" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="允收数 Ac">
              <el-input-number v-model="createForm.acceptedLimit" :min="0" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="拒收数 Re">
              <el-input-number v-model="createForm.rejectedLimit" :min="1" style="width: 100%" />
            </el-form-item>
          </el-col>
        </el-row>

        <div class="items-header">
          <span>检验项（{{ createForm.items.length }}）</span>
          <el-button size="small" type="primary" :icon="Plus" @click="addCreateItem">添加检验项</el-button>
        </div>

        <el-table :data="createForm.items" size="small" border max-height="260">
          <el-table-column label="检验项" min-width="140">
            <template #default="{ row }"><el-input v-model="row.name" placeholder="如 外观 / 长度" /></template>
          </el-table-column>
          <el-table-column label="标准/要求" min-width="140">
            <template #default="{ row }"><el-input v-model="row.standard" /></template>
          </el-table-column>
          <el-table-column label="规格下限" width="110">
            <template #default="{ row }"><el-input v-model.number="row.lowerLimit" type="number" /></template>
          </el-table-column>
          <el-table-column label="规格上限" width="110">
            <template #default="{ row }"><el-input v-model.number="row.upperLimit" type="number" /></template>
          </el-table-column>
          <el-table-column label="关键项" width="80" align="center">
            <template #default="{ row }"><el-switch v-model="row.isKeyItem" /></template>
          </el-table-column>
          <el-table-column label="操作" width="70" align="center">
            <template #default="{ $index }">
              <el-button link type="danger" @click="createForm.items.splice($index, 1)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-form>

      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleCreate">创建</el-button>
      </template>
    </el-dialog>

    <!-- 录入与判定 -->
    <el-dialog v-model="detailVisible" :title="`检验执行 · ${detail?.inspectionNumber || ''}`" width="900px">
      <el-descriptions v-if="detail" :column="3" size="small" border>
        <el-descriptions-item label="类型">{{ InspectionTypeMap[detail.type] }}</el-descriptions-item>
        <el-descriptions-item label="抽样数">{{ detail.sampleSize }}</el-descriptions-item>
        <el-descriptions-item label="状态">{{ InspectionStatusMap[detail.status]?.label }}</el-descriptions-item>
        <el-descriptions-item label="受检对象">{{ detail.sn || detail.materialCode || detail.productCode || '-' }}</el-descriptions-item>
        <el-descriptions-item label="抽样标准">{{ detail.aqlLevel || '-' }}</el-descriptions-item>
        <el-descriptions-item label="Ac / Re">{{ detail.acceptedLimit }} / {{ detail.rejectedLimit }}</el-descriptions-item>
      </el-descriptions>

      <el-table :data="detail?.items || []" size="small" border class="detail-table">
        <el-table-column prop="sequence" label="序" width="60" />
        <el-table-column prop="name" label="检验项" min-width="130" />
        <el-table-column label="规格" width="130">
          <template #default="{ row }">
            <span v-if="row.lowerLimit !== null || row.upperLimit !== null">
              {{ row.lowerLimit ?? '' }} ~ {{ row.upperLimit ?? '' }}
            </span>
            <span v-else>{{ row.standard || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="实测值" width="140">
          <template #default="{ row }">
            <el-input v-model="row.measuredValue" :disabled="isJudged" size="small" placeholder="录入数值/描述" />
          </template>
        </el-table-column>
        <el-table-column label="不良代码" width="130">
          <template #default="{ row }">
            <el-input v-model="row.defectCode" :disabled="isJudged" size="small" placeholder="不合格时填" />
          </template>
        </el-table-column>
        <el-table-column label="判定" width="90" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.isQualified === true" type="success" size="small">合格</el-tag>
            <el-tag v-else-if="row.isQualified === false" type="danger" size="small">不合格</el-tag>
            <el-tag v-else type="info" size="small">未判</el-tag>
          </template>
        </el-table-column>
      </el-table>

      <el-form v-if="!isJudged" :inline="true" class="submit-bar">
        <el-form-item label="不良数">
          <el-input-number v-model="submitForm.defectQuantity" :min="0" :max="detail?.sampleSize || 0" />
        </el-form-item>
        <el-form-item label="让步接收">
          <el-switch v-model="submitForm.concession" />
        </el-form-item>
        <el-form-item label="自动生成处置单">
          <el-switch v-model="submitForm.createNonconformance" />
        </el-form-item>
        <el-form-item label="不良描述">
          <el-input v-model="submitForm.defectDescription" placeholder="如 长度超上限" style="width: 200px" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="detailVisible = false">关闭</el-button>
        <el-button v-if="!isJudged" :loading="submitting" @click="handleSaveResults">保存录入</el-button>
        <el-button v-if="!isJudged" type="primary" :loading="submitting" @click="handleSubmit">提交判定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { inspectionApi, InspectionStatusMap, InspectionTypeMap } from '@/api/quality'

const list = ref([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)

const query = reactive({ page: 1, pageSize: 20, type: null, status: null, keyword: '' })
const createVisible = ref(false)
const detailVisible = ref(false)
const detail = ref(null)

const createForm = reactive({
  type: 0,
  sampleSize: 32,
  aqlLevel: 'AQL 1.0',
  sn: '',
  materialCode: '',
  acceptedLimit: 0,
  rejectedLimit: 1,
  items: [],
})

const submitForm = reactive({
  defectQuantity: 0,
  concession: false,
  createNonconformance: true,
  defectDescription: '',
})

const isJudged = computed(() => (detail.value?.status ?? 0) >= 2)

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

async function loadData() {
  loading.value = true
  try {
    const data = await inspectionApi.list({
      page: query.page,
      pageSize: query.pageSize,
      type: query.type ?? undefined,
      status: query.status ?? undefined,
      keyword: query.keyword || undefined,
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
  query.type = null
  query.status = null
  query.keyword = ''
  handleSearch()
}

function addCreateItem() {
  createForm.items.push({ name: '', standard: '', lowerLimit: null, upperLimit: null, isKeyItem: false })
}

function openCreate() {
  Object.assign(createForm, {
    type: 0,
    sampleSize: 32,
    aqlLevel: 'AQL 1.0',
    sn: '',
    materialCode: '',
    acceptedLimit: 0,
    rejectedLimit: 1,
    items: [],
  })
  addCreateItem()
  createVisible.value = true
}

async function handleCreate() {
  if (!createForm.sn && !createForm.materialCode) {
    ElMessage.warning('请填写 SN 或物料编码作为受检对象')
    return
  }
  if (createForm.items.some((item) => !item.name?.trim())) {
    ElMessage.warning('检验项名称不能为空')
    return
  }

  submitting.value = true
  try {
    await inspectionApi.create({
      type: createForm.type,
      sampleSize: createForm.sampleSize,
      aqlLevel: createForm.aqlLevel || null,
      sn: createForm.sn || null,
      materialCode: createForm.materialCode || null,
      acceptedLimit: createForm.acceptedLimit,
      rejectedLimit: createForm.rejectedLimit,
      items: createForm.items.map((item) => ({
        name: item.name,
        standard: item.standard || null,
        lowerLimit: item.lowerLimit === '' ? null : item.lowerLimit,
        upperLimit: item.upperLimit === '' ? null : item.upperLimit,
        isKeyItem: !!item.isKeyItem,
      })),
    })
    ElMessage.success('检验单已创建')
    createVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

async function openDetail(row) {
  detail.value = await inspectionApi.getById(row.id)
  Object.assign(submitForm, {
    defectQuantity: detail.value.defectQuantity || 0,
    concession: false,
    createNonconformance: true,
    defectDescription: '',
  })
  detailVisible.value = true
}

async function persistItems() {
  if (!detail.value) return

  for (const item of detail.value.items) {
    if (item.measuredValue === null || item.measuredValue === undefined || item.measuredValue === '') {
      continue
    }

    await inspectionApi.recordItem(detail.value.id, {
      itemId: item.id,
      measuredValue: String(item.measuredValue),
      defectCode: item.defectCode || null,
    })
  }
}

async function handleSaveResults() {
  submitting.value = true
  try {
    await persistItems()
    detail.value = await inspectionApi.getById(detail.value.id)
    ElMessage.success('录入已保存（定量项已按规格自动判定）')
  } finally {
    submitting.value = false
  }
}

async function handleSubmit() {
  submitting.value = true
  try {
    await persistItems()
    const result = await inspectionApi.submit(detail.value.id, {
      defectQuantity: submitForm.defectQuantity,
      concession: submitForm.concession,
      createNonconformance: submitForm.createNonconformance,
      defectDescription: submitForm.defectDescription || null,
    })
    detail.value = result
    ElMessage.success(`判定完成：${InspectionStatusMap[result.status]?.label}`)
    await loadData()
  } finally {
    submitting.value = false
  }
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

.items-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 8px 0;
  font-weight: 600;
}

.detail-table {
  margin-top: 12px;
}

.submit-bar {
  margin-top: 16px;
}

.text-danger {
  color: #f56c6c;
  font-weight: 600;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
