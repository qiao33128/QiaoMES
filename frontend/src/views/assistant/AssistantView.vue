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
        <el-button
          v-if="authStore.hasPermission('assistant:manage')"
          link
          type="primary"
          :icon="Setting"
          @click="openConfig"
        >
          模型配置
        </el-button>
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
        <el-button v-if="history.length" :icon="Delete" @click="assistantStore.clearHistory()">
          清空记录
        </el-button>
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
          <el-radio-group
            :model-value="item.chart"
            size="small"
            @update:model-value="(value) => assistantStore.setChart(item, value)"
          >
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
            <div class="empty-hint">
              <el-empty description="查询执行成功，但没匹配到数据" :image-size="70" />
              <p>
                SQL 是跑通了的，只是结果为空。常见原因：① 筛选维度的口径不对（例如按产线/班次拆分时，
                预聚合表的产线列需要班次定义绑定产线）；② 这段时间确实没有数据；③ 演示数据还没灌
                （在开发机执行 <code>tools/seed-demo.ps1</code>）。
              </p>
            </div>
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

    <!-- 模型配置抽屉：保存即生效，不需要重启或重新部署 -->
    <el-drawer v-model="configVisible" title="智能问数 · 模型配置" size="500px">
      <div v-loading="configLoading">
        <el-alert
          v-if="configMeta.source === 'configuration'"
          title="当前仍在用 appsettings / 环境变量里的配置。在这里保存过之后，就以这里的为准。"
          type="info"
          :closable="false"
          show-icon
          class="config-tip"
        />
        <el-alert
          v-else
          :title="`当前配置来自数据库（最后修改：${configMeta.updatedBy || '-'} · ${formatTime(configMeta.updatedAt)}）`"
          type="success"
          :closable="false"
          show-icon
          class="config-tip"
        />

        <el-form label-position="top">
          <el-form-item label="总开关">
            <el-switch v-model="configForm.enabled" active-text="启用" inactive-text="关闭" />
          </el-form-item>

          <el-form-item label="模型地址（OpenAI 兼容）">
            <el-input v-model="configForm.baseUrl" placeholder="https://api.deepseek.com/v1" />
            <div class="field-hint">
              DeepSeek / 通义千问 / Kimi / 硅基流动填各自的 <code>/v1</code> 地址；本地 Ollama 用
              <code>http://host.docker.internal:11434/v1</code>
            </div>
          </el-form-item>

          <el-form-item label="模型名">
            <el-input v-model="configForm.model" placeholder="deepseek-chat" />
          </el-form-item>

          <el-form-item label="API Key">
            <el-input
              v-model="configForm.apiKey"
              type="password"
              show-password
              :placeholder="
                configMeta.hasApiKey
                  ? `已配置 ${configMeta.apiKeyMasked}，留空表示不改动`
                  : '本地 Ollama 可留空；其它服务需要填写'
              "
            />
            <div class="field-hint">
              只以掩码回显，明文不会通过接口返回；加密后存库（密钥来自 <code>Jwt:SecretKey</code>）。
              <el-button
                v-if="configMeta.hasApiKey"
                link
                type="danger"
                size="small"
                @click="clearApiKey"
              >
                清除密钥
              </el-button>
            </div>
          </el-form-item>

          <el-row :gutter="12">
            <el-col :span="12">
              <el-form-item label="模型超时（秒）">
                <el-input-number v-model="configForm.llmTimeoutSeconds" :min="10" :max="600" :step="10" />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="返回行数上限">
                <el-input-number v-model="configForm.maxRows" :min="1" :max="2000" :step="50" />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="SQL 超时（秒）">
                <el-input-number v-model="configForm.queryTimeoutSeconds" :min="1" :max="300" />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="失败自我修复轮数">
                <el-input-number v-model="configForm.maxRepairAttempts" :min="0" :max="5" />
              </el-form-item>
            </el-col>
          </el-row>
        </el-form>

        <el-alert
          v-if="probe"
          :title="probe.message"
          :type="probe.ok ? 'success' : 'error'"
          :closable="false"
          show-icon
          class="config-tip"
        />
      </div>

      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 8px; flex-wrap: wrap">
          <el-button :icon="Connection" :loading="testing" @click="testConfig">测试连接</el-button>
          <el-button @click="configVisible = false">关闭</el-button>
          <el-button type="primary" :icon="Check" :loading="saving" @click="saveConfig">
            保存并生效
          </el-button>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Check,
  Connection,
  CopyDocument,
  Delete,
  Download,
  Promotion,
  Refresh,
  Setting,
} from '@element-plus/icons-vue'
import { assistantApi } from '@/api/assistant'
import { useAssistantStore } from '@/stores/assistant'
import { useAuthStore } from '@/stores/auth'
import ResultChart from './ResultChart.vue'

