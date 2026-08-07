# ---------- 构建阶段：Node 构建 Vue 静态文件 ----------
FROM node:22-alpine AS build
WORKDIR /app

# 复制依赖清单并安装
COPY frontend/package.json ./
RUN npm install --registry=https://registry.npmjs.org/

# 复制源码并构建
COPY frontend/ ./
RUN npm run build

# ---------- 运行阶段：Nginx 托管静态文件 ----------
FROM nginx:alpine
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
