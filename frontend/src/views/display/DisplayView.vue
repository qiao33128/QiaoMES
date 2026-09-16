<template>
  <div class="display" :class="{ 'is-fullscreen': isFullscreen }">
    <!-- 顶栏 -->
    <header class="display-header">
      <div class="brand">
        <span class="brand-dot"></span>
        <span class="brand-name">QiaoMES 车间大屏</span>
      </div>
      <div class="clock">
        <span class="clock-time">{{ clock }}</span>
        <span class="clock-date">{{ overview?.dateText || today }}</span>
      </div>
      <div class="header-right">
        <div class="dots">
          <span
            v-for="(screen, index) in screens"
            :key="screen.key"
            class="dot"
            :class="{ active: index === activeIndex }"
            @click="goTo(index)"
          ></span>
        </div>
        <button class="ghost-btn" @click="toggleFullscreen">{{ isFullscreen ? '退出全屏' : '全屏' }}</button>
        <button class="ghost-btn" @click="refresh">刷新</button>
        <!-- 大屏是独立全屏路由（不套侧边导航），必须有明确的退出口，否则只能改地址栏 -->
        <button class="ghost-btn exit-btn" title="返回工单管理（快捷键 Esc）" @click="exitDisplay">退出大屏</button>
      </div>
    </header>

    <!-- 屏 1：Andon -->
    <section v-show="currentScreen === 'andon'" class="screen andon-screen">
      <div class="andon-stats">
        <div class="stat-card danger">
          <div class="stat-value">{{ overview?.andon.waiting ?? 0 }}</div>
          <div class="stat-label">待响应</div>
        </div>
        <div class="stat-card warn">
          <div class="stat-value">{{ overview?.andon.responded ?? 0 }}</div>
          <div class="stat-label">处理中</div>
        </div>
        <div class="stat-card blur">
          <div class="stat-value">{{ overview?.andon.timeout ?? 0 }}</div>
          <div class="stat-label">已超时</div>
        </div>
        <div class="stat-card blur">
          <div class="stat-value">{{ overview?.andon.escalated ?? 0 }}</div>
          <div class="stat-label">已升级红灯</div>
        </div>
      </div>

      <div class="andon-list">
        <div class="list-title">未结束呼叫（{{ overview?.andon.recentCalls.length || 0 }}）</div>
        <div v-if="!overview?.andon.recentCalls.length" class="empty">当前无 Andon 呼叫，产线正常</div>
        <div
          v-for="call in overview?.andon.recentCalls"
          :key="call.id"
          class="andon-item"
          :class="[call.level === 1 ? 'level-red' : 'level-yellow', { blinking: call.isTimeout }]"
        >
          <span class="andon-light"></span>
          <span class="andon-number">{{ call.callNumber }}</span>
          <span class="andon-type">{{ andonTypeLabel(call.type) }}</span>
          <span class="andon-target">{{ call.equipmentCode || call.workCenterName || '-' }}</span>
          <span class="andon-desc">{{ call.description }}</span>
          <span class="andon-status" :class="{ overtime: call.isTimeout }">{{ andonStatusLabel(call) }}</span>
        </div>
      </div>
    </section>

    <!-- 屏 2：产量达成 -->
    <section v-show="currentScreen === 'production'" class="screen production-screen">
      <div class="prod-left">
        <div class="yield-ring">
          <el-progress
            type="dashboard"
            :percentage="Number(overview?.production.yieldRate ?? 0)"
            :width="280"
            :stroke-width="18"
            :color="yieldColor"
          >
            <template #default="{ percentage }">
              <div class="ring-value">{{ percentage }}%</div>
              <div class="ring-label">良率（完工 / 完工+报废）</div>
            </template>
          </el-progress>
        </div>
      </div>
      <div class="prod-grid">
        <div class="big-stat">
          <div class="big-value">{{ overview?.production.total ?? 0 }}</div>
          <div class="big-label">今日投产 SN</div>
        </div>
        <div class="big-stat ok">
          <div class="big-value">{{ overview?.production.completed ?? 0 }}</div>
          <div class="big-label">已完工</div>
        </div>
        <div class="big-stat">
          <div class="big-value">{{ overview?.production.inProcess ?? 0 }}</div>
          <div class="big-label">在制</div>
        </div>
        <div class="big-stat warn">
          <div class="big-value">{{ overview?.production.onHold ?? 0 }}</div>
          <div class="big-label">挂起待处理</div>
        </div>
        <div class="big-stat danger">
          <div class="big-value">{{ overview?.production.scrapped ?? 0 }}</div>
          <div class="big-label">已报废</div>
        </div>
        <div class="big-stat">
          <div class="big-value">{{ finishRate }}%</div>
          <div class="big-label">完工占投产比</div>
        </div>
      </div>
    </section>

    <!-- 屏 3：质量与设备 -->
    <section v-show="currentScreen === 'quality'" class="screen quality-screen">
      <div class="quality-col">
        <div class="col-title">质量（今日）</div>
        <div class="fpy">
          <div class="fpy-value" :class="fpyClass">{{ overview?.quality.fpy ?? 0 }}%</div>
          <div class="fpy-label">一次合格率 FPY</div>
        </div>
        <div class="mini-grid">
          <div class="mini"><span class="mini-value">{{ overview?.quality.total ?? 0 }}</span><span class="mini-label">检验单</span></div>
          <div class="mini"><span class="mini-value ok">{{ overview?.quality.passed ?? 0 }}</span><span class="mini-label">合格</span></div>
          <div class="mini"><span class="mini-value danger">{{ overview?.quality.failed ?? 0 }}</span><span class="mini-label">不合格</span></div>
          <div class="mini"><span class="mini-value warn">{{ overview?.quality.concessioned ?? 0 }}</span><span class="mini-label">让步接收</span></div>
          <div class="mini"><span class="mini-value">{{ overview?.quality.pending ?? 0 }}</span><span class="mini-label">待检</span></div>
          <div class="mini"><span class="mini-value danger">{{ overview?.quality.defectQuantity ?? 0 }}</span><span class="mini-label">不良数</span></div>
        </div>
      </div>

      <div class="equipment-col">
        <div class="col-title">设备状态</div>
        <div class="equip-row">
          <div v-for="item in equipmentCards" :key="item.label" class="equip-card" :style="{ borderColor: item.color }">
            <div class="equip-value" :style="{ color: item.color }">{{ item.value }}</div>
            <div class="equip-label">{{ item.label }}</div>
          </div>
        </div>

        <div class="col-title mt">停机 TOP5（近 30 天，按次数）</div>
        <div v-if="!overview?.downtimeTop.length" class="empty small">暂无停机记录</div>
        <div v-for="item in overview?.downtimeTop" :key="item.reasonCode" class="downtime-row">
          <span class="downtime-code">{{ item.reasonCode }}</span>
          <div class="downtime-bar"><div class="downtime-fill" :style="{ width: downtimeWidth(item.count) }"></div></div>
          <span class="downtime-count">{{ item.count }} 次</span>
        </div>
      </div>
    </section>

    <footer class="display-footer">
      <span>{{ screens[activeIndex].label }}</span>
      <span class="footer-hint">每 10 秒自动轮播 · 数据 30 秒刷新一次</span>
    </footer>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { dashboardApi } from '@/api/dashboard'