const authStore = useAuthStore()
const assistantStore = useAssistantStore()

// 🔴 这些状态住在 store 里,不能放组件:本应用没有 keep-alive,路由一切走页面组件就卸载了,
// 放组件里会连同「正在分析中」的那次请求一起丢掉 —— 请求还在后台飞,却没有组件接手结果。
// 详见 stores/assistant.js。
const { status, question, asking, followUp, history } = storeToRefs(assistantStore)

// ---------- 模型配置 ----------
const configVisible = ref(false)
const configLoading = ref(false)
const saving = ref(false)
const testing = ref(false)
const probe = ref(null)
const configMeta = ref({
  hasApiKey: false,
  apiKeyMasked: null,
  source: 'configuration',
  updatedAt: null,
  updatedBy: null,
})
const configForm = reactive({
  enabled: true,
  baseUrl: '',
  model: '',
  apiKey: '',
  llmTimeoutSeconds: 90,
  maxRows: 200,
  queryTimeoutSeconds: 15,
  maxRepairAttempts: 2,
})

const examples = [
  '最近 7 天每条产线的良率是多少？',
  '本月不良代码 TOP 10 及占比',
  '各产品的一次合格率 FPY 对比',
  '停机时长最长的 5 台设备是哪些？',
  '近 30 天白班和夜班的产量趋势',
  '已完工工单的达成率排名',
  '当前有多少条 Andon 呼叫还没响应？',
  '哪几道工序的不良数最高？',
  '最近 30 天报废 SN 数',
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

// ---------- 模型配置 ----------

function applyConfig(data) {
  configForm.enabled = data.enabled
  configForm.baseUrl = data.baseUrl || ''
  configForm.model = data.model || ''
  configForm.apiKey = '' // 明文永不下发，留空表示不改动
  configForm.llmTimeoutSeconds = data.llmTimeoutSeconds
  configForm.maxRows = data.maxRows
  configForm.queryTimeoutSeconds = data.queryTimeoutSeconds
  configForm.maxRepairAttempts = data.maxRepairAttempts
  configMeta.value = {
    hasApiKey: data.hasApiKey,
    apiKeyMasked: data.apiKeyMasked,
    source: data.source,
    updatedAt: data.updatedAt,
    updatedBy: data.updatedBy,
  }
}

async function openConfig() {
  configVisible.value = true
  probe.value = null
  configLoading.value = true
  try {
    applyConfig(await assistantApi.getConfig())
  } catch {
    ElMessage.error('读取模型配置失败')
  } finally {
    configLoading.value = false
  }
}

async function saveConfig() {
  saving.value = true
  try {
    // apiKey 为空 → 让后端沿用现有密钥（只改模型名不会把 Key 抹掉）
    const saved = await assistantApi.saveConfig({
      ...configForm,
      apiKey: configForm.apiKey ? configForm.apiKey : null,
    })
    applyConfig(saved)
    ElMessage.success('已保存并立即生效（无需重启）')
    await loadStatus()
  } finally {
    saving.value = false
  }
}

async function clearApiKey() {
  await ElMessageBox.confirm(
    '清除后智能问数将无法调用大模型（会变成"未配置模型"状态），确定清除吗？',
    '提示',
    { confirmButtonText: '清除', cancelButtonText: '取消', type: 'warning' },
  )
  saving.value = true
  try {
    applyConfig(await assistantApi.saveConfig({ clearApiKey: true }))
    ElMessage.success('密钥已清除')
    await loadStatus()
  } finally {
    saving.value = false
  }
}

async function testConfig() {
  testing.value = true
  probe.value = null
  try {
    probe.value = await assistantApi.testConfig()
    if (probe.value.ok) {
      ElMessage.success('连接正常')
    }
  } finally {
    testing.value = false
  }
}

function formatTime(value) {
  if (!value) {
    return '-'
  }
  return String(value).replace('T', ' ').slice(0, 19)
}

// ---------- 结果呈现 ----------

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

onMounted(() => {
  // 先恢复上次的记录(切页面 / 刷新后仍在),再刷新能力状态
  assistantStore.restore()
  loadStatus()
})
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

.config-tip {
  margin-bottom: 14px;
}

.empty-hint p {
  margin: 0 auto;
  max-width: 520px;
  font-size: 12px;
  line-height: 1.8;
  color: #909399;
}

.empty-hint code {
  padding: 0 4px;
  border-radius: 3px;
  background: #f0f2f5;
  color: #606266;
}

.field-hint {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.6;
  color: #909399;
}

.field-hint code {
  padding: 0 4px;
  border-radius: 3px;
  background: #f0f2f5;
  color: #606266;
}
</style>
