import { defineStore } from 'pinia'
import http from '@/api/http'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: localStorage.getItem('access_token') || '',
    user: JSON.parse(localStorage.getItem('user') || 'null'),
  }),
  getters: {
    isAuthenticated: (state) => !!state.token,
    displayName: (state) => state.user?.displayName || state.user?.username || '用户',
    roles: (state) => state.user?.roles || [],
    // 权限仅用于控制前端展示（菜单/按钮），真正的鉴权在服务端
    permissions: (state) => state.user?.permissions || [],
    hasPermission: (state) => (permission) => (state.user?.permissions || []).includes(permission),
    hasAnyPermission: (state) => (permissions) =>
      permissions.some((permission) => (state.user?.permissions || []).includes(permission)),
  },
  actions: {
    async login(username, password) {
      const data = await http.post('/auth/login', { username, password })
      this.token = data.accessToken
      this.user = data.user
      localStorage.setItem('access_token', data.accessToken)
      localStorage.setItem('user', JSON.stringify(data.user))
      return data
    },
    /** 重新拉取当前用户（角色或权限变更后调用），使菜单与按钮立即生效 */
    async refreshProfile() {
      const user = await http.get('/auth/me')
      this.user = user
      localStorage.setItem('user', JSON.stringify(user))
      return user
    },
    logout() {
      this.token = ''
      this.user = null
      localStorage.removeItem('access_token')
      localStorage.removeItem('user')
    },
  },
})
