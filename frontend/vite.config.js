import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// Vite 配置：开发服务器代理到后端 API
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    host: true,
    proxy: {
      // API 请求代理到后端
      '/api': {
        target: 'http://localhost:5100',
        changeOrigin: true,
      },
      // SignalR WebSocket 代理
      '/hubs': {
        target: 'http://localhost:5100',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
