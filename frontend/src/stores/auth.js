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
    logout() {
      this.token = ''
      this.user = null
      localStorage.removeItem('access_token')
      localStorage.removeItem('user')
    },
  },
})
