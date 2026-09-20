import http from './http'

/**
 * 改进建议与迭代审阅。
 *
 * 说明：真正的建议记录、迭代计划、审阅留痕、执行审计都在 **AI 迭代服务**里，
 * QiaoMES 只做「入口 + 权限闸门」—— 所以这里全是转发，前端拿到的就是迭代服务的原始结构。
 *
 * 提交建议会调大模型做「评审 + 与已有计划交叉对比」，所以超时单独放宽。
 */
export const iterationApi = {
  /** 当前迭代周期 + 本期计划条目 + 最近一次一致性检查结论 */
  current() {
    return http.get('/iteration/cycle')
  },
  /**
   * 提交建议
   * @param {{title:string, body:string, category:'Modify'|'New',
   *          sourceFeature?:string, sourcePermission?:string,
   *          actorId?:string, actorName?:string}} payload
   *
   * category=Modify 时后端会校验：你确实持有 sourcePermission（也就是你能用这个功能）——
   * 只能对**自己可用的功能**提修改建议；category=New（新增功能）需要 iteration:manage。
   */
  submit(payload) {
    return http.post('/iteration/suggestions', payload, { timeout: 240000 })
  },
  /**
   * 审阅计划条目（需要 iteration:manage）
   * @param {string} itemId
   * @param {{approve?:boolean, reject?:boolean, note?:string, actor?:string}} payload
   *   note 非空表示提意见 —— 该条目本期先顺延，等按意见完善后再进下一期。
   */
  review(itemId, payload) {
    return http.post(`/iteration/plan-items/${itemId}/review`, payload)
  },
  /**
   * 推进周期（需要 iteration:manage）
   * @param {'freeze'|'settle'|'execute'} action
   */
  advance(action) {
    return http.post(`/iteration/cycles/current/${action}`)
  },
}
