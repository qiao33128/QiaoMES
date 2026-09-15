import http from './http'

/** 检验单 */
export const inspectionApi = {
  list(params) {
    return http.get('/quality/inspections', { params })
  },
  getById(id) {
    return http.get(`/quality/inspections/${id}`)
  },
  getBySn(sn) {
    return http.get(`/quality/inspections/by-sn/${encodeURIComponent(sn)}`)
  },
  create(data) {
    return http.post('/quality/inspections', data)
  },
  addItems(id, items) {
    return http.post(`/quality/inspections/${id}/items`, { items })
  },
  recordItem(id, data) {
    return http.put(`/quality/inspections/${id}/items/record`, data)
  },
  submit(id, data) {
    return http.post(`/quality/inspections/${id}/submit`, data)
  },
}

/** 不合格品处置（NCR） */
export const nonconformanceApi = {
  list(params) {
    return http.get('/quality/nonconformances', { params })
  },
  getById(id) {
    return http.get(`/quality/nonconformances/${id}`)
  },
  create(data) {
    return http.post('/quality/nonconformances', data)
  },
  decide(id, data) {
    return http.post(`/quality/nonconformances/${id}/decide`, data)
  },
  startRepair(id, data) {
    return http.post(`/quality/nonconformances/${id}/repairs`, data)
  },
  completeRepair(id, data) {
    return http.post(`/quality/nonconformances/${id}/repairs/complete`, data)
  },
  reinspect(id, data) {
    return http.post(`/quality/nonconformances/${id}/reinspect`, data)
  },
  scrap(id, remark) {
    return http.post(`/quality/nonconformances/${id}/scrap`, null, { params: { remark } })
  },
}

/** 不良代码 */
export const defectCodeApi = {
  list(params) {
    return http.get('/quality/defect-codes', { params })
  },
  pareto(params) {
    return http.get('/quality/defect-codes/pareto', { params })
  },
  create(data) {
    return http.post('/quality/defect-codes', data)
  },
  update(id, data) {
    return http.put(`/quality/defect-codes/${id}`, data)
  },
  setStatus(id, isActive) {
    return http.put(`/quality/defect-codes/${id}/status`, null, { params: { isActive } })
  },
}

/** SPC */
export const spcApi = {
  trend(params) {
    return http.get('/quality/spc/trend', { params })
  },
}

export const InspectionTypeMap = { 0: 'IQC 来料', 1: 'IPQC 过程', 2: 'FQC 成品', 3: 'OQC 出货' }

export const InspectionStatusMap = {
  0: { label: '待检验', type: 'info' },
  1: { label: '检验中', type: 'warning' },
  2: { label: '合格', type: 'success' },
  3: { label: '不合格', type: 'danger' },
  4: { label: '让步接收', type: 'warning' },
}

export const ConclusionMap = { 0: '-', 1: '合格', 2: '不合格', 3: '让步接收' }

export const DispositionTypeMap = { 0: '返工', 1: '返修', 2: '让步接收', 3: '报废', 4: '退货' }

export const DispositionStatusMap = {
  0: { label: '待处理', type: 'info' },
  1: { label: '处理中', type: 'warning' },
  2: { label: '待复检', type: 'primary' },
  3: { label: '已关闭', type: 'success' },
}

/** 来料批次与批次谱系 */
export const materialLotApi = {
  list(params) {
    return http.get('/quality/material-lots', { params })
  },
  getById(id) {
    return http.get(`/quality/material-lots/${id}`)
  },
  getByLotNumber(lotNumber) {
    return http.get(`/quality/material-lots/by-lot-number/${encodeURIComponent(lotNumber)}`)
  },
  create(data) {
    return http.post('/quality/material-lots', data)
  },
  /** 登记 IQC 结论（决定批次能否投产） */
  inspect(id, data) {
    return http.post(`/quality/material-lots/${id}/inspect`, data)
  },
  setFrozen(id, data) {
    return http.post(`/quality/material-lots/${id}/freeze`, data)
  },
  /** SN 绑定来料批次（批量、幂等） */
  bind(items) {
    return http.post('/quality/material-consumptions', { items })
  },
  /** 正向：某 SN 用了哪些批次 */
  consumptionsBySn(sn) {
    return http.get(`/quality/material-consumptions/by-sn/${encodeURIComponent(sn)}`)
  },
  /** 反向：某批次流向了哪些 SN */
  traceByLot(lotNumber, take = 200) {
    return http.get(`/quality/material-consumptions/by-lot/${encodeURIComponent(lotNumber)}`, { params: { take } })
  },
}

/** 来料批次状态：0 待检 / 1 合格可用 / 2 不合格 / 3 冻结 / 4 已耗尽 */
export const MaterialLotStatusMap = {
  0: { label: '待检', type: 'info' },
  1: { label: '合格可用', type: 'success' },
  2: { label: '不合格', type: 'danger' },
  3: { label: '冻结', type: 'warning' },
  4: { label: '已耗尽', type: 'info' },
}