const router = useRouter()

const screens = [
  { key: 'andon', label: '① Andon 呼叫看板' },
  { key: 'production', label: '② 产量与良率' },
  { key: 'quality', label: '③ 质量与设备' },
]

const ROTATE_MS = 10000
const REFRESH_MS = 30000

const overview = ref(null)
const activeIndex = ref(0)
const clock = ref('')
const isFullscreen = ref(false)
const today = new Date().toLocaleDateString('zh-CN')

let rotateTimer = null
let refreshTimer = null
let clockTimer = null

const currentScreen = computed(() => screens[activeIndex.value].key)

const andonTypeMap = { 0: '设备故障', 1: '质量异常', 2: '缺料', 3: '其它' }

const yieldColor = computed(() => {
  const value = Number(overview.value?.production.yieldRate ?? 0)
  if (value >= 98) return '#67c23a'
  if (value >= 95) return '#e6a23c'
  return '#f56c6c'
})

const fpyClass = computed(() => {
  const value = Number(overview.value?.quality.fpy ?? 0)
  if (value >= 98) return 'ok'
  if (value >= 95) return 'warn'
  return 'danger'
})

const finishRate = computed(() => {
  const production = overview.value?.production
  if (!production || !production.total) return '0.00'
  return ((production.completed / production.total) * 100).toFixed(2)
})

const equipmentCards = computed(() => {
  const equipment = overview.value?.equipment
  return [
    { label: '运行', value: equipment?.running ?? 0, color: '#67c23a' },
    { label: '待机', value: equipment?.idle ?? 0, color: '#909399' },
    { label: '故障', value: equipment?.down ?? 0, color: '#f56c6c' },
    { label: '保养', value: equipment?.maintenance ?? 0, color: '#e6a23c' },
    { label: '离线', value: equipment?.offline ?? 0, color: '#606266' },
  ]
})

function andonTypeLabel(type) {
  return andonTypeMap[type] ?? '-'
}

function andonStatusLabel(call) {
  if (call.escalated) return '已升级'
  if (call.isTimeout) return '超时未响应'
  return call.status === 0 ? '待响应' : '处理中'
}

