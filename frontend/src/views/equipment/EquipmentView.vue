<template>
  <div class="equipment-page">
    <el-tabs v-model="activeTab">
      <el-tab-pane label="设备台账" name="equipments">
        <el-row :gutter="12" class="summary-row">
          <el-col v-for="item in summaryCards" :key="item.status" :span="4">
            <el-card shadow="never" class="summary-card">
              <div class="summary-value" :style="{ color: item.color }">{{ item.count }}</div>
              <div class="summary-label">{{ item.label }}</div>
            </el-card>
          </el-col>
          <el-col :span="4">
            <el-card shadow="never" class="summary-card">
              <div class="summary-value">{{ summary.total }}</div>
              <div class="summary-label">设备总数</div>
            </el-card>
          </el-col>
        </el-row>

        <el-card shadow="never" class="filter-card">
          <el-form :inline="true" :model="equipmentQuery">
            <el-form-item label="关键字">
              <el-input v-model="equipmentQuery.keyword" placeholder="编号 / 名称 / 型号" clearable style="width: 180px" @keyup.enter="loadEquipments" />
            </el-form-item>
            <el-form-item label="状态">
              <el-select v-model="equipmentQuery.status" placeholder="全部" clearable style="width: 120px">
                <el-option v-for="(item, key) in EquipmentStatusMap" :key="key" :label="item.label" :value="Number(key)" />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :icon="Search" @click="loadEquipments">查询</el-button>
              <el-button :icon="Refresh" @click="resetEquipmentQuery">重置</el-button>
            </el-form-item>
            <el-form-item style="float: right">
              <el-button type="primary" :icon="Plus" @click="openEquipmentForm()">新增设备</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="equipments" v-loading="loading" stripe>
            <el-table-column prop="code" label="设备编号" width="140" />
            <el-table-column prop="name" label="名称" min-width="150" />
            <el-table-column prop="model" label="型号" width="110" />
            <el-table-column prop="lineName" label="产线" width="110" />
            <el-table-column label="状态" width="110">
              <template #default="{ row }">
                <el-tag :type="EquipmentStatusMap[row.status]?.type" size="small">
                  {{ EquipmentStatusMap[row.status]?.label }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="当前状态持续" width="130">
              <template #default="{ row }">{{ formatDuration(row.currentStatusSeconds) }}</template>
            </el-table-column>
            <el-table-column label="累计停机" width="120">
              <template #default="{ row }">{{ formatDuration(row.totalDownSeconds) }}</template>
            </el-table-column>
            <el-table-column label="状态原因" min-width="140">
              <template #default="{ row }">{{ row.statusReason || '-' }}</template>
            </el-table-column>
            <el-table-column label="操作" width="230" fixed="right">
              <template #default="{ row }">
                <el-button size="small" type="primary" plain @click="openStatusDialog(row)">切换状态</el-button>
                <el-button size="small" plain @click="openMaintenance(row)">点检</el-button>
                <el-button size="small" plain @click="openEquipmentForm(row)">编辑</el-button>
              </template>
            </el-table-column>
          </el-table>

          <el-pagination
            class="pagination"
            layout="total, prev, pager, next"
            :total="equipmentTotal"
            v-model:current-page="equipmentQuery.page"
            v-model:page-size="equipmentQuery.pageSize"
            @current-change="loadEquipments"
          />
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="Andon 呼叫" name="andon">
        <el-card shadow="never" class="filter-card">
          <el-form :inline="true" :model="andonQuery">
            <el-form-item label="状态">
              <el-select v-model="andonQuery.status" placeholder="全部" clearable style="width: 120px">
                <el-option v-for="(item, key) in AndonStatusMap" :key="key" :label="item.label" :value="Number(key)" />
              </el-select>
            </el-form-item>
            <el-form-item label="只看未结束">
              <el-switch v-model="andonQuery.onlyOpen" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :icon="Search" @click="loadAndonCalls">查询</el-button>
              <el-button :icon="Refresh" @click="resetAndonQuery">重置</el-button>
            </el-form-item>
            <el-form-item style="float: right">
              <el-button type="danger" :icon="Bell" @click="openCallDialog">一键呼叫</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="never">
          <el-table :data="andonCalls" v-loading="loading" stripe>
            <el-table-column prop="callNumber" label="呼叫单号" width="180" />
            <el-table-column label="类型" width="100">
              <template #default="{ row }">{{ AndonTypeMap[row.type] }}</template>
            </el-table-column>
            <el-table-column label="级别" width="90">
              <template #default="{ row }">
                <el-tag :type="row.level === 1 ? 'danger' : 'warning'" size="small">
                  {{ AndonLevelMap[row.level] }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="设备 / 产线" width="150">
              <template #default="{ row }">{{ row.equipmentCode || row.workCenterName || '-' }}</template>
            </el-table-column>
            <el-table-column prop="description" label="描述" min-width="200" />
            <el-table-column label="状态" width="150">
              <template #default="{ row }">
                <el-tag :type="AndonStatusMap[row.status]?.type" size="small">
                  {{ AndonStatusMap[row.status]?.label }}
                </el-tag>
                <el-tag v-if="row.escalated" type="danger" size="small" effect="dark" class="escalated">已升级</el-tag>
                <el-tag v-else-if="row.isTimeout" type="warning" size="small">超时</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="呼叫时间" width="170">
              <template #default="{ row }">{{ formatTime(row.calledAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="200" fixed="right">
              <template #default="{ row }">
                <el-button v-if="row.status === 0" size="small" type="primary" plain @click="handleRespond(row)">响应</el-button>
                <el-button v-if="row.status <= 1" size="small" type="success" plain @click="openResolveDialog(row)">解决</el-button>
                <el-button v-else size="small" plain disabled>已结束</el-button>
              </template>
            </el-table-column>
          </el-table>

          <el-pagination
            class="pagination"
            layout="total, prev, pager, next"
            :total="andonTotal"
            v-model:current-page="andonQuery.page"
            v-model:page-size="andonQuery.pageSize"
            @current-change="loadAndonCalls"
          />
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <!-- 设备表单 -->
    <el-dialog v-model="formVisible" :title="form.id ? '编辑设备' : '新增设备'" width="520px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="设备编号" required>
          <el-input v-model="form.code" :disabled="!!form.id" placeholder="如 EQ-SMT-001" />
        </el-form-item>
        <el-form-item label="名称" required>
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="型号">
          <el-input v-model="form.model" />
        </el-form-item>
        <el-form-item label="序列号">
          <el-input v-model="form.serialNumber" />
        </el-form-item>
        <el-form-item label="产线">
          <el-input v-model="form.lineName" placeholder="如 SMT-1" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.remark" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="formVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleSaveEquipment">保存</el-button>
      </template>
    </el-dialog>

    <!-- 状态切换 -->
    <el-dialog v-model="statusVisible" :title="`切换状态 · ${current?.name || ''}`" width="480px">
      <el-form :model="statusForm" label-width="100px">
        <el-form-item label="新状态" required>
          <el-select v-model="statusForm.status" style="width: 100%">
            <el-option v-for="(item, key) in EquipmentStatusMap" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="statusForm.status === 2" label="停机原因码" required>
          <el-input v-model="statusForm.reasonCode" placeholder="如 BREAKDOWN / CHANGE_OVER / NO_MATERIAL" />
        </el-form-item>
        <el-form-item label="原因说明">
          <el-input v-model="statusForm.reason" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="statusVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleChangeStatus">确定</el-button>
      </template>
    </el-dialog>

    <!-- 点检 / 保养登记 -->
    <el-dialog v-model="maintenanceVisible" :title="`点检 / 保养 · ${current?.name || ''}`" width="520px">
      <el-form :model="maintenanceForm" label-width="90px">
        <el-form-item label="类型">
          <el-select v-model="maintenanceForm.type" style="width: 100%">
            <el-option v-for="(label, key) in MaintenanceTypeMap" :key="key" :label="label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="内容" required>
          <el-input v-model="maintenanceForm.content" type="textarea" :rows="2" placeholder="如 气压、导轨润滑检查" />
        </el-form-item>
        <el-form-item label="结果">
          <el-select v-model="maintenanceForm.result" style="width: 100%">
            <el-option v-for="(label, key) in MaintenanceResultMap" :key="key" :label="label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="maintenanceForm.result === 1" label="异常描述">
          <el-input v-model="maintenanceForm.abnormalDescription" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="maintenanceVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleMaintenance">提交</el-button>
      </template>
    </el-dialog>

    <!-- Andon 一键呼叫 -->
    <el-dialog v-model="callVisible" title="Andon 一键呼叫" width="520px">
      <el-form :model="callForm" label-width="100px">
        <el-form-item label="类型" required>
          <el-select v-model="callForm.type" style="width: 100%">
            <el-option v-for="(label, key) in AndonTypeMap" :key="key" :label="label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="级别">
          <el-radio-group v-model="callForm.level">
            <el-radio :value="0">黄灯（需关注）</el-radio>
            <el-radio :value="1">红灯（立即处理）</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="设备">
          <el-select v-model="callForm.equipmentId" filterable clearable placeholder="可留空" style="width: 100%">
            <el-option v-for="eq in equipments" :key="eq.id" :label="`${eq.code} ${eq.name}`" :value="eq.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述" required>
          <el-input v-model="callForm.description" type="textarea" :rows="2" placeholder="如 贴片机报警 E-204，产线停线" />
        </el-form-item>
        <el-form-item label="响应时限(分)">
          <el-input-number v-model="callForm.timeoutMinutes" :min="1" :max="120" style="width: 100%" />
        </el-form-item>
      </el-form>
      <p class="hint">超过响应时限未响应，系统会自动升级为红灯并推送看板。</p>
      <template #footer>
        <el-button @click="callVisible = false">取消</el-button>
        <el-button type="danger" :loading="submitting" @click="handleCall">呼叫</el-button>
      </template>
    </el-dialog>

    <!-- Andon 解决 -->
    <el-dialog v-model="resolveVisible" title="解决 Andon 呼叫" width="480px">
      <el-form label-width="90px">
        <el-form-item label="处理结果">
          <el-input v-model="resolveForm.resolution" type="textarea" :rows="3" placeholder="如 更换吸嘴后恢复正常" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resolveVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="handleResolve">提交</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Bell, Plus, Refresh, Search } from '@element-plus/icons-vue'
import {
  andonApi,
  AndonLevelMap,
  AndonStatusMap,
  AndonTypeMap,
  equipmentApi,
  EquipmentStatusMap,
  MaintenanceResultMap,
  MaintenanceTypeMap,
} from '@/api/equipment'

const activeTab = ref('equipments')
const loading = ref(false)
const submitting = ref(false)

const equipments = ref([])
const equipmentTotal = ref(0)
const equipmentQuery = reactive({ page: 1, pageSize: 20, keyword: '', status: null })
const summary = ref({ running: 0, idle: 0, down: 0, maintenance: 0, offline: 0, total: 0 })

const andonCalls = ref([])
const andonTotal = ref(0)
const andonQuery = reactive({ page: 1, pageSize: 20, status: null, onlyOpen: true })

const current = ref(null)
const formVisible = ref(false)
const statusVisible = ref(false)
const maintenanceVisible = ref(false)
const callVisible = ref(false)
const resolveVisible = ref(false)

const form = reactive({ id: null, code: '', name: '', model: '', serialNumber: '', lineName: '', remark: '' })
const statusForm = reactive({ status: 0, reasonCode: '', reason: '' })
const maintenanceForm = reactive({ type: 0, content: '', result: 0, abnormalDescription: '' })
const callForm = reactive({ type: 0, level: 1, equipmentId: null, description: '', timeoutMinutes: 10 })
const resolveForm = reactive({ resolution: '' })

const summaryCards = computed(() => [
  { status: 0, label: '运行', count: summary.value.running, color: EquipmentStatusMap[0].color },
  { status: 1, label: '待机', count: summary.value.idle, color: EquipmentStatusMap[1].color },
  { status: 2, label: '故障', count: summary.value.down, color: EquipmentStatusMap[2].color },
  { status: 3, label: '保养', count: summary.value.maintenance, color: EquipmentStatusMap[3].color },
  { status: 4, label: '离线', count: summary.value.offline, color: EquipmentStatusMap[4].color },
])

function formatTime(value) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '-'
}

