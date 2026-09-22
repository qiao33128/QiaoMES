#!/usr/bin/env bash
# ============================================================================
# QiaoMES · 一条命令部署（**幂等**：反复执行结果一致，且绝不会删数据）
#
# 用法（服务器上）：
#   mkdir -p /root/qiaomes && curl -fsSL -o /root/qiaomes/one-shot.sh \
#     https://raw.githubusercontent.com/qiao33128/QiaoMES/main/deploy/one-shot.sh
#   bash /root/qiaomes/one-shot.sh
#
#   或者从已检出的仓库里直接跑：  bash deploy/one-shot.sh
#
# 九个步骤，每步都是「有则复用、无则创建」：
#   1. 环境检查（docker + compose v2 插件 + 守护进程）
#   2. 建目录（默认 /root/qiaomes）
#   3. 就位编排文件（本地有就用本地的，否则从仓库拉；内容相同则不动）
#   4. 就位 .env —— 缺 JWT_SECRET_KEY 自动生成；数据库密码**只在全新库时**生成
#   5. 确保 external 网关网络存在（不存在 compose 会直接拒绝启动）
#   6. 需要时登录镜像仓（给了凭证就登录，否则检查是否登录过）
#   7. pull api / web
#   8. up -d（compose 支持 --wait 就等健康检查）
#   9. 健康检查 + 打印访问地址；失败时自动附带容器日志尾部
#
# 可用环境变量覆盖：
#   QIAOMES_DIR / WEB_PORT / CORS_ORIGINS / GATEWAY_NETWORK / PUBLIC_BASE_URL /
#   REGISTRY / REGISTRY_USERNAME / REGISTRY_PASSWORD / QIAOMES_API_IMAGE /
#   QIAOMES_WEB_IMAGE / HEALTH_TIMEOUT / RAW_BASE / INSTANCE_NAME
#
# 🔴 与 CI 的关系：日常发布请 push main 走 CI（.github/workflows/deploy.yml）。
#    本脚本是**应急 / 换机器 / 没有 CI** 时的通道；两者共用同一份编排文件与 .env 约定，
#    都只做 pull / up，**都不执行任何数据库脚本**（迁移由 API 启动时自动应用），所以不会打架。
# ============================================================================

set -Eeuo pipefail

# ---------------------------------------------------------------- 参数
QIAOMES_DIR="${QIAOMES_DIR:-/root/qiaomes}"
COMPOSE_NAME="docker-compose.deploy.yml"
ENV_FILE=".env"
GATEWAY_NETWORK="${GATEWAY_NETWORK:-kanban_net}"
WEB_PORT="${WEB_PORT:-8090}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"
RAW_BASE="${RAW_BASE:-https://raw.githubusercontent.com/qiao33128/QiaoMES/main}"
REGISTRY="${REGISTRY:-ccr.ccs.tencentyun.com}"
PUBLIC_BASE_URL="${PUBLIC_BASE_URL:-}"
STAMP="$(date +%Y%m%d-%H%M%S)"

if [ -t 1 ]; then
  blue=$'\033[1;34m'; green=$'\033[1;32m'; yellow=$'\033[1;33m'; red=$'\033[1;31m'; off=$'\033[0m'
else
  blue=''; green=''; yellow=''; red=''; off=''   # 非交互（云助手 / CI 日志）不打转义码
fi