function downtimeWidth(count) {
  const max = Math.max(...(overview.value?.downtimeTop || []).map((item) => item.count), 1)
  return `${Math.round((count / max) * 100)}%`
}

function tickClock() {
  const now = new Date()
  clock.value = now.toLocaleTimeString('zh-CN', { hour12: false })
}

async function refresh() {
  try {
    overview.value = await dashboardApi.overview()
  } catch {
    ElMessage.error('大屏数据加载失败，请检查网络或登录状态')
  }
}

function goTo(index) {
  activeIndex.value = index
  restartRotate()
}

function restartRotate() {
  if (rotateTimer) clearInterval(rotateTimer)
  rotateTimer = setInterval(() => {
    activeIndex.value = (activeIndex.value + 1) % screens.length
  }, ROTATE_MS)
}

async function toggleFullscreen() {
  if (!document.fullscreenElement) {
    await document.documentElement.requestFullscreen()
    isFullscreen.value = true
  } else {
    await document.exitFullscreen()
    isFullscreen.value = false
  }
}

/** 浏览器原生全屏也支持 Esc 退出，这里同步状态，避免按钮文案与实际不一致 */
function onFullscreenChange() {
  isFullscreen.value = Boolean(document.fullscreenElement)
}

/** 退出大屏：先退出全屏，再回到工单管理 */
async function exitDisplay() {
  if (document.fullscreenElement) {
    await document.exitFullscreen().catch(() => {})
  }
  router.push({ name: 'work-orders' })
}

/**
 * Esc 退出大屏。
 * 若当前处于全屏，第一次 Esc 交给浏览器退全屏（不跳路由），再按一次才返回 —— 与用户直觉一致。
 */
function onKeydown(event) {
  if (event.key !== 'Escape') return
  if (document.fullscreenElement) return
  exitDisplay()
}

onMounted(() => {
  refresh()
  tickClock()
  restartRotate()
  clockTimer = setInterval(tickClock, 1000)
  refreshTimer = setInterval(refresh, REFRESH_MS)
  document.addEventListener('fullscreenchange', onFullscreenChange)
  window.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  clearInterval(rotateTimer)
  clearInterval(refreshTimer)
  clearInterval(clockTimer)
  document.removeEventListener('fullscreenchange', onFullscreenChange)
  window.removeEventListener('keydown', onKeydown)
  // 离开大屏时如果还在全屏，顺手退掉，否则浏览器会一直停在全屏
  if (document.fullscreenElement) {
    document.exitFullscreen().catch(() => {})
  }
})
</script>

<style scoped>
.display {
  min-height: 100vh;
  background: radial-gradient(circle at 20% 0%, #10233d 0%, #050a14 55%, #02040a 100%);
  color: #e6f0ff;
  display: flex;
  flex-direction: column;
  padding: 16px 24px 8px;
  box-sizing: border-box;
}

.display-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 12px;
  border-bottom: 1px solid rgba(64, 158, 255, 0.2);
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 2px;
}

.brand-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: #409eff;
  box-shadow: 0 0 12px #409eff;
}

.clock {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.clock-time {
  font-size: 30px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.clock-date {
  font-size: 13px;
  color: #7f9dc4;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
}

.dots {
  display: flex;
  gap: 8px;
}

.dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.25);
  cursor: pointer;
}

.dot.active {
  background: #409eff;
  box-shadow: 0 0 10px #409eff;
}

.ghost-btn {
  background: transparent;
  border: 1px solid rgba(64, 158, 255, 0.5);
  color: #9fc4ee;
  border-radius: 6px;
  padding: 6px 14px;
  cursor: pointer;
  font-size: 13px;
}

.ghost-btn:hover {
  border-color: #409eff;
  color: #fff;
}

/* 退出口：在深色大屏上要一眼能找到，所以用暖色描边与其它按钮区分 */
.exit-btn {
  border-color: rgba(245, 108, 108, 0.6);
  color: #f8b3b3;
}

.exit-btn:hover {
  border-color: #f56c6c;
  background: rgba(245, 108, 108, 0.14);
  color: #fff;
}

.screen {
  flex: 1;
  padding: 20px 0;
  display: flex;
  gap: 24px;
}

/* ---- Andon ---- */
.andon-screen {
  flex-direction: column;
}

.andon-stats {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}

