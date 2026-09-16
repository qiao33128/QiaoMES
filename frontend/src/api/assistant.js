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

  // ---------------- 模型配置（需要 assistant:manage） ----------------

  /** 读取生效配置；API Key 只回掩码，明文不出接口 */
  getConfig() {
    return http.get('/assistant/config')
  },
  /**
   * 保存配置（保存即生效，无需重启）
   * @param {{enabled?:boolean, baseUrl?:string, model?:string, apiKey?:string,
   *          clearApiKey?:boolean, llmTimeoutSeconds?:number, maxRows?:number,
   *          queryTimeoutSeconds?:number, maxRepairAttempts?:number}} payload
   */
  saveConfig(payload) {
    return http.put('/assistant/config', payload)
  },
  /** 测试模型连接：发一个最小请求，确认地址 / 密钥 / 模型名可用 */
  testConfig() {
    return http.post('/assistant/config/test', null, { timeout: 120000 })
  },
}

export const ChartTypeMap = {
  table: '表格',
  bar: '柱状图',
  line: '折线图',
  pie: '饼图',
}
