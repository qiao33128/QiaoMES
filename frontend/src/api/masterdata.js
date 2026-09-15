import http from './http'

/** 主数据接口结构完全一致，按资源名生成一组 CRUD 方法。 */
function createCatalogApi(resource) {
  const base = `/master-data/${resource}`
  return {
    list: (params) => http.get(base, { params }),
    getById: (id) => http.get(`${base}/${id}`),
    create: (data) => http.post(base, data),
    update: (id, data) => http.put(`${base}/${id}`, data),
    setStatus: (id, isActive) => http.put(`${base}/${id}/status`, { isActive }),
  }
}

export const productApi = createCatalogApi('products')
export const materialApi = createCatalogApi('materials')
export const operationApi = createCatalogApi('operations')
export const workCenterApi = createCatalogApi('work-centers')

/** BOM 与工艺路线是「版本化」资源：启用用 activate，删除用 remove，没有简单的启停。 */
function createVersionedApi(resource) {
  const base = `/master-data/${resource}`
  return {
    list: (params) => http.get(base, { params }),
    getById: (id) => http.get(`${base}/${id}`),
    create: (data) => http.post(base, data),
    update: (id, data) => http.put(`${base}/${id}`, data),
    activate: (id) => http.post(`${base}/${id}/activate`),
    remove: (id) => http.delete(`${base}/${id}`),
  }
}

export const bomApi = createVersionedApi('boms')
export const routingApi = createVersionedApi('routings')

/** 导出为 CSV 文件（带 BOM，Excel 可直接打开） */
export function exportCatalogCsv(resource) {
  return http.get(`/master-data/${resource}/export`, { responseType: 'blob' })
}

/** 从 CSV 文本导入（按编码 upsert） */
export function importCatalogCsv(resource, content) {
  return http.post(`/master-data/${resource}/import`, { content })
}

export const MaterialTypeMap = { 0: '原材料', 1: '半成品', 2: '成品', 3: '辅料' }

export const WorkCenterTypeMap = { 0: '产线', 1: '单元', 2: '工位', 3: '设备' }