.stat-card {
  border-radius: 12px;
  padding: 18px;
  text-align: center;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.stat-card.danger .stat-value {
  color: #ff7875;
}

.stat-card.warn .stat-value {
  color: #ffc53d;
}

.stat-card.blur .stat-value {
  color: #7f9dc4;
}

.stat-value {
  font-size: 46px;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
}

.stat-label {
  margin-top: 4px;
  font-size: 14px;
  color: #8fa8c8;
}

.andon-list {
  margin-top: 20px;
  flex: 1;
  overflow: hidden;
}

.list-title {
  font-size: 15px;
  color: #8fa8c8;
  margin-bottom: 10px;
}

.andon-item {
  display: grid;
  grid-template-columns: 18px 200px 120px 160px 1fr 150px;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  margin-bottom: 8px;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.035);
  border-left: 4px solid #e6a23c;
  font-size: 16px;
}

.andon-item.level-red {
  border-left-color: #f56c6c;
  background: rgba(245, 108, 108, 0.08);
}

.andon-light {
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: #e6a23c;
  box-shadow: 0 0 10px #e6a23c;
}

.level-red .andon-light {
  background: #f56c6c;
  box-shadow: 0 0 12px #f56c6c;
}

.blinking {
  animation: blink 1s step-start infinite;
}

@keyframes blink {
  50% {
    opacity: 0.45;
  }
}

.andon-number {
  font-weight: 700;
  color: #cfe3ff;
}

.andon-type {
  color: #9fc4ee;
}

.andon-target {
  color: #9fc4ee;
}

.andon-desc {
  color: #e6f0ff;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.andon-status {
  text-align: right;
  color: #8fa8c8;
}

.andon-status.overtime {
  color: #ff7875;
  font-weight: 700;
}

/* ---- 产量 ---- */
.production-screen {
  align-items: center;
}

.prod-left {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.ring-value {
  font-size: 52px;
  font-weight: 800;
}

.ring-label {
  font-size: 14px;
  color: #8fa8c8;
}

.prod-grid {
  flex: 1.4;
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 18px;
}

.big-stat {
  border-radius: 14px;
  padding: 24px;
  background: rgba(255, 255, 255, 0.035);
  border: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  flex-direction: column;
  justify-content: center;
}

.big-stat.ok .big-value {
  color: #67c23a;
}

.big-stat.warn .big-value {
  color: #e6a23c;
}

.big-stat.danger .big-value {
  color: #f56c6c;
}

.big-value {
  font-size: 58px;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
}

.big-label {
  font-size: 15px;
  color: #8fa8c8;
}

/* ---- 质量与设备 ---- */
.quality-screen {
  flex-direction: row;
}

.quality-col,
.equipment-col {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.col-title {
  font-size: 15px;
  color: #8fa8c8;
  margin-bottom: 12px;
}

.col-title.mt {
  margin-top: 20px;
}

.fpy {
  text-align: center;
  padding: 18px 0;
}

.fpy-value {
  font-size: 76px;
  font-weight: 900;
  font-variant-numeric: tabular-nums;
}

.fpy-value.ok {
  color: #67c23a;
}

.fpy-value.warn {
  color: #e6a23c;
}

.fpy-value.danger {
  color: #f56c6c;
}

.fpy-label {
  font-size: 14px;
  color: #8fa8c8;
}

.mini-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-top: 12px;
}

.mini {
  background: rgba(255, 255, 255, 0.035);
  border-radius: 10px;
  padding: 14px;
  text-align: center;
}

.mini-value {
  display: block;
  font-size: 30px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.mini-value.ok {
  color: #67c23a;
}

.mini-value.danger {
  color: #f56c6c;
}

.mini-value.warn {
  color: #e6a23c;
}

.mini-label {
  font-size: 13px;
  color: #8fa8c8;
}

.equip-row {
  display: flex;
  gap: 12px;
}

.equip-card {
  flex: 1;
  text-align: center;
  padding: 16px 8px;
  border-radius: 10px;
  border: 1px solid;
  background: rgba(255, 255, 255, 0.03);
}

.equip-value {
  font-size: 34px;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
}

.equip-label {
  font-size: 13px;
  color: #8fa8c8;
}

.downtime-row {
  display: grid;
  grid-template-columns: 140px 1fr 70px;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}

.downtime-code {
  font-size: 14px;
  color: #cfe3ff;
}

.downtime-bar {
  height: 14px;
  border-radius: 7px;
  background: rgba(255, 255, 255, 0.07);
  overflow: hidden;
}

.downtime-fill {
  height: 100%;
  background: linear-gradient(90deg, #f56c6c, #e6a23c);
}

.downtime-count {
  font-size: 14px;
  color: #8fa8c8;
  text-align: right;
}

.empty {
  color: #5f7ba1;
  text-align: center;
  padding: 40px 0;
  font-size: 16px;
}

.empty.small {
  padding: 16px 0;
  font-size: 14px;
}

.display-footer {
  display: flex;
  justify-content: space-between;
  font-size: 13px;
  color: #5f7ba1;
  border-top: 1px solid rgba(64, 158, 255, 0.15);
  padding-top: 10px;
}
</style>