CURRENT_STEP="启动"
# 失败时除了原命令自己的报错，再补一句「这一步失败通常是因为什么」——
# 本脚本常通过云助手的 RunCommand 执行，用户只看得到这段输出，没有交互式排查的机会。
on_error() {
  printf '\n%s❌ 在第「%s」步失败 —— 上面的输出就是原因%s\n' "$red" "$CURRENT_STEP" "$off" >&2
  case "$CURRENT_STEP" in
    5/*) printf '   常见原因：没有创建网络的权限，或 docker 守护进程异常（可手工执行 docker network create %s 确认）\n' "$GATEWAY_NETWORK" >&2 ;;
    6/*) printf '   常见原因：镜像仓用户名/密码错（TCR 个人版要用「访问凭证」里的密码，不是登录密码）\n' >&2 ;;
    7/*) printf '   常见原因：镜像仓未登录、镜像 tag 不存在、或服务器拉不到镜像仓（跨境/网络）\n' >&2 ;;
    8/*) printf '   常见原因：JWT_SECRET_KEY 没配（编排文件里是必填）、端口被占、或内存不足被 OOM Kill\n' >&2 ;;
    *)   : ;;
  esac
  exit 1
}
trap 'on_error' ERR

step() { printf '\n%s=== [%s] %s%s\n' "$blue" "$1" "$2" "$off"; }
info() { printf '    %s\n' "$*"; }
ok()   { printf '%s    ✓ %s%s\n' "$green" "$*" "$off"; }
warn() { printf '%s    ⚠️  %s%s\n' "$yellow" "$*" "$off" >&2; }
die()  { printf '\n%s❌ %s%s\n' "$red" "$*" "$off" >&2; exit 1; }

# ---------------------------------------------------------------- 工具函数

random_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 32
  else
    head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n'
  fi
}

# .env 里某个键是否有非空值
env_has() {
  [ -f "$ENV_FILE" ] || return 1
  grep -qE "^$1=.+" "$ENV_FILE"
}

env_get() {
  [ -f "$ENV_FILE" ] || return 0
  grep -E "^$1=" "$ENV_FILE" | tail -1 | cut -d= -f2- || true
}

# 幂等写入：先删掉同名键再追加，避免 .env 里出现两行同名变量
env_set() {
  local key="$1" value="$2" tmp
  tmp="$(mktemp)"
  if [ -f "$ENV_FILE" ]; then
    grep -vE "^${key}=" "$ENV_FILE" > "$tmp" || true
  fi
  cat "$tmp" > "$ENV_FILE"
  rm -f "$tmp"
  printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
}

# compose 的卷名带项目前缀（/root/qiaomes → qiaomes_qiaomes_pgdata），
# 所以**按后缀找**，别把卷名写死。
find_pgdata_volume() {
  docker volume ls --format '{{.Name}}' 2>/dev/null | grep -E 'qiaomes_pgdata$' | head -1 || true
}

compose_supports_wait() {
  # 先把输出抓下来再匹配：直接 `docker compose up --help | grep -q` 有坑 ——
  # grep 命中即退出，写端会吃到 SIGPIPE，叠加 pipefail 后整条管道被判为失败，
  # 于是「支持 --wait 的版本」被误判成不支持。
  local help
  help="$(docker compose up --help 2>/dev/null || true)"
  [ -n "$help" ] || return 1
  grep -q -- '--wait-timeout' <<<"$help"
}

# ================================================================ 1
CURRENT_STEP="1/9 环境检查"
step "1/9" "环境检查"
command -v docker >/dev/null 2>&1 || die "没有 docker —— 请先安装 Docker Engine 与 compose 插件"
docker info >/dev/null 2>&1 || die "docker 守护进程不可用（试试 systemctl start docker）"
docker compose version >/dev/null 2>&1 \
  || die "docker compose（v2 插件）不可用 —— 本脚本用的是 'docker compose'，不是老的 'docker-compose'"
ok "docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?') / compose $(docker compose version --short 2>/dev/null || echo '?')"

# ================================================================ 2
CURRENT_STEP="2/9 准备目录"
step "2/9" "准备目录 ${QIAOMES_DIR}"
mkdir -p "$QIAOMES_DIR"
[ -w "$QIAOMES_DIR" ] || die "目录不可写：$QIAOMES_DIR（是不是该加 sudo？）"

# 「改进建议 → 迭代服务配置」保存的文件落在这里（编排把它挂到容器的 /app/config）。
# 容器以非 root 用户（UID 1654）运行，所以宿主目录要让它能写：
# chown 失败（没有 root / 用户不存在）就退化成 777 —— $QIAOMES_DIR 通常在 /root 下，
# 而 /root 只有 root 能进入，所以 777 并不会真的把它暴露给别的用户。
mkdir -p "$QIAOMES_DIR/config"
chown 1654:1654 "$QIAOMES_DIR/config" 2>/dev/null || chmod 777 "$QIAOMES_DIR/config"

ok "目录就绪（含 config/：迭代服务配置落盘在这里）"

# ================================================================ 3
CURRENT_STEP="3/9 就位编排文件"
step "3/9" "就位编排文件"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_COMPOSE=""
if [ -f "${SCRIPT_DIR}/../${COMPOSE_NAME}" ]; then
  SOURCE_COMPOSE="${SCRIPT_DIR}/../${COMPOSE_NAME}"       # 从仓库里跑：用当次检出的那份
  info "来源：仓库（${SOURCE_COMPOSE}）"
elif [ -f "${SCRIPT_DIR}/${COMPOSE_NAME}" ]; then
  SOURCE_COMPOSE="${SCRIPT_DIR}/${COMPOSE_NAME}"
  info "来源：脚本同目录"
else
  info "来源：从仓库下载（${RAW_BASE}/${COMPOSE_NAME}）"
  SOURCE_COMPOSE="$(mktemp)"
  curl -fsSL --retry 3 --retry-delay 3 -o "$SOURCE_COMPOSE" "${RAW_BASE}/${COMPOSE_NAME}" \
    || die "下载编排文件失败：${RAW_BASE}/${COMPOSE_NAME}"
fi
grep -q '^services:' "$SOURCE_COMPOSE" || die "编排文件不合法（没有 services: 段）：${SOURCE_COMPOSE}"

mkdir -p "$QIAOMES_DIR"
if [ -f "${QIAOMES_DIR}/${COMPOSE_NAME}" ] && command -v cmp >/dev/null 2>&1 \
   && cmp -s "$SOURCE_COMPOSE" "${QIAOMES_DIR}/${COMPOSE_NAME}"; then
  ok "与现有一致，无需更新"
else
  # 先写临时文件再 mv：避免半截文件被别的进程读到
  cp -f "$SOURCE_COMPOSE" "${QIAOMES_DIR}/${COMPOSE_NAME}.new.${STAMP}"
  mv -f "${QIAOMES_DIR}/${COMPOSE_NAME}.new.${STAMP}" "${QIAOMES_DIR}/${COMPOSE_NAME}"
  ok "已写入 ${QIAOMES_DIR}/${COMPOSE_NAME}"
fi

cd "$QIAOMES_DIR"
COMPOSE_CMD=(docker compose -f "$COMPOSE_NAME" --env-file "$ENV_FILE")

# ================================================================ 4
CURRENT_STEP="4/9 就位 .env"
step "4/9" "就位 .env"
GENERATED=""

if [ ! -f "$ENV_FILE" ]; then
  info "没有 .env，生成一份最小可用的（完整变量说明见仓库根 .env.example）"
  cat > "$ENV_FILE" <<ENVEOF
# 由 deploy/one-shot.sh 生成于 ${STAMP}
# 完整可复制模板见仓库根 .env.example（其中 ASSISTANT_* / ITERATION_* 等可选功能按需追加）
# 🔴 本文件含密钥，已 chmod 600：**请备份**。丢了 POSTGRES_PASSWORD 就要去改库里的密码，而不是改这里。
QIAOMES_API_IMAGE=${QIAOMES_API_IMAGE:-${REGISTRY}/qiaoqiao11/qiaomes-api:latest}
QIAOMES_WEB_IMAGE=${QIAOMES_WEB_IMAGE:-${REGISTRY}/qiaoqiao11/qiaomes-web:latest}
ENVEOF
fi
chmod 600 "$ENV_FILE"

# 🔴 JWT 与数据库密码的处置方式**刻意不同**：
#   JWT 可以随便换（代价是所有人重新登录）；数据库密码与数据卷绑死，换 = 连不上库。
if ! env_has JWT_SECRET_KEY; then
  env_set JWT_SECRET_KEY "$(random_secret)"
  GENERATED="${GENERATED}JWT_SECRET_KEY "
  ok "已生成 JWT_SECRET_KEY（随机 64 位十六进制）"
fi

PG_VOLUME="$(find_pgdata_volume)"
if ! env_has POSTGRES_PASSWORD; then
  if [ -n "$PG_VOLUME" ]; then
    warn "数据卷 ${PG_VOLUME} 已存在，但 .env 里没有 POSTGRES_PASSWORD"
    warn "→ 将沿用编排文件的默认值 qiaomes_dev。若这个卷当初不是用它初始化的，API 会连不上库。"
    warn "→ 这里**不会**凭空生成新密码：那必然连不上已初始化的库。密码确实丢了，请去改库里的密码。"
  else
    env_set POSTGRES_PASSWORD "$(random_secret)"
    GENERATED="${GENERATED}POSTGRES_PASSWORD "
    ok "全新数据库：已生成 POSTGRES_PASSWORD（首次初始化写进数据卷，之后别再改）"
  fi
fi

# 这两个必须落进 .env：编排文件里的默认值与脚本的默认值不同（8080 vs 8090），
# 不写死就会出现「脚本按 8090 探活、容器其实在 8080」的假故障。
env_has WEB_PORT || env_set WEB_PORT "$WEB_PORT"
env_has GATEWAY_NETWORK || env_set GATEWAY_NETWORK "$GATEWAY_NETWORK"
WEB_PORT="$(env_get WEB_PORT)"; WEB_PORT="${WEB_PORT:-8080}"
GATEWAY_NETWORK="$(env_get GATEWAY_NETWORK)"; GATEWAY_NETWORK="${GATEWAY_NETWORK:-kanban_net}"

if [ -n "$PUBLIC_BASE_URL" ]; then
  env_has CORS_ORIGINS || env_set CORS_ORIGINS "${PUBLIC_BASE_URL},http://localhost:${WEB_PORT}"
fi

ok ".env 就绪（WEB_PORT=${WEB_PORT}，GATEWAY_NETWORK=${GATEWAY_NETWORK}）"
if [ -n "$GENERATED" ]; then
  info "本次新生成的键：${GENERATED}"
fi

# ================================================================ 5
CURRENT_STEP="5/9 网关网络"
step "5/9" "确保网关网络 ${GATEWAY_NETWORK} 存在"
if docker network inspect "$GATEWAY_NETWORK" >/dev/null 2>&1; then
  ok "已存在"
else
  docker network create "$GATEWAY_NETWORK" >/dev/null
  ok "已创建（编排文件把它声明成 external，不存在会让 compose 直接拒绝启动）"
fi

# ================================================================ 6
CURRENT_STEP="6/9 镜像仓登录"
step "6/9" "镜像仓登录（${REGISTRY}）"
if [ -n "${REGISTRY_USERNAME:-}" ] && [ -n "${REGISTRY_PASSWORD:-}" ]; then
  # 密码走 stdin，不落到命令历史 / ps 输出里
  printf '%s' "$REGISTRY_PASSWORD" | docker login "$REGISTRY" -u "$REGISTRY_USERNAME" --password-stdin >/dev/null \
    || die "docker login 失败（用户名/密码错？TCR 个人版要用「访问凭证」的密码）"
  ok "已登录"
elif grep -qs "$REGISTRY" "${HOME}/.docker/config.json" 2>/dev/null; then
  ok "已登录过，跳过"
else
  warn "未提供 REGISTRY_USERNAME / REGISTRY_PASSWORD，也没找到已登录记录"
  warn "若镜像是私有的，下一步 pull 会失败 —— 加上 REGISTRY_USERNAME=... REGISTRY_PASSWORD=... 再跑一次即可"
fi

# ================================================================ 7
CURRENT_STEP="7/9 拉取镜像"
step "7/9" "拉取 api / web 镜像"
"${COMPOSE_CMD[@]}" pull api web
ok "镜像就绪"

# ================================================================ 8
CURRENT_STEP="8/9 启动"
step "8/9" "启动容器"
# --force-recreate 必需：镜像 tag 是 :latest，光看配置哈希 compose 不会发现镜像换了，
# 结果就是「部署成功但跑的还是旧版本」。
if compose_supports_wait; then
  info "使用 --wait 等待健康检查（最多 ${HEALTH_TIMEOUT}s）"
  "${COMPOSE_CMD[@]}" up -d --force-recreate --wait --wait-timeout "$HEALTH_TIMEOUT" api web
else
  warn "当前 compose 版本不支持 --wait，改为启动后自己轮询（见下一步）"
  "${COMPOSE_CMD[@]}" up -d --force-recreate api web
fi
ok "容器已启动"

# ================================================================ 9
CURRENT_STEP="9/9 健康检查"
step "9/9" "健康检查"

# API 不发布宿主机端口，且 aspnet 运行时镜像里没有 curl/wget（进容器也探不了），
# 所以在宿主机上取它的容器 IP 直接打 8080。容器重建后 IP 会变，故每次都重新取。
ROUNDS=$(( HEALTH_TIMEOUT / 3 )); [ "$ROUNDS" -gt 0 ] || ROUNDS=1
API_IP=""
for i in $(seq 1 "$ROUNDS"); do
  API_IP="$(docker inspect qiaomes-api \
      --format '{{range .NetworkSettings.Networks}}{{println .IPAddress}}{{end}}' 2>/dev/null | head -1 || true)"
  if [ -n "$API_IP" ] && curl -fsS -o /dev/null -m 5 "http://${API_IP}:8080/health/ready"; then
    info "API /health/ready 通过（第 ${i} 次，容器 IP ${API_IP}）"
    break
  fi
  API_IP=""
  sleep 3
done
if [ -z "$API_IP" ]; then
  printf '\n--- qiaomes-api 日志尾部 ---\n' >&2
  docker logs --tail 40 qiaomes-api >&2 2>&1 || true
  die "API 未就绪（/health/ready 探不通）。常见原因：JWT_SECRET_KEY 缺失、POSTGRES_PASSWORD 与数据卷不一致、迁移失败。"
fi

WEB_CODE="$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://127.0.0.1:${WEB_PORT}/" || echo 000)"
if [ "$WEB_CODE" != "200" ]; then
  printf '\n--- qiaomes-web 日志尾部 ---\n' >&2
  docker logs --tail 30 qiaomes-web >&2 2>&1 || true
  die "前端端口 ${WEB_PORT} 探不通（HTTP ${WEB_CODE}）"
fi
ok "前端端口 ${WEB_PORT} 通过"

# 清掉 7 天前的悬空镜像，避免小机器磁盘被历史镜像吃满
docker image prune -f --filter "until=168h" >/dev/null 2>&1 || true

# 配了公网地址就打一次真实请求：DNS / TLS / Caddy / 应用任一层断了都能暴露
if [ -n "$PUBLIC_BASE_URL" ]; then
  CODE="$(curl -s -o /dev/null -w '%{http_code}' -m 10 "${PUBLIC_BASE_URL%/}/" || echo 000)"
  if [ "$CODE" = "200" ]; then ok "公网入口 ${PUBLIC_BASE_URL} 通过"
  else warn "公网入口 ${PUBLIC_BASE_URL} 返回 ${CODE}（容器本身是好的，检查网关/DNS/TLS）"; fi
fi

# ================================================================ 汇总
HOST_IP="$(hostname -I 2>/dev/null | awk '{print $1}' || true)"
printf '\n%s✅ 部署完成%s\n' "$green" "$off"
printf '   前端：   http://%s:%s\n' "${HOST_IP:-<服务器IP>}" "$WEB_PORT"
if [ -n "$PUBLIC_BASE_URL" ]; then
  printf '   域名：   %s\n' "$PUBLIC_BASE_URL"
fi
printf '   容器：   cd %s && docker compose -f %s ps\n' "$QIAOMES_DIR" "$COMPOSE_NAME"
printf '   日志：   docker logs -f qiaomes-api\n'
printf '   回滚：   在 .env 里把 QIAOMES_*_IMAGE 改回上一次的地址再跑本脚本（历史地址见 .last_api_image / .last_web_image）\n'
# 🔴 这里用 if 而不是 `[ -n ... ] && printf`：后者在条件不成立时整条语句返回 1，
# 叠加 set -e 会判脚本「失败」—— 于是明明部署成功却以非 0 退出（CI / 云助手会当成失败）。
if [ -n "$GENERATED" ]; then
  printf '\n%s⚠️  本次新生成：%s→ 已写入 %s/%s（chmod 600），请备份该文件。%s\n' \
    "$yellow" "$GENERATED" "$QIAOMES_DIR" "$ENV_FILE" "$off"
fi
printf '\n%s本脚本幂等：再跑一次同样安全，不会动数据。日常发布请 push main 走 CI。%s\n' "$blue" "$off"
