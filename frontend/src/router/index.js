import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { public: true, title: '登录' },
  },
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    children: [
      {
        path: '',
        redirect: '/work-orders',
      },
      {
        path: 'work-orders',
        name: 'work-orders',
        component: () => import('@/views/workorder/WorkOrderList.vue'),
        meta: { title: '工单管理', permission: 'workorders:read' },
      },
      {
        path: 'dashboard',
        name: 'dashboard',
        component: () => import('@/views/dashboard/DashboardView.vue'),
        meta: { title: '生产看板' },
      },
      {
        path: 'roles',
        name: 'roles',
        component: () => import('@/views/admin/RoleList.vue'),
        meta: { title: '角色与权限', permission: 'roles:read' },
      },
      {
        path: 'users',
        name: 'users',
        component: () => import('@/views/admin/UserList.vue'),
        meta: { title: '用户管理', permission: 'users:read' },
      },
      {
        path: 'forbidden',
        name: 'forbidden',
        component: () => import('@/views/ForbiddenView.vue'),
        meta: { title: '无访问权限' },
      },
    ],
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

// 路由守卫：未登录跳转登录页；有权限要求的页面按权限拦截
router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login' }
  }
  if (to.meta.public && auth.isAuthenticated) {
    return { path: '/' }
  }
  if (to.meta.permission && !auth.hasPermission(to.meta.permission)) {
    return { name: 'forbidden' }
  }
  document.title = to.meta.title ? `${to.meta.title} - QiaoMES` : 'QiaoMES'
})

export default router
