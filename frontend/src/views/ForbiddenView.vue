<template>
  <el-result icon="warning" title="没有访问权限" sub-title="当前账号缺少所需权限，请联系管理员分配角色">
    <template #extra>
      <el-button type="primary" @click="router.push('/')">返回首页</el-button>
    </template>
  </el-result>
</template>

<script setup>
import { useRouter } from 'vue-router'

const router = useRouter()
</script>
