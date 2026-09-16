<template>
  <el-container class="layout">
    <el-aside :width="collapsed ? '64px' : '220px'" class="aside">
      <div class="logo-area">
        <el-icon :size="26" color="#409EFF"><Monitor /></el-icon>
        <span v-if="!collapsed" class="logo-text">QiaoMES</span>
      </div>
      <el-menu
        :default-active="activeMenu"
        :collapse="collapsed"
        :collapse-transition="false"
        router
        background-color="#1f2d3d"
        text-color="#c0c4cc"
        active-text-color="#409EFF"
      >
        <el-menu-item index="/dashboard">
          <el-icon><Odometer /></el-icon>
          <template #title>生产看板</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('workorders:read')" index="/work-orders">
          <el-icon><Document /></el-icon>
          <template #title>工单管理</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('workorders:read')" index="/serial-numbers">
          <el-icon><Position /></el-icon>
          <template #title>SN 过站</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('reporting:read')" index="/reports">
          <el-icon><TrendCharts /></el-icon>
          <template #title>报表与班次</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('assistant:read')" index="/assistant">
          <el-icon><MagicStick /></el-icon>
          <template #title>智能问数</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('workorders:read')" index="/display">
          <el-icon><Monitor /></el-icon>
          <template #title>车间大屏</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('equipment:read')" index="/equipment">
          <el-icon><Bell /></el-icon>
          <template #title>设备与 Andon</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('workorders:read')" index="/traceability">
          <el-icon><Search /></el-icon>
          <template #title>追溯查询</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('quality:read')" index="/quality">
          <el-icon><CircleCheck /></el-icon>
          <template #title>质量管理</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('masterdata:read')" index="/master-data">
          <el-icon><Grid /></el-icon>
          <template #title>主数据维护</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('users:read')" index="/users">
          <el-icon><User /></el-icon>
          <template #title>用户管理</template>
        </el-menu-item>
        <el-menu-item v-if="authStore.hasPermission('roles:read')" index="/roles">
          <el-icon><Key /></el-icon>
          <template #title>角色与权限</template>
        </el-menu-item>
      </el-menu>
    </el-aside>

    <el-container>
      <el-header class="header">
        <div class="header-left">
          <el-icon class="collapse-btn" @click="collapsed = !collapsed">
            <Fold v-if="!collapsed" />
            <Expand v-else />
          </el-icon>
          <span class="page-title">{{ currentTitle }}</span>
        </div>
        <div class="header-right">
          <el-dropdown @command="handleCommand">
            <span class="user-info">
              <el-icon><UserFilled /></el-icon>
              {{ authStore.displayName }}
              <el-icon><ArrowDown /></el-icon>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>

      <el-main class="main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import {
  Monitor,
  Odometer,
  Document,
  Grid,
  Key,
  Position,
  CircleCheck,
  Bell,
  Search,
  TrendCharts,
  MagicStick,
  User,
  UserFilled,
  Fold,
  Expand,
  ArrowDown,
} from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const collapsed = ref(false)

const activeMenu = computed(() => route.path)
const currentTitle = computed(() => route.meta.title || 'QiaoMES')

async function handleCommand(command) {
  if (command === 'logout') {
    await ElMessageBox.confirm('确定要退出登录吗？', '提示', {
      confirmButtonText: '退出',
      cancelButtonText: '取消',
      type: 'warning',
    })
    authStore.logout()
    router.push({ name: 'login' })
  }
}
</script>

<style scoped>
.layout {
  height: 100vh;
}

.aside {
  background-color: #1f2d3d;
  transition: width 0.2s;
  overflow: hidden;
}

.logo-area {
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: #fff;
}

.logo-text {
  font-size: 20px;
  font-weight: 600;
  letter-spacing: 1px;
  white-space: nowrap;
}

.aside :deep(.el-menu) {
  border-right: none;
}

.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  height: 60px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 16px;
}

.collapse-btn {
  font-size: 20px;
  cursor: pointer;
  color: #606266;
}

.page-title {
  font-size: 16px;
  font-weight: 500;
  color: #303133;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  color: #606266;
  outline: none;
}

.main {
  background: #f0f2f5;
  padding: 20px;
  overflow-y: auto;
}
</style>
