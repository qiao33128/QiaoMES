import { defineStore } from 'pinia'
import { assistantApi } from '@/api/assistant'

const STORAGE_KEY = 'qiaomes_assistant_history'

/** 最多保留多少条问答。聊多了也没必要全留,切页面能接上就够。 */
const MAX_ITEMS = 20

/** 落盘体积上限:结果集行数可能很多,太大就只留在内存里,不往 sessionStorage 塞。 */
const MAX_BYTES = 1200 * 1024

function readPersisted() {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return []
    }
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? parsed.slice(0, MAX_ITEMS) : []
  } catch {
    // 解析失败(格式变过 / 被改坏)就当没有:不能因为一段缓存把页面搞崩
    return []
  }
}

function writePersisted(history) {
  try {
    const payload = JSON.stringify(history.slice(0, MAX_ITEMS))
    if (payload.length > MAX_BYTES) {
      sessionStorage.removeItem(STORAGE_KEY)
      return
    }
    sessionStorage.setItem(STORAGE_KEY, payload)
  } catch {
    // 无痕模式 / 配额满 → 静默降级为「只在内存里保留」
  }
}

/**
 * 智能问数的会话状态。
 *
 * <para>
 * 为什么必须放在 store 里、而不是页面组件里:这个应用**没有用 keep-alive**,
 * 路由一切走页面组件就卸载了 ——
 * ① 提问记录会丢;
 * ② 更糟的是「正在分析中」的那次请求:它还在后台飞着,但没有组件接手结果,
 *    回来以后页面是空的、按钮也不再转圈,用户只能白等一场重问一遍。
 * 把请求生命周期交给 store 之后,切页面完全不影响它,回来就能看到这次的结果。
 * </para>
 * <para>
 * 另外用 sessionStorage 兜一层:刷新(F5)也能接上,标签页关掉即清。
 * 换会话(登录 / 退出)会主动清空,避免下一个用户看到上一个用户的问数结果。
 * </para>
 */
export const useAssistantStore = defineStore('assistant', {
  state: () => ({
    status: null,
    /** 输入框草稿也留着:切页面回来接着改,不用重打 */
    question: '',
    followUp: false,
    asking: false,
    /** [{ id, question, answer, chart }],最新的在最前面 */
    history: [],
  }),

  getters: {
    hasHistory: (state) => state.history.length > 0,
    lastQuery: (state) => state.history[0] ?? null,
  },

  actions: {
    /** 从 sessionStorage 恢复(只需在页面挂载时调用一次,已有记录则不覆盖)。 */
    restore() {
      if (this.history.length === 0) {
        this.history = readPersisted()
      }
    },

    async loadStatus() {
      try {
        this.status = await assistantApi.status()
      } catch {
        this.status = null
      }
      return this.status
    },

    /**
     * 提问。**请求生命周期归 store**:组件卸载了照样把结果收进 history。
     * @returns {Promise<{ok: boolean, answered?: boolean, reason?: string}>}
     */
    async ask(text) {
      const value = String(text ?? this.question ?? '').trim()
      if (!value) {
        return { ok: false, reason: 'empty' }
      }
      // 防重复提交;注意这次判断也跨页面生效,切走再回来按钮仍然是禁用状态
      if (this.asking) {
        return { ok: false, reason: 'busy' }
      }

      this.asking = true
      try {
        const payload = { question: value }
        if (this.followUp && this.history.length > 0) {
          const last = this.history[0]
          payload.previousQuestion = last.question
          payload.previousSql = last.answer?.sql || undefined
        }

        const answer = await assistantApi.ask(payload)
        this.history.unshift({
          id: `${Date.now()}-${this.history.length}`,
          question: value,
          answer,
          chart: answer.answered ? answer.chart || 'table' : 'table',
        })
        writePersisted(this.history)

        return { ok: true, answered: answer.answered }
      } catch (error) {
        return { ok: false, reason: 'error', error }
      } finally {
        this.asking = false
      }
    },

    /** 切换某条结果的展示形式(表格 / 柱状 / 折线 / 饼图)。 */
    setChart(item, chart) {
      if (!item) {
        return
      }
      item.chart = chart
      writePersisted(this.history)
    },

    clearHistory() {
      this.history = []
      writePersisted(this.history)
    },

    /** 换会话时调用(登录 / 退出):清干净,别把上一个人的问数结果留给下一个人。 */
    reset() {
      this.history = []
      this.question = ''
      this.followUp = false
      this.asking = false
      try {
        sessionStorage.removeItem(STORAGE_KEY)
      } catch {
        // 无痕模式下 sessionStorage 不可用,忽略即可
      }
    },
  },
})
