import http from './http'

// 工单状态映射
export const WorkOrderStatusMap = {
  0: { label: '草稿', type: 'info' },
  1: { label: '已下达', type: 'primary' },
  2: { label: '生产中', type: 'warning' },
  3: { label: '已完成', type: 'success' },
  4: { label: '已取消', type: 'danger' },
}

export const workOrderApi = {
  list(params) {
    return http.get('/work-orders', { params })
  },
  getById(id) {
    return http.get(`/work-orders/${id}`)
  },
  create(data) {
    return http.post('/work-orders', data)
  },
  update(id, data) {
    return http.put(`/work-orders/${id}`, data)
  },
  release(id) {
    return http.post(`/work-orders/${id}/release`)
  },
  start(id) {
    return http.post(`/work-orders/${id}/start`)
  },
  report(id, quantity) {
    return http.post(`/work-orders/${id}/report`, { quantity })
  },
  complete(id) {
    return http.post(`/work-orders/${id}/complete`)
  },
  cancel(id) {
    return http.post(`/work-orders/${id}/cancel`)
  },
}
