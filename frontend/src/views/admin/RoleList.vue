<template>
  <div class="role-page">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>角色与权限</span>
          <el-button type="primary" :icon="Plus" @click="openForm()">新建角色</el-button>
        </div>
      </template>

      <el-table :data="roles" v-loading="loading" stripe>
        <el-table-column prop="name" label="角色" width="150" />
        <el-table-column prop="description" label="描述" width="180" />
        <el-table-column label="类型" width="90">
          <template #default="{ row }">
            <el-tag :type="row.isBuiltIn ? 'info' : 'success'" size="small">
              {{ row.isBuiltIn ? '内置' : '自定义' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="已授权限" min-width="280">
          <template #default="{ row }">
            <el-tag v-for="code in row.permissions" :key="code" size="small" class="perm-tag">
              {{ permissionName(code) }}
            </el-tag>
            <span v-if="!row.permissions.length" class="empty-text">未授权任何权限</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" plain @click="openPermissions(row)">配置权限</el-button>
            <el-button size="small" plain @click="openForm(row)">编辑</el-button>
            <el-button
              size="small"
              type="danger"
              plain
              :disabled="row.isBuiltIn"
              @click="handleDelete(row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 / 编辑角色 -->
    <el-dialog v-model="form.visible" :title="form.id ? '编辑角色' : '新建角色'" width="460px">
      <el-form :model="form" label-width="72px">
        <el-form-item label="角色名">
          <el-input v-model="form.name" :disabled="form.isBuiltIn" placeholder="如 line-leader" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" placeholder="该角色的职责说明" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="form.visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitForm">保存</el-button>
      </template>
    </el-dialog>

    <!-- 配置权限 -->
    <el-dialog v-model="perm.visible" :title="`配置权限 · ${perm.roleName}`" width="620px">
      <div v-loading="loadingCatalog" class="perm-body">
        <div v-for="group in catalog" :key="group.group" class="perm-group">
          <div class="perm-group-title">{{ group.group }}</div>
          <el-checkbox-group v-model="perm.selected">
            <el-checkbox v-for="item in group.items" :key="item.code" :value="item.code">
              {{ item.name }}
            </el-checkbox>
          </el-checkbox-group>
        </div>
      </div>
      <template #footer>
        <el-button @click="perm.visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitPermissions">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { roleApi } from '@/api/admin'

const roles = ref([])
const catalog = ref([])
const loading = ref(false)
const loadingCatalog = ref(false)
const saving = ref(false)

const form = reactive({ visible: false, id: null, name: '', description: '', isBuiltIn: false })
const perm = reactive({ visible: false, roleId: null, roleName: '', selected: [] })

const permissionNameMap = computed(() => {
  const map = {}
  catalog.value.forEach((group) => {
    group.items.forEach((item) => {
      map[item.code] = item.name
    })
  })
  return map
})

function permissionName(code) {
  return permissionNameMap.value[code] || code
}

async function loadRoles() {
  loading.value = true
  try {
    roles.value = await roleApi.list()
  } finally {
    loading.value = false
  }
}

async function loadCatalog() {
  if (catalog.value.length) return
  loadingCatalog.value = true
  try {
    catalog.value = await roleApi.permissionCatalog()
  } finally {
    loadingCatalog.value = false
  }
}

function openForm(role) {
  form.id = role?.id || null
  form.name = role?.name || ''
  form.description = role?.description || ''
  form.isBuiltIn = role?.isBuiltIn || false
  form.visible = true
}

async function submitForm() {
  if (!form.name.trim()) {
    ElMessage.warning('角色名不能为空')
    return
  }
  saving.value = true
  try {
    if (form.id) {
      await roleApi.update(form.id, { name: form.name, description: form.description })
    } else {
      await roleApi.create({ name: form.name, description: form.description, permissions: [] })
    }
    ElMessage.success('保存成功')
    form.visible = false
    await loadRoles()
  } finally {
    saving.value = false
  }
}

async function openPermissions(role) {
  await loadCatalog()
  perm.roleId = role.id
  perm.roleName = role.name
  perm.selected = [...role.permissions]
  perm.visible = true
}

async function submitPermissions() {
  saving.value = true
  try {
    await roleApi.setPermissions(perm.roleId, perm.selected)
    ElMessage.success('权限已更新，对在线用户立即生效')
    perm.visible = false
    await loadRoles()
  } finally {
    saving.value = false
  }
}

async function handleDelete(role) {
  await ElMessageBox.confirm(`确定删除角色「${role.name}」吗？`, '提示', { type: 'warning' })
  await roleApi.remove(role.id)
  ElMessage.success('已删除')
  await loadRoles()
}

onMounted(async () => {
  await Promise.all([loadRoles(), loadCatalog()])
})
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.perm-tag {
  margin: 2px 6px 2px 0;
}

.empty-text {
  color: #909399;
  font-size: 13px;
}

.perm-body {
  max-height: 420px;
  overflow-y: auto;
}

.perm-group {
  margin-bottom: 16px;
}

.perm-group-title {
  font-weight: 600;
  margin-bottom: 8px;
  color: #303133;
}
</style>
