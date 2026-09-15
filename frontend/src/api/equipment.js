import http from './http'

/** 设备台账与状态 */
export const equipmentApi = {
  list(params) {
    return http.get('/equipment/equipments', { params })
  },
  summary() {
    return http.get('/equipment/equipments/summary')
  },
  downtimePareto(params) {
    return http.get('/equipment/equipments/downtime-pareto', { params })
  },
  getById(id) {
    return http.get(`/equipment/equipments/${id}`)
  },
  create(data) {
    return http.post('/equipment/equipments', data)
  },
  update(id, data) {
    return http.put(`/equipment/equipments/${id}`, data)
  },
  changeStatus(id, data) {
    return http.post(`/equipment/equipments/${id}/status`, data)
  },
  setActive(id, isActive) {
    return http.put(`/equipment/equipments/${id}/active`, null, { params: { isActive } })
  },
  addMaintenance(id, data) {
    return http.post(`/equipment/equipments/${id}/maintenance`, data)
  },
}

/** Andon 呼叫 */
export const andonApi = {
  list(params) {
    return http.get('/equipment/andon-calls', { params })
  },
  create(data) {
    return http.post('/equipment/andon-calls', data)
  },
  respond(id) {
    return http.post(`/equipment/andon-calls/${id}/respond`)
  },
  resolve(id, data) {
    return http.post(`/equipment/andon-calls/${id}/resolve`, data)
  },
  close(id, data) {
    return http.post(`/equipment/andon-calls/${id}/close`, data)
  },
}

/** 设备状态：0 运行 / 1 待机 / 2 故障 / 3 保养 / 4 离线 */
export const EquipmentStatusMap = {
  0: { label: '运行', type: 'success', color: '#67c23a' },
  1: { label: '待机', type: 'info', color: '#909399' },
  2: { label: '故障', type: 'danger', color: '#f56c6c' },
  3: { label: '保养', type: 'warning', color: '#e6a23c' },
  4: { label: '离线', type: 'info', color: '#c0c4cc' },
}

export const MaintenanceTypeMap = { 0: '日常点检', 1: '定期保养', 2: '维修' }
export const MaintenanceResultMap = { 0: '正常', 1: '异常' }

export const AndonTypeMap = { 0: '设备故障', 1: '质量异常', 2: '缺料', 3: '其它' }
export const AndonLevelMap = { 0: '黄灯', 1: '红灯' }
export const AndonStatusMap = {
  0: { label: '待响应', type: 'danger' },
  1: { label: '已响应', type: 'warning' },
  2: { label: '已解决', type: 'success' },
  3: { label: '已关闭', type: 'info' },
}
