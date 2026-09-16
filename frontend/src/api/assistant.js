import http from './http'

/**
 * 智能问数：自然语言 → 只读 SQL → 结果集。
 * 超时单独放宽：一次提问可能包含「生成 → 执行失败 → 自我修复」多个来回。
 */
export const assistantApi = {
  /** 能力自检：是否启用、模型是否配好、语义层覆盖多少张表 */
  status() {
    return http.get('/assistant/status')
  },
  /** 查看喂给模型的语义层文本（调试用） */
  schema() {
    return http.get('/assistant/schema')
  },
  /**
   * 提问
   * @param {{question: string, maxRows?: number, previousQuestion?: string, previousSql?: string}} payload
   */
  ask(payload) {
    return http.post('/assistant/ask', payload, { timeout: 240000 })
  },
}

export const ChartTypeMap = {
  table: '表格',
  bar: '柱状图',
  line: '折线图',
  pie: '饼图',
}
