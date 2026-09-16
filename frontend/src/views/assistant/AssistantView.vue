<template>
  <div class="assistant-page">
    <!-- 能力状态 -->
    <el-alert
      v-if="status && (!status.enabled || !status.llmConfigured)"
      :title="status.hint"
      :type="status.enabled ? 'warning' : 'info'"
      show-icon
      :closable="false"
    />

    <el-card v-if="status" shadow="never" class="status-card">
      <div class="status-row">
        <el-tag :type="status.enabled ? 'success' : 'info'" size="small">
          {{ status.enabled ? '已启用' : '已关闭' }}
        </el-tag>
        <el-tag :type="status.llmConfigured ? 'success' : 'danger'" size="small">
          模型：{{ status.model || '未配置' }}
        </el-tag>
        <el-tag type="info" size="small">语义层 {{ status.tableCount }} 张表</el-tag>
        <el-tag type="info" size="small">最多返回 {{ status.maxRows }} 行</el-tag>
        <el-tag type="info" size="small">SQL 超时 {{ status.queryTimeoutSeconds }} 秒</el-tag>
        <el-tag :type="status.hasDedicatedReadOnlyConnection ? 'success' : 'warning'" size="small">
          {{ status.hasDedicatedReadOnlyConnection ? '独立只读连接' : '复用主连接（只读事务兜底）' }}
        </el-tag>
        <el-button link type="primary" :icon="Refresh" @click="loadStatus">刷新</el-button>
      </div>
    </el-card>

    <!-- 提问 -->
    <el-card shadow="never">
      <el-input
        v-model="question"
        type="textarea"
        :rows="2"
        resize="none"
        placeholder="用中文提问，例如：最近 7 天各产线的良率是多少？（Ctrl + Enter 直接提问）"
        @keydown.ctrl.enter.prevent="ask()"
      />

      <div class="examples">
        <span class="examples-label">试试：</span>
        <el-tag
          v-for="example in examples"
          :key="example"
          class="example-tag"
          effect="plain"
          @click="ask(example)"
        >
          {{ example }}
        </el-tag>
      </div>

      <div class="actions">
        <el-checkbox v-model="followUp" :disabled="!history.length">
          基于上一次查询追问
        </el-checkbox>
        <el-button type="primary" :icon="Promotion" :loading="asking" @click="ask()">
          {{ asking ? '分析中…' : '提问' }}
        </el-button>
        <el-button v-if="history.length" :icon="Delete" @click="history = []">清空记录</el-button>
      </div>
    </el-card>

    <!-- 结果 -->
    <el-card v-for="item in history" :key="item.id" shadow="never" class="result-card">
      <template #header>
        <div class="result-header">
          <span class="result-question">{{ item.question }}</span>
          <div class="result-meta">
            <el-tag v-if="item.answer.answered" size="small" type="success">
              {{ item.answer.rowCount }} 行 · {{ item.answer.elapsedMs }} ms
            </el-tag>
            <el-tag v-if="item.answer.truncated" size="small" type="warning">已截断</el-tag>
            <el-tag v-if="item.answer.attempts > 1" size="small" type="info">
              自我修复 {{ item.answer.attempts - 1 }} 次
            </el-tag>
            <el-tag v-if="item.answer.model" size="small" type="info">{{ item.answer.model }}</el-tag>
          </div>
        </div>
      </template>

      <!-- 答不出来 -->
      <template v-if="!item.answer.answered">
        <el-alert
          :title="item.answer.failure || item.answer.explanation || '没能给出可执行的查询'"
          type="error"
          :closable="false"
          show-icon
        />
        <p v-if="item.answer.explanation && item.answer.failure" class="explanation">
          {{ item.answer.explanation }}
        </p>
      </template>

      <template v-else>
        <p v-if="item.answer.explanation" class="explanation">{{ item.answer.explanation }}</p>
        <p v-if="item.answer.thought" class="thought">思路：{{ item.answer.thought }}</p>

        <div class="toolbar">
          <el-radio-group v-model="item.chart" size="small">
            <el-radio-button value="table">表格</el-radio-button>
            <el-radio-button value="bar">柱状图</el-radio-button>
            <el-radio-button value="line">折线图</el-radio-button>
            <el-radio-button value="pie">饼图</el-radio-button>
          </el-radio-group>
          <div class="toolbar-right">
            <el-button size="small" :icon="CopyDocument" @click="copySql(item)">复制 SQL</el-button>
            <el-button size="small" :icon="Download" @click="exportCsv(item)">导出 CSV</el-button>
          </div>
        </div>

        <ResultChart
          v-if="item.chart !== 'table'"
          :columns="item.answer.columns"
          :rows="item.answer.rows"
          :chart="item.chart"
          :x-field="item.answer.xField || ''"
          :y-field="item.answer.yField || ''"
        />

        <el-table v-else :data="toObjects(item.answer)" border stripe max-height="460" size="small">
          <el-table-column
            v-for="column in item.answer.columns"
            :key="column.name"
            :prop="column.name"
            :label="column.name"
            min-width="140"
            show-overflow-tooltip
          />
          <template #empty>
            <el-empty description="查询没有返回数据" :image-size="70" />
          </template>
        </el-table>

        <el-collapse class="sql-collapse">
          <el-collapse-item title="查看生成的 SQL">
            <pre class="sql-block">{{ item.answer.sql }}</pre>
          </el-collapse-item>
        </el-collapse>
      </template>
    </el-card>

    <el-empty
      v-if="!history.length && !asking"
      description="还没有提问记录。上面挑一个示例试试，或者直接输入你的问题。"
    />
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { CopyDocument, Delete, Download, Promotion, Refresh } from '@element-plus/icons-vue'
import { assistantApi } from '@/api/assistant'
import ResultChart from './ResultChart.vue'

