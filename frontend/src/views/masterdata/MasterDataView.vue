<template>
  <div class="masterdata-page">
    <el-tabs v-model="activeKey" @tab-change="handleTabChange">
      <el-tab-pane
        v-for="(item, key) in catalogConfigs"
        :key="key"
        :label="item.title"
        :name="key"
      />
    </el-tabs>

    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            :placeholder="`${config.title}编码 / 名称`"
            clearable
            style="width: 220px"
            @keyup.enter="handleSearch"
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="query.isActive" placeholder="全部" clearable style="width: 120px">
            <el-option label="启用" :value="true" />
            <el-option label="停用" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
          <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
        </el-form-item>
        <el-form-item style="float: right">
          <el-button type="primary" :icon="Plus" @click="openForm()">新建{{ config.title }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="list" v-loading="loading" stripe>
        <el-table-column
          v-for="col in config.columns"
          :key="col.prop"
          :prop="col.prop"
          :label="col.label"
          :width="col.width"
          :min-width="col.minWidth"
        >
          <template #default="{ row }">
            <el-tag v-if="col.type === 'bool'" :type="boolTagType(col, row)" size="small">
              {{ boolText(col, row) }}
            </el-tag>
            <span v-else-if="col.type === 'enum'">{{ col.options[row[col.prop]] ?? '-' }}</span>
            <span v-else>{{ formatCell(row[col.prop]) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button size="small" plain @click="openForm(row)">编辑</el-button>
            <el-button
              size="small"
              :type="row.isActive ? 'danger' : 'success'"
              plain
              @click="toggleStatus(row)"
            >
              {{ row.isActive ? '停用' : '启用' }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        class="pagination"
        layout="total, sizes, prev, pager, next"
        :total="total"
        :page-sizes="[10, 20, 50]"
        v-model:current-page="query.page"
        v-model:page-size="query.pageSize"
        @current-change="loadData"
        @size-change="handleSearch"
      />
    </el-card>

    <el-dialog v-model="form.visible" :title="dialogTitle" width="520px">
      <el-form :model="form.data" label-width="110px">
        <el-form-item
          v-for="field in visibleFormFields"
          :key="field.prop"
          :label="field.label"
          :required="field.required"
        >
          <el-input
            v-if="field.type === 'number'"
            v-model.number="form.data[field.prop]"
            type="number"
          />
          <el-select
            v-else-if="field.type === 'enum'"
            v-model="form.data[field.prop]"
            style="width: 100%"
          >
            <el-option
              v-for="(label, value) in field.options"
              :key="value"
              :label="label"
              :value="Number(value)"
            />
          </el-select>
          <el-select
            v-else-if="field.type === 'workCenter'"
            v-model="form.data[field.prop]"
            clearable
            filterable
            placeholder="可留空"
            style="width: 100%"
          >
            <el-option
              v-for="wc in workCenters"
              :key="wc.id"
              :label="`${wc.code} ${wc.name}`"
              :value="wc.id"
            />
          </el-select>
          <el-switch v-else-if="field.type === 'switch'" v-model="form.data[field.prop]" />
          <el-input v-else v-model="form.data[field.prop]" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="form.visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitForm">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { workCenterApi } from '@/api/masterdata'
import { catalogConfigs } from './catalogConfig'

const activeKey = ref('products')
const list = ref([])
const workCenters = ref([])
const total = ref(0)
const loading = ref(false)
const saving = ref(false)

const query = reactive({ page: 1, pageSize: 20, keyword: '', isActive: null })
const form = reactive({ visible: false, id: null, data: {} })

const config = computed(() => catalogConfigs[activeKey.value])
const dialogTitle = computed(() => `${form.id ? '编辑' : '新建'}${config.value.title}`)
// 编码是业务唯一键，编辑时不允许修改
const visibleFormFields = computed(() =>
  config.value.formFields.filter((field) => !(field.createOnly && form.id)),
)

function formatCell(value) {
  return value === null || value === undefined || value === '' ? '-' : value
}

function boolText(col, row) {
  return row[col.prop] ? col.trueText || '启用' : col.falseText || '停用'
}

function boolTagType(col, row) {
  if (col.trueText) return row[col.prop] ? 'warning' : 'info'
  return row[col.prop] ? 'success' : 'danger'
}

async function loadData() {
  loading.value = true
  try {
    const data = await config.value.api.list({
      page: query.page,
      pageSize: query.pageSize,
      keyword: query.keyword || undefined,
      isActive: query.isActive ?? undefined,
    })
    list.value = data.items
    total.value = data.totalCount
  } finally {
    loading.value = false
  }
}

async function loadWorkCenters() {
  if (workCenters.value.length) return
  try {
    const data = await workCenterApi.list({ page: 1, pageSize: 100, isActive: true })
    workCenters.value = data.items
  } catch {
    workCenters.value = []
  }
}

function handleSearch() {
  query.page = 1
  loadData()
}

function resetQuery() {
  query.keyword = ''
  query.isActive = null
  query.page = 1
  loadData()
}

function handleTabChange() {
  resetQuery()
  if (activeKey.value === 'operations') {
    loadWorkCenters()
  }
}

function openForm(row) {
  form.id = row?.id || null
  form.data = row ? { ...row } : config.value.createDefaults()
  form.visible = true
}

async function submitForm() {
  const requiredFields = config.value.formFields.filter(
    (field) => field.required && !(field.createOnly && form.id),
  )
  const missing = requiredFields.find((field) => !String(form.data[field.prop] ?? '').trim())
  if (missing) {
    ElMessage.warning(`${missing.label}不能为空`)
    return
  }

  saving.value = true
  try {
    if (form.id) {
      await config.value.api.update(form.id, form.data)
    } else {
      await config.value.api.create(form.data)
    }
    ElMessage.success('保存成功')
    form.visible = false
    await loadData()
  } finally {
    saving.value = false
  }
}

async function toggleStatus(row) {
  const action = row.isActive ? '停用' : '启用'
  await ElMessageBox.confirm(`确定${action}「${row.name}」吗？`, '提示', { type: 'warning' })
  await config.value.api.setStatus(row.id, !row.isActive)
  ElMessage.success(`已${action}`)
  await loadData()
}

onMounted(async () => {
  await Promise.all([loadData(), loadWorkCenters()])
})
</script>

<style scoped>
.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
