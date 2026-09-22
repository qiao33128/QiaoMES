# ---------- 构建阶段：Node 构建 Vue 静态文件 ----------
# 🔴 这里的 Node 版本必须与 .github/workflows/ci.yml 的 node-version 保持一致（当前都是 20）。
# 两处不一致会出现「CI 构建通过、镜像里构建失败」（或反之）这类最难查的问题。
FROM node:20-alpine AS build
WORKDIR /app

# 复制依赖清单并安装
# 用 npm ci（而不是 npm install）：严格按 package-lock.json 还原，
# 保证 CI 构建出的镜像与本地/测试环境依赖版本完全一致。
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund

# 复制源码并构建
COPY frontend/ ./
RUN npm run build

# ---------- 运行阶段：Nginx 托管静态文件 ----------
FROM nginx:alpine
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

# 🔴 删掉这个 entrypoint 脚本。
# 实测（2026-09-20 生产事故）：它会在容器里**卡在 `apk manifest nginx`** 上（进程还在、CPU 0:00、阻塞在 I/O），
# nginx 因此永远不会被启动 —— 表现是**容器 Up、端口却无人应答、公网 502**，
# 而日志只停在 "Getting the checksum of /etc/nginx/conf.d/default.conf" 这一行，极具误导性；
# CI 的 web 冒烟检查也只会给出一句失败（当时被我误判成"假报警"，实际是真故障）。
# 该脚本唯一的作用是让 nginx 额外监听 IPv6；本部署对外是网关侧反代 443，不需要它，删掉即可（确定性修复，不依赖运行时挂载）。
RUN rm -f /docker-entrypoint.d/10-listen-on-ipv6-by-default.sh

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
