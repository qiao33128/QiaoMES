<template>
  <el-dialog
    :model-value="modelValue"
    :title="routingId ? '编辑工艺路线' : '新建工艺路线'"
    width="880px"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <el-form :model="form" label-width="70px">
      <el-form-item label="产品" required>
        <el-select
          v-model="form.productId"
          filterable
          :disabled="!!routingId"
          placeholder="选择产品"
          style="width: 100%"
        >
          <el-option
            v-for="p in products"
            :key="p.id"
            :label="`${p.code} ${p.name}`"
            :value="p.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="版本" required>
        <el-input v-model="form.version" :disabled="!!routingId" placeholder="如 V1.0" style="width: 220px" />
      </el-form-item>
      <el-form-item label="备注">
        <el-input v-model="form.remark" />
      </el-form-item>
    </el-form>

    <div class="steps-header">
      <span>工序步骤（{{ form.steps.length }}）· 顺序由小到大执行</span>
      <el-button size="small" type="primary" :icon="Plus" @click="addStep">添加工序</el-button>
    </div>

    <el-table :data="form.steps" size="small" border max-height="340">
      <el-table-column label="顺序" width="90">
        <template #default="{ row }">
          <el-input v-model.number="row.sequence" type="number" />
        </template>
      </el-table-column>
      <el-table-column label="工序" min-width="200">
        <template #default="{ row }">
          <el-select v-model="row.operationId" filterable placeholder="选择工序" style="width: 100%">
            <el-option
              v-for="op in operations"
              :key="op.id"
              :label="`${op.code} ${op.name}`"
              :value="op.id"
            />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="工作中心" min-width="180">
        <template #default="{ row }">
          <el-select v-model="row.workCenterId" filterable clearable placeholder="可留空" style="width: 100%">
            <el-option
              v-for="wc in workCenters"
              :key="wc.id"
              :label="`${wc.code} ${wc.name}`"
              :value="wc.id"
            />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="标准工时(秒)" width="130">
        <template #default="{ row }">
          <el-input v-model.number="row.standardSeconds" type="number" />
        </template>
      </el-table-column>
      <el-table-column label="质检点" width="90" align="center">
        <template #default="{ row }">
          <el-switch v-model="row.isQualityGate" />
        </template>
      </el-table-column>
      <el-table-column label="操作" width="70" align="center">
        <template #default="{ $index }">
          <el-button link type="danger" @click="form.steps.splice($index, 1)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <template #footer>
      <el-button @click="$emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="saving" @click="submit">保存</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { operationApi, productApi, routingApi, workCenterApi } from '@/api/masterdata'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  routingId: { type: String, default: null },
})
const emit = defineEmits(['update:modelValue', 'saved'])

const products = ref([])
const operations = ref([])
const workCenters = ref([])
const saving = ref(false)
const form = reactive({ productId: null, version: '', remark: '', steps: [] })

async function loadOptions() {
  if (!products.value.length) {
    products.value = (await productApi.list({ page: 1, pageSize: 100, isActive: true })).items
  }
  if (!operations.value.length) {
    operations.value = (await operationApi.list({ page: 1, pageSize: 100, isActive: true })).items
  }
  if (!workCenters.value.length) {
    workCenters.value = (await workCenterApi.list({ page: 1, pageSize: 100, isActive: true })).items
  }
}

function addStep() {
  form.steps.push({
    sequence: (form.steps.length + 1) * 10,
    operationId: null,
    workCenterId: null,
    standardSeconds: 0,
    isQualityGate: false,
  })
}

async function loadDetail() {
  const detail = await routingApi.getById(props.routingId)
  form.productId = detail.productId
  form.version = detail.version
  form.remark = detail.remark || ''
  form.steps = detail.steps.map((step) => ({
    sequence: step.sequence,
    operationId: step.operationId,
    workCenterId: step.workCenterId,
    standardSeconds: step.standardSeconds,
    isQualityGate: step.isQualityGate,
  }))
}

watch(
  () => props.modelValue,
  async (visible) => {
    if (!visible) return

    await loadOptions()

    if (props.routingId) {
      await loadDetail()
    } else {
      form.productId = null
      form.version = ''
      form.remark = ''
      form.steps = []
      addStep()
    }
  },
)

async function submit() {
  if (!form.productId) {
    ElMessage.warning('请选择产品')
    return
  }
  if (!form.version.trim()) {
    ElMessage.warning('版本号不能为空')
    return
  }
  if (!form.steps.length) {
    ElMessage.warning('至少需要一个工序')
    return
  }
  if (form.steps.some((step) => !step.operationId)) {
    ElMessage.warning('每个步骤都需要选择工序')
    return
  }
  if (form.steps.some((step) => Number(step.standardSeconds) < 0)) {
    ElMessage.warning('标准工时不能为负数')
    return
  }

  const steps = form.steps.map((step) => ({
    sequence: Number(step.sequence) || 0,
    operationId: step.operationId,
    workCenterId: step.workCenterId || null,
    standardSeconds: Number(step.standardSeconds) || 0,
    isQualityGate: !!step.isQualityGate,
  }))

  saving.value = true
  try {
    if (props.routingId) {
      await routingApi.update(props.routingId, { remark: form.remark, steps })
    } else {
      await routingApi.create({
        productId: form.productId,
        version: form.version,
        remark: form.remark,
        steps,
      })
    }
    ElMessage.success('保存成功')
    emit('update:modelValue', false)
    emit('saved')
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.steps-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 8px 0;
  font-weight: 600;
  color: #303133;
}
</style>
