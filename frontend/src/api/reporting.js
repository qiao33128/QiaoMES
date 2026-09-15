import http from './http'

/** 班次与生产日历 */
export const shiftApi = {
  list(params) {
    return http.get('/reporting/shifts', { params })
  },
  getById(id) {
    return http.get(`/reporting/shifts/${id}`)
  },
  create(data) {
    return http.post('/reporting/shifts', data)
  },
  update(id, data) {
    return http.put(`/reporting/shifts/${id}`, data)
  },
  setActive(id, isActive) {
    return http.put(`/reporting/shifts/${id}/active`, null, { params: { isActive } })
  },
  /** 当前所处班次（含生产日） */
  current(lineName) {
    return http.get('/reporting/shifts/current', { params: { lineName } })
  },
  /** 生产日区间 → 班次时间窗（UTC 边界） */
  ranges(params) {
    return http.get('/reporting/shifts/ranges', { params })
  },
  calendar(params) {
    return http.get('/reporting/calendar', { params })
  },
  upsertCalendarDay(data) {
    return http.post('/reporting/calendar', data)
  },
}

/** 指标报表 */
export const reportApi = {
  oee(params) {
    return http.get('/reports/oee', { params })
  },
  shiftMetrics(params) {
    return http.get('/reports/shifts', { params })
  },
  quality(params) {
    return http.get('/reports/quality', { params })
  },
  achievement(params) {
    return http.get('/reports/achievement', { params })
  },
  downtime(params) {
    return http.get('/reports/downtime', { params })
  },
  /** 导出 CSV（返回 Blob） */
  exportCsv(type, params) {
    return http.get('/reports/export', { params: { type, ...params }, responseType: 'blob' })
  },
}
