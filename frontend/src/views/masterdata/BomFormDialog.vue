<template>
  <el-dialog
    :model-value="modelValue"
    :title="bomId ? '编辑 BOM' : '新建 BOM'"
    width="840px"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <el-form :model="form" label-width="70px">
      <el-form-item label="产品" required>
        <el-select
          v-model="form.productId"
          filterable
          :disabled="!!bomId"
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
        <el-input v-model="form.version" :disabled="!!bomId" placeholder="如 V1.0" style="width: 220px" />
      </el-form-item>
      <el-form-item label="备注">
        <el-input v-model="form.remark" />
      </el-form-item>
    </el-form>

    <div class="items-header">
      <span>明细行（{{ form.items.length }}）</span>
      <el-button size="small" type="primary" :icon="Plus" @click="addItem">添加物料</el-button>
    </div>

    <el-table :data="form.items" size="small" border max-height="320">
      <el-table-column label="物料" min-width="240">
        <template #default="{ row }">
          <el-select v-model="row.materialId" filterable placeholder="选择物料" style="width: 100%">
            <el-option
              v-for="m in materials"
              :key="m.id"
              :label="`${m.code} ${m.name}`"
              :value="m.id"
            />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="用量" width="110">
        <template #default="{ row }">
          <el-input v-model.number="row.quantity" type="number" />
        </template>
      </el-table-column>
      <el-table-column label="单位" width="90">
        <template #default="{ row }">
          <el-input v-model="row.unit" />
        </template>
      </el-table-column>
      <el-table-column label="损耗率" width="110">
        <template #default="{ row }">
          <el-input v-model.number="row.lossRate" type="number" />
        </template>
      </el-table-column>
      <el-table-column label="操作" width="70" align="center">
        <template #default="{ $index }">
          <el-button link type="danger" @click="form.items.splice($index, 1)">删除</el-button>
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
import { bomApi, materialApi, productApi } from '@/api/masterdata'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  bomId: { type: String, default: null },
})
const emit = defineEmits(['update:modelValue', 'saved'])

const products = ref([])
const materials = ref([])
const saving = ref(false)
const form = reactive({ productId: null, version: '', remark: '', items: [] })

async function loadOptions() {
  if (!products.value.length) {
    const data = await productApi.list({ page: 1, pageSize: 100, isActive: true })
    products.value = data.items
  }
  if (!materials.value.length) {
    const data = await materialApi.list({ page: 1, pageSize: 100, isActive: true })
    materials.value = data.items
  }
}

function addItem() {
  form.items.push({ materialId: null, quantity: 1, unit: 'PCS', lossRate: 0 })
}

async function loadDetail() {
  const detail = await bomApi.getById(props.bomId)
  form.productId = detail.productId
  form.version = detail.version
  form.remark = detail.remark || ''
  form.items = detail.items.map((item) => ({
    materialId: item.materialId,
    quantity: Number(item.quantity),
    unit: item.unit || '',
    lossRate: Number(item.lossRate),
  }))
}

watch(
  () => props.modelValue,
  async (visible) => {
    if (!visible) return

    await loadOptions()

    if (props.bomId) {
      await loadDetail()
    } else {
      form.productId = null
      form.version = ''
      form.remark = ''
      form.items = []
      addItem()
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
  if (!form.items.length) {
    ElMessage.warning('至少需要一条明细')
    return
  }
  if (form.items.some((item) => !item.materialId || !(Number(item.quantity) > 0))) {
    ElMessage.warning('每条明细都需要选择物料，且用量大于 0')
    return
  }

  const items = form.items.map((item) => ({
    materialId: item.materialId,
    quantity: Number(item.quantity),
    unit: item.unit,
    lossRate: Number(item.lossRate) || 0,
  }))

  saving.value = true
  try {
    if (props.bomId) {
      await bomApi.update(props.bomId, { remark: form.remark, items })
    } else {
      await bomApi.create({
        productId: form.productId,
        version: form.version,
        remark: form.remark,
        items,
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
.items-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 8px 0;
  font-weight: 600;
  color: #303133;
}
</style>
