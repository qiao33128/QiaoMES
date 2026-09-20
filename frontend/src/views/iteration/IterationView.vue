<template>
  <div class="iteration-view">
    <el-card shadow="never">
      <div class="head">
        <div>
          <h3>改进建议与迭代计划</h3>
          <p class="sub">
            提建议 → AI 评审并写入迭代计划（同时与已有计划做一致性检查）→ 管理员审阅 → 到期自动执行并走 CI/CD。
          </p>
        </div>
        <el-button :loading="loading" @click="load">刷新</el-button>
      </div>
      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    </el-card>

    <!-- 周期 -->
    <el-card v-if="cycle" shadow="never">
      <div class="head">
        <b>
          周期 {{ cycle.name }}
          <el-tag size="small" :type="cycle.status === 'Collecting' ? 'success' : 'warning'">
            {{ statusText(cycle.status) }}
          </el-tag>
        </b>
        <span v-if="canManage" class="row">
          <el-button size="small" @click="advance('freeze')">冻结收集</el-button>
          <el-button size="small" @click="advance('settle')">结算</el-button>
          <el-button size="small" type="primary" @click="advance('execute')">执行已放行</el-button>
        </span>
      </div>
      <p class="sub">
        收集 {{ fmt(cycle.collectFrom) }} · 冻结 {{ fmt(cycle.freezeAt) }} ·
        审阅至 {{ fmt(cycle.reviewUntil) }} · 执行 {{ fmt(cycle.executeAt) }}
      </p>
      <p class="sub">
        沉默语义：<b>低风险沉默即放行</b>；<b>中 / 高风险沉默即顺延</b>（要管理员显式点头）。
      </p>
      <p v-if="lastCheck" class="sub">
        最近一致性检查：<b>{{ lastCheck.verdict }}</b>（{{ fmt(lastCheck.createdAt) }}）
      </p>
      <div v-if="conflicts.length" class="issues">
        <p class="issue-title">冲突（AI 交叉对比的结论，未裁决前该条目不会被自动执行）：</p>
        <p v-for="(text, index) in conflicts" :key="'c' + index" class="issue">· {{ text }}</p>
      </div>
      <div v-if="ambiguities.length" class="issues">
        <p class="issue-title">歧义（描述不明确，需补充说明）：</p>
        <p v-for="(text, index) in ambiguities" :key="'a' + index" class="issue">· {{ text }}</p>
      </div>
    </el-card>

    <!-- 计划 -->
    <el-card shadow="never">
      <template #header>
        <b>本期计划</b>
        <span class="sub">（{{ items.length }} 条）</span>
      </template>
      <el-table :data="items" border stripe size="small">
        <el-table-column label="条目" min-width="240">
          <template #default="{ row }">
            <div class="item-title">{{ row.title }}</div>
            <div class="sub">{{ row.intent }}</div>
          </template>
        </el-table-column>
        <el-table-column label="风险" width="90">
          <template #default="{ row }">
            <el-tag size="small" :type="riskType(row.risk)">{{ row.risk }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">{{ statusText(row.status) }}</template>
        </el-table-column>
        <el-table-column label="影响面 / 风险依据" min-width="260">
          <template #default="{ row }">
            <div class="sub">{{ impactText(row.impact) }}</div>
            <div class="sub">{{ row.riskReason }}</div>
            <div v-if="row.decisionNote" class="sub note">意见：{{ row.decisionNote }}</div>
          </template>
        </el-table-column>
        <el-table-column v-if="canManage" label="审阅" width="230">
          <template #default="{ row }">
            <el-button size="small" @click="review(row, { approve: true })">批准</el-button>
            <el-button size="small" type="danger" plain @click="review(row, { reject: true })">否决</el-button>
            <el-input
              v-model="notes[row.id]"
              size="small"
              placeholder="提意见（回车提交，该条本期先顺延）"
              class="note-input"
              @keyup.enter="review(row, { note: notes[row.id] })"
            />
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="本期还没有条目" :image-size="60" />
        </template>
      </el-table>
    </el-card>

    <!-- 提建议 -->
    <el-card shadow="never">
      <template #header><b>提改进建议</b></template>
      <el-form label-width="96px">
        <el-form-item label="建议类型">
          <el-radio-group v-model="form.category">
            <el-radio value="Modify">修改现有功能</el-radio>
            <el-radio v-if="canManage" value="New">新增功能（仅管理员）</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="form.category === 'Modify'" label="针对功能">
          <el-select v-model="form.sourcePermission" placeholder="选择你要改进的功能" style="width: 320px">
            <el-option v-for="option in myFeatures" :key="option.code" :label="option.label" :value="option.code" />
          </el-select>
          <span class="sub" style="margin-left: 10px">只能对自己可用的功能提建议</span>
        </el-form-item>
        <el-form-item label="标题">
          <el-input v-model="form.title" maxlength="80" show-word-limit placeholder="一句话说清你想改什么" />
        </el-form-item>
        <el-form-item label="详细说明">
          <el-input
            v-model="form.body"
            type="textarea"
            :rows="5"
            placeholder="现在是什么样、你希望变成什么样（说得越具体，AI 的实现越准；例如指出具体页面、操作路径、期望效果）"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="submitting" @click="submit">提交建议</el-button>
          <span class="sub" style="margin-left: 12px">提交后 AI 会评审并与已有计划交叉对比，约 10~30 秒</span>
        </el-form-item>
      </el-form>
      <el-alert v-if="submitResult" :title="submitResult" :type="submitType" show-icon :closable="false" />
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { iterationApi } from '@/api/iteration'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

const loading = ref(false)
const error = ref('')
const cycle = ref(null)
const items = ref([])
const lastCheck = ref(null)
const notes = reactive({})

const submitting = ref(false)
const submitResult = ref('')
const submitType = ref('success')

const form = reactive({
  category: 'Modify',
  sourcePermission: '',
  title: '',
  body: '',
})

const canManage = computed(() => authStore.hasPermission('iteration:manage'))

/**
 * 「自己可用的功能」= 自己持有的 *:read 权限。
 * 🔴 后端会再校验一次（复用 perm: 动态策略），这里只是给出可选项 —— 前端过滤不是安全边界。
 */
const FEATURE_LABELS = {
  'workorders:read': '工单 / SN 过站 / 追溯查询 / 车间大屏',
  'quality:read': '质量管理',
  'equipment:read': '设备与 Andon',
  'masterdata:read': '主数据维护',
  'reporting:read': '报表与班次',
  'assistant:read': '智能问数',
  'roles:read': '角色与权限',
  'users:read': '用户管理',
}

const myFeatures = computed(() => {
  const owned = authStore.user?.permissions || []
  return owned
    .filter((code) => code.endsWith(':read'))
    .map((code) => ({ code, label: FEATURE_LABELS[code] || code }))
})

function fmt(value) {
  return value ? new Date(value).toLocaleString() : '-'
}

function riskType(risk) {
  return { High: 'danger', Medium: 'warning', Low: 'success' }[risk] || 'info'
}

function statusText(status) {
  return {
    Collecting: '收集中',
    UnderReview: '审阅中',
    Scheduled: '已排期',
    Executing: '执行中',
    Completed: '已完成',
    Proposed: '待审阅',
    Accepted: '已放行',
    Rejected: '已否决',
    Deferred: '已顺延',
    Done: '已完成',
    Failed: '执行失败',
  }[status] || status
}

function impactText(impact) {
  if (!impact) return ''
  try {
    const parsed = typeof impact === 'string' ? JSON.parse(impact) : impact
    const parts = []
    if (parsed.modules?.length) parts.push(`模块 ${parsed.modules.join('、')}`)
    if (parsed.files?.length) parts.push(`文件 ${parsed.files.join('、')}`)
    if (parsed.tables?.length) parts.push(`表 ${parsed.tables.join('、')}`)
    if (parsed.apis?.length) parts.push(`接口 ${parsed.apis.join('、')}`)
    if (parsed.permissions?.length) parts.push(`权限 ${parsed.permissions.join('、')}`)
    return parts.join(' · ')
  } catch {
    return String(impact)
  }
}

function parseList(json) {
  if (!json) return []
  try {
    const parsed = typeof json === 'string' ? JSON.parse(json) : json
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

const conflicts = computed(() => parseList(lastCheck.value?.conflictsJson).map(describeIssue))
const ambiguities = computed(() => parseList(lastCheck.value?.ambiguitiesJson).map(describeIssue))

function describeIssue(issue) {
  if (typeof issue === 'string') return issue
  const pairs = [issue.leftTitle, issue.rightTitle].filter(Boolean).join(' ↔ ')
  return [pairs, issue.detail || issue.reason].filter(Boolean).join('：')
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const data = await iterationApi.current()
    cycle.value = data.cycle
    items.value = data.items || []
    lastCheck.value = data.lastCheck
  } catch (exception) {
    error.value = exception?.response?.data?.detail || exception?.message || '加载失败'
  } finally {
    loading.value = false
  }
}

async function submit() {
  if (!form.title.trim() || !form.body.trim()) {
    ElMessage.warning('标题和详细说明都要填')
    return
  }
  if (form.category === 'Modify' && !form.sourcePermission) {
    ElMessage.warning('请选择这条建议针对哪个功能')
    return
  }

  submitting.value = true
  submitResult.value = ''
  try {
    const result = await iterationApi.submit({
      title: form.title.trim(),
      body: form.body.trim(),
      category: form.category,
      sourcePermission: form.category === 'Modify' ? form.sourcePermission : null,
      sourceFeature: (form.sourcePermission || 'new').split(':')[0],
      actorName: authStore.user?.displayName || authStore.user?.username,
    })

    submitType.value = result.verdict === 'Clean' ? 'success' : 'warning'
    submitResult.value =
      result.verdict === 'Clean'
        ? `已写入本期计划：${result.message}`
        : `未直接合并（${result.verdict}）：${result.message}`

    form.title = ''
    form.body = ''
    await load()
  } catch (exception) {
    submitType.value = 'error'
    submitResult.value = exception?.response?.data?.detail || exception?.message || '提交失败'
  } finally {
    submitting.value = false
  }
}

async function review(row, payload) {
  try {
    await iterationApi.review(row.id, { ...payload, actor: authStore.user?.username })
    ElMessage.success('已记录')
    notes[row.id] = ''
    await load()
  } catch (exception) {
    ElMessage.error(exception?.response?.data?.detail || '操作失败')
  }
}

async function advance(action) {
  try {
    await iterationApi.advance(action)
    ElMessage.success('已推进')
    await load()
  } catch (exception) {
    ElMessage.error(exception?.response?.data?.detail || '操作失败')
  }
}

onMounted(load)
</script>

<style scoped>
.iteration-view {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.row {
  display: flex;
  gap: 8px;
  align-items: center;
}

h3 {
  margin: 0 0 4px;
  font-size: 16px;
}

.sub {
  margin: 2px 0;
  font-size: 12px;
  line-height: 1.7;
  color: #909399;
}

.item-title {
  font-weight: 600;
}

.note {
  color: #e6a23c;
}

.note-input {
  margin-top: 6px;
}

.issues {
  margin-top: 8px;
  padding: 8px 10px;
  border-radius: 6px;
  background: #fef0f0;
}

.issue-title {
  margin: 0 0 4px;
  font-size: 12px;
  font-weight: 600;
  color: #f56c6c;
}

.issue {
  margin: 0;
  font-size: 12px;
  line-height: 1.7;
  color: #606266;
}
</style>
