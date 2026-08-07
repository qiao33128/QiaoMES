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
        meta: { title: '工单管理' },
      },
      {
        path: 'dashboard',
        name: 'dashboard',
        component: () => import('@/views/dashboard/DashboardView.vue'),
        meta: { title: '生产看板' },
      },
    ],
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

// 路由守卫：未登录跳转到登录页
router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login' }
  }
  if (to.meta.public && auth.isAuthenticated) {
    return { path: '/' }
  }
  document.title = to.meta.title ? `${to.meta.title} - QiaoMES` : 'QiaoMES'
})

export default router
