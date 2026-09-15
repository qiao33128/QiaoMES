import http from './http'

/** SN 过站接口。SN 可能包含特殊字符，统一 encode。 */
export const serialNumberApi = {
  list(params) {
    return http.get('/production/serial-numbers', { params })
  },
  getBySn(sn) {
    return http.get(`/production/serial-numbers/${encodeURIComponent(sn)}`)
  },
  generate(data) {
    return http.post('/production/serial-numbers/generate', data)
  },
  trackIn(sn, data) {
    return http.post(`/production/serial-numbers/${encodeURIComponent(sn)}/track-in`, data)
  },
  trackOut(sn, data) {
    return http.post(`/production/serial-numbers/${encodeURIComponent(sn)}/track-out`, data)
  },
  scrap(sn, remark) {
    return http.post(`/production/serial-numbers/${encodeURIComponent(sn)}/scrap`, null, {
      params: { remark },
    })
  },
}

/** SN 状态：0 在制 / 1 已完工 / 2 已报废 / 3 已挂起 */
export const SerialNumberStatusMap = {
  0: { label: '在制', type: 'warning' },
  1: { label: '已完工', type: 'success' },
  2: { label: '已报废', type: 'danger' },
  3: { label: '已挂起', type: 'info' },
}

/** 过站动作与结果 */
export const WipActionMap = { 0: '进站', 1: '出站' }
export const WipResultMap = { 0: '-', 1: '合格', 2: '不合格' }