function formatDuration(seconds) {
  if (!seconds || seconds <= 0) return '0 分'
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  return hours > 0 ? `${hours} 时 ${minutes} 分` : `${minutes} 分`
}

async function loadEquipments() {
  loading.value = true
  try {
    const data = await equipmentApi.list({
      page: equipmentQuery.page,
      pageSize: equipmentQuery.pageSize,
      keyword: equipmentQuery.keyword || undefined,
      status: equipmentQuery.status ?? undefined,
    })
    equipments.value = data.items
    equipmentTotal.value = data.totalCount
    summary.value = await equipmentApi.summary()
  } finally {
    loading.value = false
  }
}

function resetEquipmentQuery() {
  equipmentQuery.keyword = ''
  equipmentQuery.status = null
  equipmentQuery.page = 1
  loadEquipments()
}

async function loadAndonCalls() {
  loading.value = true
  try {
    const data = await andonApi.list({
      page: andonQuery.page,
      pageSize: andonQuery.pageSize,
      status: andonQuery.status ?? undefined,
      onlyOpen: andonQuery.onlyOpen || undefined,
    })
    andonCalls.value = data.items
    andonTotal.value = data.totalCount
  } finally {
    loading.value = false
  }
}

function resetAndonQuery() {
  andonQuery.status = null
  andonQuery.onlyOpen = true
  andonQuery.page = 1
  loadAndonCalls()
}

