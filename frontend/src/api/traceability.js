import http from './http'

/** SN 正 / 反向追溯与批次影响范围 */
export const traceabilityApi = {
  /** 按 SN 拉取「人机料法环」追溯报告 */
  bySn(sn) {
    return http.get(`/traceability/sn/${encodeURIComponent(sn)}`)
  },
  /** 批次影响范围（某工单下全部 SN 的状态汇总） */
  byWorkOrder(workOrderId) {
    return http.get(`/traceability/batch/${workOrderId}`)
  },
}
