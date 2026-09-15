import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '@/router'

const http = axios.create({
  baseURL: '/api',
  timeout: 15000,
})

// 请求拦截器：附加 JWT
http.interceptors.request.use((config) => {
  const token = localStorage.getItem('access_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截器：统一错误处理
http.interceptors.response.use(
  (response) => response.data,
  (error) => {
    const status = error.response?.status
    const data = error.response?.data || {}
    // 后端统一返回 ProblemDetails：{ title, status, code, traceId, detail }
    const message = data.title || data.message || data.description || data.detail || data.Error?.Description
    if (status === 401) {
      localStorage.removeItem('access_token')
      localStorage.removeItem('user')
      ElMessage.error('登录已过期，请重新登录')
      router.push({ name: 'login' })
    } else if (status === 403) {
      ElMessage.error(message || '当前账号没有执行该操作的权限')
    } else {
      ElMessage.error(message || error.message || '请求失败')
    }
    return Promise.reject(error)
  }
)

export default http