const status = ref(null)
const question = ref('')
const asking = ref(false)
const followUp = ref(false)
const history = ref([])

const examples = [
  '最近 7 天每条产线的良率是多少？',
  '本月不良代码 TOP 10 及占比',
  '各产品的一次合格率 FPY 对比',
  '停机时长最长的 5 台设备是哪些？',
  '近 30 天白班和夜班的产量趋势',
  '已完工工单的达成率排名',
  '当前有多少条 Andon 呼叫还没响应？',
  '哪几道工序的不良数最高？',
]

async function loadStatus() {
  try {
    status.value = await assistantApi.status()
  } catch {
    status.value = null
  }
}

async function ask(text) {
  const value = (text ?? question.value).trim()
  if (!value) {
    ElMessage.warning('请先输入问题')
    return
  }
  if (asking.value) {
    return
  }

  asking.value = true
  try {
    const payload = { question: value }
    if (followUp.value && history.value.length > 0) {
      const last = history.value[0]
      payload.previousQuestion = last.question
      payload.previousSql = last.answer?.sql || undefined
    }

    const answer = await assistantApi.ask(payload)
    history.value.unshift({
      id: `${Date.now()}-${history.value.length}`,
      question: value,
      answer,
      chart: answer.answered ? answer.chart || 'table' : 'table',
    })

    if (!answer.answered) {
      ElMessage.warning('这次没能给出可执行的查询，详情见下方提示')
    }
  } finally {
    asking.value = false
  }
}

/** 结果集 → el-table 需要的对象数组（列名做 key）。 */
function toObjects(answer) {
  const names = answer.columns.map((column) => column.name)
  return answer.rows.map((row) => {
    const record = {}
    names.forEach((name, index) => {
      record[name] = row[index] === null || row[index] === undefined ? '' : row[index]
    })
    return record
  })
}

async function copySql(item) {
  const sql = item.answer.sql || ''
  try {
    await navigator.clipboard.writeText(sql)
    ElMessage.success('SQL 已复制')
  } catch {
    ElMessage.warning('浏览器拒绝了剪贴板写入，请手动从下方代码块复制')
  }
}

function exportCsv(item) {
  const answer = item.answer
  const escape = (value) => {
    if (value === null || value === undefined) {
      return ''
    }
    const text = String(value)
    return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
  }

  const lines = [answer.columns.map((column) => escape(column.name)).join(',')]
  answer.rows.forEach((row) => lines.push(row.map(escape).join(',')))

  // 带 BOM，Excel 打开中文才不乱码
  const blob = new Blob(['\uFEFF', lines.join('\r\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `问数结果-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '')}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

onMounted(loadStatus)
</script>

<style scoped>
.assistant-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.status-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.examples {
  margin-top: 12px;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.examples-label {
  font-size: 13px;
  color: #909399;
}

.example-tag {
  cursor: pointer;
}

.actions {
  margin-top: 14px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.actions .el-checkbox {
  margin-right: auto;
}

.result-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.result-question {
  font-weight: 600;
  color: #303133;
}

.result-meta {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.explanation {
  margin: 0 0 8px;
  color: #303133;
  line-height: 1.6;
}

.thought {
  margin: 0 0 12px;
  color: #909399;
  font-size: 13px;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.toolbar-right {
  display: flex;
  gap: 8px;
}

.sql-collapse {
  margin-top: 12px;
}

.sql-block {
  margin: 0;
  padding: 12px;
  background: #f5f7fa;
  border-radius: 4px;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  color: #303133;
}
</style>