function openEquipmentForm(row) {
  Object.assign(form, {
    id: row?.id || null,
    code: row?.code || '',
    name: row?.name || '',
    model: row?.model || '',
    serialNumber: row?.serialNumber || '',
    lineName: row?.lineName || '',
    remark: row?.remark || '',
  })
  formVisible.value = true
}

async function handleSaveEquipment() {
  if (!form.code.trim() || !form.name.trim()) {
    ElMessage.warning('设备编号与名称不能为空')
    return
  }

  submitting.value = true
  try {
    const payload = {
      code: form.code,
      name: form.name,
      model: form.model || null,
      serialNumber: form.serialNumber || null,
      lineName: form.lineName || null,
      remark: form.remark || null,
    }

    if (form.id) {
      await equipmentApi.update(form.id, payload)
    } else {
      await equipmentApi.create(payload)
    }

    ElMessage.success('保存成功')
    formVisible.value = false
    await loadEquipments()
  } finally {
    submitting.value = false
  }
}

function openStatusDialog(row) {
  current.value = row
  Object.assign(statusForm, { status: 0, reasonCode: '', reason: '' })
  statusVisible.value = true
}

async function handleChangeStatus() {
  if (statusForm.status === 2 && !statusForm.reasonCode.trim()) {
    ElMessage.warning('故障停机必须填写停机原因码')
    return
  }

  submitting.value = true
  try {
    await equipmentApi.changeStatus(current.value.id, {
      status: statusForm.status,
      reasonCode: statusForm.reasonCode || null,
      reason: statusForm.reason || null,
    })
    ElMessage.success('设备状态已更新')
    statusVisible.value = false
    await loadEquipments()
  } finally {
    submitting.value = false
  }
}

