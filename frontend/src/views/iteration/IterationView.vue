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
        <span class="row">
          <el-button v-if="canManage" size="small" @click="openConfig">迭代服务配置</el-button>
          <el-button :loading="loading" @click="load">刷新</el-button>
        </span>
      </div>
      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false">
        <template v-if="notConfigured" #default>
          <span class="sub">
            这个提示说的是「功能处于关闭状态」，不是故障（迭代服务没配地址时就该这样，页面不会报错）。
            需要管理员在右上角「迭代服务配置」里填上地址并保存，保存即生效、无需重启。
          </span>
        </template>
      </el-alert>
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

    <!-- 迭代服务配置（仅管理员）：填完保存即生效，管理员密钥只回掩码 -->
    <el-dialog v-model="configVisible" title="迭代服务配置" width="640px" @open="loadConfig">
      <el-alert type="info" :closable="false" show-icon>
        <template #default>
          <span class="sub">
            QiaoMES 只做「入口 + 权限闸门 + 密钥代持」：建议与计划都存在独立的 AI 迭代服务里，
            管理员密钥由「服务端」代持、不下发浏览器。把地址留空并保存 = 关停「改进建议」功能。
          </span>
        </template>
      </el-alert>

      <el-form label-width="110px" style="margin-top: 14px">
        <el-form-item label="服务地址">
          <el-input v-model="config.baseUrl" placeholder="http://ai-iteration:8080" clearable />
          <div class="sub">
            同容器网络用服务名（推荐，不必对外暴露端口）；本机 Docker 用 http://host.docker.internal:8091。
            保存后立即生效，不用重启；下次部署也还在（配置存在服务器上挂载出来的配置文件里）。
          </div>
        </el-form-item>
        <el-form-item label="管理员密钥">
          <el-input
            v-model="config.adminKey"
            type="password"
            show-password
            clearable
            :placeholder="configSnapshot?.adminKeyMasked ? '已配置 ' + configSnapshot.adminKeyMasked + '（留空 = 不改）' : '留空 = 不改动现有密钥'"
          />
          <div class="sub">
            只以掩码回显，明文既不进数据库、也不出接口 —— 它只写在服务器上的
            <code>config/iteration.json</code>（0600，与 .env 同一档保护）。
            迭代服务端的 Admin__ApiKey 换值时，这里要同步改。
          </div>
        </el-form-item>
        <el-form-item label="超时（秒）">
          <el-input-number v-model="config.timeoutSeconds" :min="5" :max="900" :step="10" />
          <div class="sub">提交建议要调大模型做评审与一致性检查，默认 180 秒。</div>
        </el-form-item>
        <el-form-item label="当前来源">
          <el-tag size="small" :type="configSnapshot?.source === 'file' ? 'success' : 'info'">
            {{ configSnapshot?.source === 'file' ? '页面上配过（存在服务器的配置文件里）' : '还没在页面上配（沿用部署配置）' }}
          </el-tag>
          <span v-if="configSnapshot?.updatedAt" class="sub" style="margin-left: 10px">
            最后修改 {{ fmt(configSnapshot.updatedAt) }}
            <template v-if="configSnapshot.updatedBy"> · {{ configSnapshot.updatedBy }}</template>
          </span>
        </el-form-item>
      </el-form>

      <el-alert v-if="configResult" :title="configResult" :type="configResultType" show-icon :closable="false" />

      <template #footer>
        <el-button :loading="configTesting" @click="testConfig">测试连接</el-button>
        <el-button type="primary" :loading="configSaving" @click="saveConfig">保存并生效</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { iterationApi } from '@/api/iteration'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

// ---------------- 迭代服务配置（需要 iteration:manage） ----------------
const configVisible = ref(false)
const configSnapshot = ref(null)
const configSaving = ref(false)
const configTesting = ref(false)
const configResult = ref('')
const configResultType = ref('success')
const config = reactive({ baseUrl: '', adminKey: '', timeoutSeconds: 180 })

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

// ---------------- 迭代服务配置（需要 iteration:manage） ----------------

/**
 * 后端那句「迭代服务还没配置」= 功能处于关闭状态，不是故障。
 * 页面据此多说一句「去哪开」，否则用户只会以为是坏了。
 */
const notConfigured = computed(() => (error.value || '').includes('还没配置'))

async function loadConfig() {
  configResult.value = ''
  try {
    const snapshot = await iterationApi.getConfig()
    configSnapshot.value = snapshot
    config.baseUrl = snapshot.baseUrl || ''
    config.timeoutSeconds = snapshot.timeoutSeconds ?? 180
    config.adminKey = '' // 密钥永远不回显：留空 = 不改动
  } catch (exception) {
    configResultType.value = 'error'
    configResult.value = exception?.response?.data?.detail || '读取配置失败（需要 iteration:manage 权限）'
  }
}

function openConfig() {
  configResult.value = ''
  configVisible.value = true
}

async function saveConfig() {
  const baseUrl = (config.baseUrl || '').trim()
  configSaving.value = true
  configResult.value = ''
  try {
    const snapshot = await iterationApi.saveConfig({
      baseUrl,
      adminKey: config.adminKey ? config.adminKey.trim() : null,
      timeoutSeconds: config.timeoutSeconds,
      // 地址留空 = 显式关停（否则后端会把"未提供"当成"不改动"）
      clearBaseUrl: !baseUrl,
    })

    configSnapshot.value = snapshot
    config.adminKey = ''
    configResultType.value = 'success'
    configResult.value = snapshot.configured
      ? `已保存并生效：${snapshot.baseUrl}（无需重启；点「刷新」即可看到周期与计划）`
      : '已保存：地址为空，改进建议功能已关停。'
    ElMessage.success('已保存并生效')
    await load()
  } catch (exception) {
    configResultType.value = 'error'
    configResult.value = exception?.response?.data?.detail || '保存失败'
  } finally {
    configSaving.value = false
  }
}

async function testConfig() {
  if ((config.baseUrl || '').trim() !== (configSnapshot.value?.baseUrl || '')) {
    ElMessage.warning('检测到未保存的修改：测试连接用的是「已保存」的配置，请先保存再测')
  }

  configTesting.value = true
  configResult.value = ''
  try {
    const probe = await iterationApi.testConfig()
    configResultType.value = probe.ok ? 'success' : 'warning'
    configResult.value = probe.message
  } catch (exception) {
    configResultType.value = 'error'
    configResult.value = exception?.response?.data?.detail || '测试失败'
  } finally {
    configTesting.value = false
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
