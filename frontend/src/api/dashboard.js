import http from './http'

/** 车间大屏 / 指标聚合 */
export const dashboardApi = {
  /** 大屏总览：产量 / 良率 / 设备状态 / Andon（一次拿全） */
  overview(params) {
    return http.get('/dashboard/overview', { params })
  },
}