function openMaintenance(row) {
  current.value = row
  Object.assign(maintenanceForm, { type: 0, content: '', result: 0, abnormalDescription: '' })
  maintenanceVisible.value = true
}

async function handleMaintenance() {
  if (!maintenanceForm.content.trim()) {
    ElMessage.warning('请填写点检 / 保养内容')
    return
  }

  submitting.value = true
  try {
    await equipmentApi.addMaintenance(current.value.id, {
      type: maintenanceForm.type,
      content: maintenanceForm.content,
      result: maintenanceForm.result,
      abnormalDescription: maintenanceForm.abnormalDescription || null,
    })
    ElMessage.success('已登记')
    maintenanceVisible.value = false
  } finally {
    submitting.value = false
  }
}

function openCallDialog() {
  Object.assign(callForm, { type: 0, level: 1, equipmentId: null, description: '', timeoutMinutes: 10 })
  callVisible.value = true
}

async function handleCall() {
  if (!callForm.description.trim()) {
    ElMessage.warning('请填写呼叫描述')
    return
  }

  submitting.value = true
  try {
    const call = await andonApi.create({
      type: callForm.type,
      level: callForm.level,
      equipmentId: callForm.equipmentId,
      description: callForm.description,
      timeoutMinutes: callForm.timeoutMinutes,
    })
    ElMessage.success(`已呼叫：${call.callNumber}`)
    callVisible.value = false
    activeTab.value = 'andon'
    await loadAndonCalls()
  } finally {
    submitting.value = false
  }
}

async function handleRespond(row) {
  await andonApi.respond(row.id)
  ElMessage.success('已响应')
  await loadAndonCalls()
}

function openResolveDialog(row) {
  current.value = row
  resolveForm.resolution = ''
  resolveVisible.value = true
}

async function handleResolve() {
  submitting.value = true
  try {
    await andonApi.resolve(current.value.id, { resolution: resolveForm.resolution || null })
    ElMessage.success('已解决')
    resolveVisible.value = false
    await loadAndonCalls()
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  await Promise.all([loadEquipments(), loadAndonCalls()])
})
</script>

<style scoped>
.equipment-page {
  display: flex;
  flex-direction: column;
}

.summary-row {
  margin-bottom: 16px;
}

.summary-card {
  text-align: center;
}

.summary-value {
  font-size: 24px;
  font-weight: 700;
}

.summary-label {
  font-size: 12px;
  color: #909399;
}

.filter-card :deep(.el-form-item) {
  margin-bottom: 0;
}

.escalated {
  margin-left: 4px;
}

.hint {
  color: #909399;
  font-size: 12px;
  margin: 0 0 0 100px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
