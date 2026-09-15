import http from './http'

export const roleApi = {
  list() {
    return http.get('/roles')
  },
  getById(id) {
    return http.get(`/roles/${id}`)
  },
  create(data) {
    return http.post('/roles', data)
  },
  update(id, data) {
    return http.put(`/roles/${id}`, data)
  },
  setPermissions(id, permissions) {
    return http.put(`/roles/${id}/permissions`, { permissions })
  },
  remove(id) {
    return http.delete(`/roles/${id}`)
  },
  // 权限目录（按模块分组）
  permissionCatalog() {
    return http.get('/roles/permissions')
  },
}

export const userApi = {
  list(params) {
    return http.get('/users', { params })
  },
  create(data) {
    return http.post('/users', data)
  },
  setRoles(id, roleIds) {
    return http.put(`/users/${id}/roles`, { roleIds })
  },
  setStatus(id, isActive) {
    return http.put(`/users/${id}/status`, { isActive })
  },
}
