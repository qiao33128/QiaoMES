<template>
  <div class="quality-page">
    <el-tabs v-model="activeTab">
      <el-tab-pane label="检验单" name="inspections">
        <InspectionPanel v-if="activeTab === 'inspections'" />
      </el-tab-pane>
      <el-tab-pane label="不合格处置" name="nonconformances">
        <NonconformancePanel v-if="activeTab === 'nonconformances'" />
      </el-tab-pane>
      <el-tab-pane label="来料批次" name="material-lots">
        <MaterialLotPanel v-if="activeTab === 'material-lots'" />
      </el-tab-pane>
      <el-tab-pane label="不良代码" name="defect-codes">
        <DefectCodePanel v-if="activeTab === 'defect-codes'" />
      </el-tab-pane>
      <el-tab-pane label="SPC 控制图" name="spc">
        <SpcPanel v-if="activeTab === 'spc'" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import InspectionPanel from './InspectionPanel.vue'
import NonconformancePanel from './NonconformancePanel.vue'
import MaterialLotPanel from './MaterialLotPanel.vue'
import DefectCodePanel from './DefectCodePanel.vue'
import SpcPanel from './SpcPanel.vue'

const activeTab = ref('inspections')
</script>

<style scoped>
.quality-page {
  display: flex;
  flex-direction: column;
}
</style>
