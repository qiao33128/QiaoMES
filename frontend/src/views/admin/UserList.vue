<template>
  <div class="user-page">
    <el-card shadow="never" class="filter-card">
      <el-form :inline="true" :model="query">
        <el-form-item label="关键字">
          <el-input
            v-model="query.keyword"
            placeholder="用户名 / 姓名"
            clearable
            style="width: 200px"
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
          <el-button type="primary" :icon="Plus" @click="openCreate">新建用户</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <el-table :data="list" v-loading="loading" stripe>
        <el-table-column prop="username" label="用户名" width="160" />
        <el-table-column prop="displayName" label="姓名" width="140" />
        <el-table-column prop="email" label="邮箱" min-width="180" />
        <el-table-column label="角色" min-width="200">
          <template #default="{ row }">
            <el-tag v-for="name in row.roles" :key="name" size="small" class="role-tag">{{ name }}</el-tag>
            <span v-if="!row.roles.length" class="empty-text">未分配角色</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'danger'" size="small">
              {{ row.isActive ? '启用' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最后登录" width="170">
          <template #default="{ row }">{{ formatTime(row.lastLoginAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" plain @click="openRoles(row)">分配角色</el-button>
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
        @current-change="loadUsers"
        @size-change="handleSearch"
      />
    </el-card>

    <el-dialog v-model="createDialog.visible" title="新建用户" width="480px">
      <el-form :model="createDialog.form" label-width="80px">
        <el-form-item label="用户名">
          <el-input v-model="createDialog.form.username" placeholder="登录账号" />
        </el-form-item>
        <el-form-item label="姓名">
          <el-input v-model="createDialog.form.displayName" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="createDialog.form.password" type="password" show-password placeholder="至少 6 位" />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="createDialog.form.email" />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="createDialog.form.roleIds" multiple style="width: 100%" placeholder="可稍后再分配">
            <el-option v-for="role in roles" :key="role.id" :label="role.name" :value="role.id" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitCreate">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="roleDialog.visible" :title="`分配角色 · ${roleDialog.username}`" width="420px">
      <el-select v-model="roleDialog.roleIds" multiple style="width: 100%">
        <el-option v-for="role in roles" :key="role.id" :label="role.name" :value="role.id" />
      </el-select>
      <template #footer>
        <el-button @click="roleDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitRoles">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { roleApi, userApi } from '@/api/admin'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

const list = ref([])
const roles = ref([])
const total = ref(0)
const loading = ref(false)
const saving = ref(false)

const query = reactive({ page: 1, pageSize: 20, keyword: '', isActive: null })
const createDialog = reactive({
  visible: false,
  form: { username: '', displayName: '', password: '', email: '', roleIds: [] },
})
const roleDialog = reactive({ visible: false, userId: null, username: '', roleIds: [] })

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

async function loadUsers() {
  loading.value = true
  try {
    const data = await userApi.list({
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

async function loadRoles() {
  try {
    roles.value = await roleApi.list()
  } catch {
    // 没有 roles:read 权限时静默降级：角色下拉为空
    roles.value = []
  }
}

function handleSearch() {
  query.page = 1
  loadUsers()
}

function resetQuery() {
  query.keyword = ''
  query.isActive = null
  handleSearch()
}

function openCreate() {
  createDialog.form = { username: '', displayName: '', password: '', email: '', roleIds: [] }
  createDialog.visible = true
}

async function submitCreate() {
  const form = createDialog.form
  if (!form.username.trim() || !form.displayName.trim()) {
    ElMessage.warning('用户名与姓名不能为空')
    return
  }
  if (form.password.length < 6) {
    ElMessage.warning('密码长度至少 6 位')
    return
  }

  saving.value = true
  try {
    await userApi.create({ ...form, email: form.email || null })
    ElMessage.success('用户已创建')
    createDialog.visible = false
    await loadUsers()
  } finally {
    saving.value = false
  }
}

function openRoles(row) {
  roleDialog.userId = row.id
  roleDialog.username = row.username
  roleDialog.roleIds = [...(row.roleIds || [])]
  roleDialog.visible = true
}

async function submitRoles() {
  saving.value = true
  try {
    await userApi.setRoles(roleDialog.userId, roleDialog.roleIds)
    ElMessage.success('角色已更新，对在线用户立即生效')
    roleDialog.visible = false
    await loadUsers()
    // 改的是自己：刷新本地权限，让菜单立即变化
    if (authStore.user?.id === roleDialog.userId) {
      await authStore.refreshProfile()
    }
  } finally {
    saving.value = false
  }
}

async function toggleStatus(row) {
  const action = row.isActive ? '停用' : '启用'
  await ElMessageBox.confirm(`确定${action}用户「${row.username}」吗？`, '提示', { type: 'warning' })
  await userApi.setStatus(row.id, !row.isActive)
  ElMessage.success(`已${action}`)
  await loadUsers()
}

onMounted(async () => {
  await Promise.all([loadUsers(), loadRoles()])
})
</script>

<style scoped>
.role-tag {
  margin: 2px 6px 2px 0;
}

.empty-text {
  color: #909399;
  font-size: 13px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
