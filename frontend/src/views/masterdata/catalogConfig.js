import {
  materialApi,
  MaterialTypeMap,
  operationApi,
  productApi,
  workCenterApi,
  WorkCenterTypeMap,
} from '@/api/masterdata'

const commonColumns = [
  { prop: 'code', label: '编码', width: 150 },
  { prop: 'name', label: '名称', minWidth: 180 },
]

/**
 * 主数据配置：四种主数据共用一套「列表 + 表单」渲染逻辑，差异只在列与字段定义。
 * <p>formFields 中 createOnly 的字段（编码）在编辑时隐藏，因为编码是业务唯一键、不可修改。</p>
 */
export const catalogConfigs = {
  products: {
    title: '产品',
    api: productApi,
    columns: [
      ...commonColumns,
      { prop: 'spec', label: '规格', width: 140 },
      { prop: 'unit', label: '单位', width: 80 },
      { prop: 'isActive', label: '状态', width: 90, type: 'bool' },
    ],
    formFields: [
      { prop: 'code', label: '编码', createOnly: true, required: true },
      { prop: 'name', label: '名称', required: true },
      { prop: 'spec', label: '规格' },
      { prop: 'unit', label: '单位' },
      { prop: 'remark', label: '备注' },
    ],
    createDefaults: () => ({ code: '', name: '', spec: '', unit: '', remark: '' }),
  },

  materials: {
    title: '物料',
    api: materialApi,
    columns: [
      ...commonColumns,
      { prop: 'materialType', label: '类型', width: 100, type: 'enum', options: MaterialTypeMap },
      { prop: 'supplierPartNumber', label: '供应商料号', width: 140 },
      { prop: 'spec', label: '规格', width: 120 },
      { prop: 'unit', label: '单位', width: 80 },
      { prop: 'isActive', label: '状态', width: 90, type: 'bool' },
    ],
    formFields: [
      { prop: 'code', label: '编码', createOnly: true, required: true },
      { prop: 'name', label: '名称', required: true },
      { prop: 'materialType', label: '物料类型', type: 'enum', options: MaterialTypeMap },
      { prop: 'supplierPartNumber', label: '供应商料号' },
      { prop: 'spec', label: '规格' },
      { prop: 'unit', label: '单位' },
      { prop: 'remark', label: '备注' },
    ],
    createDefaults: () => ({
      code: '', name: '', materialType: 0, supplierPartNumber: '', spec: '', unit: '', remark: '',
    }),
  },

  operations: {
    title: '工序',
    api: operationApi,
    columns: [
      ...commonColumns,
      { prop: 'standardSeconds', label: '标准工时(秒)', width: 120 },
      { prop: 'isKeyOperation', label: '关键工序', width: 100, type: 'bool', trueText: '是', falseText: '否' },
      { prop: 'isActive', label: '状态', width: 90, type: 'bool' },
    ],
    formFields: [
      { prop: 'code', label: '编码', createOnly: true, required: true },
      { prop: 'name', label: '名称', required: true },
      { prop: 'standardSeconds', label: '标准工时(秒)', type: 'number' },
      { prop: 'isKeyOperation', label: '关键工序', type: 'switch' },
      { prop: 'defaultWorkCenterId', label: '默认工作中心', type: 'workCenter' },
      { prop: 'remark', label: '备注' },
    ],
    createDefaults: () => ({
      code: '', name: '', standardSeconds: 0, isKeyOperation: false, defaultWorkCenterId: null, remark: '',
    }),
  },

  'work-centers': {
    title: '工作中心',
    api: workCenterApi,
    columns: [
      ...commonColumns,
      { prop: 'type', label: '类型', width: 100, type: 'enum', options: WorkCenterTypeMap },
      { prop: 'workshop', label: '车间/区域', width: 140 },
      { prop: 'isActive', label: '状态', width: 90, type: 'bool' },
    ],
    formFields: [
      { prop: 'code', label: '编码', createOnly: true, required: true },
      { prop: 'name', label: '名称', required: true },
      { prop: 'type', label: '类型', type: 'enum', options: WorkCenterTypeMap },
      { prop: 'workshop', label: '车间/区域' },
      { prop: 'remark', label: '备注' },
    ],
    createDefaults: () => ({ code: '', name: '', type: 0, workshop: '', remark: '' }),
  },
}
