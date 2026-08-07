<template>
  <div class="dashboard">
    <el-row :gutter="16" class="stats-row">
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-value">{{ stats.total }}</div>
          <div class="stat-label">工单总数</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-value" style="color: #e6a23c">{{ stats.inProgress }}</div>
          <div class="stat-label">生产中</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-value" style="color: #409eff">{{ stats.released }}</div>
          <div class="stat-label">已下达</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-value" style="color: #67c23a">{{ stats.completed }}</div>
          <div class="stat-label">已完成</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="board-card">
      <template #header>
        <div class="board-header">
          <span>生产看板</span>
          <div class="board-status">
            <el-tag :type="connected ? 'success' : 'danger'" size="small">
              {{ connected ? '实时连接中' : '连接断开' }}
            </el-tag>
          </div>
        </div>
      </template>

      <el-table :data="orders" v-loading="loading" stripe>
        <el-table-column prop="orderNumber" label="工单号" width="180" />
        <el-table-column prop="productName" label="产品" min-width="140" />
        <el-table-column label="进度" min-width="200">
          <template #default="{ row }">
            <el-progress
              :percentage="progress(row)"
              :status="row.status === 3 ? 'success' : row.status === 4 ? 'exception' : undefined"
            />
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusMap[row.status]?.type">
              {{ statusMap[row.status]?.label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="实时事件" width="180">
          <template #default>
            <span class="live-badge">●</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { workOrderApi, WorkOrderStatusMap } from '@/api/workorder'

const loading = ref(false)
const connected = ref(false)
const orders = ref([])
const statusMap = WorkOrderStatusMap

const stats = reactive({
  total: 0,
  released: 0,
  inProgress: 0,
  completed: 0,
})

let connection = null

function progress(row) {
  if (!row.plannedQuantity) return 0
  return Math.min(100, Math.round((row.completedQuantity / row.plannedQuantity) * 100))
}

function updateStats() {
  stats.total = orders.value.length
  stats.released = orders.value.filter((o) => o.status === 1).length
  stats.inProgress = orders.value.filter((o) => o.status === 2).length
  stats.completed = orders.value.filter((o) => o.status === 3).length
}

async function loadData() {
  loading.value = true
  try {
    const data = await workOrderApi.list({ page: 1, pageSize: 100 })
    orders.value = data.items
    updateStats()
  } finally {
    loading.value = false
  }
}

async function connectSignalR() {
  const token = localStorage.getItem('access_token')
  connection = new HubConnectionBuilder()
    .withUrl(`/hubs/production?access_token=${token}`)
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect()
    .build()

  connection.on('WorkOrderChanged', async (event) => {
    // 收到实时事件后刷新数据
    console.log(`[看板] ${event.action}: ${event.workOrder.orderNumber}`)
    await loadData()
  })

  connection.onreconnected(async () => {
    connected.value = true
    await loadData()
  })

  connection.onclose(() => {
    connected.value = false
  })

  try {
    await connection.start()
    connected.value = true
  } catch (e) {
    connected.value = false
    console.error('SignalR 连接失败:', e)
  }
}

onMounted(() => {
  loadData()
  connectSignalR()
})

onBeforeUnmount(() => {
  if (connection) {
    connection.stop()
  }
})
</script>

<style scoped>
.stats-row {
  margin-bottom: 16px;
}

.stat-card {
  text-align: center;
}

.stat-value {
  font-size: 32px;
  font-weight: 700;
  color: #303133;
  line-height: 1.2;
}

.stat-label {
  margin-top: 6px;
  color: #909399;
  font-size: 13px;
}

.board-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.live-badge {
  color: #67c23a;
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}
</style>
