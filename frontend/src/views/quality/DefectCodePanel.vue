<template>
  <div class="panel">
    <el-row :gutter="16">
      <el-col :span="15">
        <el-card shadow="never" class="filter-card">
          <el-form :inline="true" :model="query">
            <el-form-item label="关键字">
              <el-input v-model="query.keyword" placeholder="代码 / 名称" clearable style="width: 180px" @keyup.enter="handleSearch" />
            </el-form-item>
            <el-form-item label="分类">
              <el-input v-model="query.category" placeholder="如 外观" clearable style="width: 120px" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :icon="Search" @click="handleSearch">查询</el-button>
              <el-button :icon="Refresh" @click="resetQuery">重置</el-button>
            </el-form-item>
            <el-form-item style="float: right">
              <el-button type="primary" :icon="Plus" @click="openForm()">新建不良代码</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="list" v-loading="loading" stripe>
            <el-table-column prop="code" label="代码" width="130" />
            <el-table-column prop="name" label="名称" min-width="140" />
            <el-table-column prop="category" label="分类" width="100" />
            <el-table-column prop="description" label="说明" min-width="140" />
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'" size="small">
                  {{ row.isActive ? '启用' : '停用' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="150" fixed="right">
              <template #default="{ row }">
                <el-button size="small" plain @click="openForm(row)">编辑</el-button>
                <el-button size="small" :type="row.isActive ? 'danger' : 'success'" plain @click="toggleActive(row)">
                  {{ row.isActive ? '停用' : '启用' }}
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
      </el-col>

      <el-col :span="9">
        <el-card shadow="never">
          <template #header>
            <div class="pareto-header">
              <span>不良 Pareto（最近 30 天）</span>
              <el-button size="small" :icon="Refresh" @click="loadPareto">刷新</el-button>
            </div>
          </template>

          <el-empty v-if="!pareto.length" description="暂无不良数据" :image-size="70" />

          <div v-else>
            <div v-for="item in pareto" :key="item.defectCode" class="pareto-row">
              <span class="pareto-code">{{ item.defectCode }}</span>
              <el-progress
                :percentage="percentOf(item.count)"
                :format="() => String(item.count)"
                :stroke-width="14"
              />
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="formVisible" :title="form.id ? '编辑不良代码' : '新建不良代码'" width="480px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="代码" required>
          <el-input v-model="form.code" :disabled="!!form.id" placeholder="如 D-SIZE" />
        </el-form-item>
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="如 尺寸超差" />
        </el-form-item>
        <el-form-item label="分类">
          <el-input v-model="form.category" placeholder="外观 / 尺寸 / 功能…" />
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="form.description" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="formVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { defectCodeApi } from '@/api/quality'

const list = ref([])
const pareto = ref([])
const total = ref(0)
const loading = ref(false)
const submitting = ref(false)

const query = reactive({ page: 1, pageSize: 20, keyword: '', category: '' })
const formVisible = ref(false)
const form = reactive({ id: null, code: '', name: '', category: '', description: '' })

async function loadData() {
  loading.value = true
  try {
    const data = await defectCodeApi.list({
      page: query.page,
      pageSize: query.pageSize,
      keyword: query.keyword || undefined,
      category: query.category || undefined,
    })
    list.value = data.items
    total.value = data.totalCount
  } finally {
    loading.value = false
  }
}

async function loadPareto() {
  pareto.value = await defectCodeApi.pareto({ top: 10 })
}

function percentOf(count) {
  const max = Math.max(...pareto.value.map((item) => item.count), 1)
  return Math.round((count / max) * 100)
}

function handleSearch() {
  query.page = 1
  loadData()
}

function resetQuery() {
  query.keyword = ''
  query.category = ''
  handleSearch()
}

function openForm(row) {
  Object.assign(form, {
    id: row?.id || null,
    code: row?.code || '',
    name: row?.name || '',
    category: row?.category || '',
    description: row?.description || '',
  })
  formVisible.value = true
}

async function handleSave() {
  if (!form.code.trim() || !form.name.trim()) {
    ElMessage.warning('代码与名称不能为空')
    return
  }

  submitting.value = true
  try {
    if (form.id) {
      await defectCodeApi.update(form.id, {
        name: form.name,
        category: form.category || null,
        description: form.description || null,
      })
    } else {
      await defectCodeApi.create({
        code: form.code,
        name: form.name,
        category: form.category || null,
        description: form.description || null,
      })
    }
    ElMessage.success('保存成功')
    formVisible.value = false
    await loadData()
  } finally {
    submitting.value = false
  }
}

async function toggleActive(row) {
  await defectCodeApi.setStatus(row.id, !row.isActive)
  ElMessage.success(row.isActive ? '已停用' : '已启用')
  await loadData()
}

onMounted(async () => {
  await Promise.all([loadData(), loadPareto()])
})
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

.pareto-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.pareto-row {
  margin-bottom: 12px;
}

.pareto-code {
  font-size: 12px;
  color: #606266;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
