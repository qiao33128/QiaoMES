#!/usr/bin/env python3
"""QiaoMES · **开发环境**（分支 `dev`）部署脚本。

与生产脚本（`gh_deploy_aliyun.py`）的关系：**只借用它的传输层**（阿里云 RunCommand 的分块上传、
日志轮询、错误处理），真正的部署动作是本文件自己的 `DEV_SERVER_SCRIPT`。
这样两套环境各自演进、互不影响 —— 生产脚本一行都不用动，也就不可能因为"顺手加个开发环境"把生产弄坏。

由 `.github/workflows/deploy-dev.yml` 调用（push `dev` 分支时触发）。

与生产的差异（每条都是刻意的，理由见仓库根 `docker-compose.dev.yml` 头部注释）：
  * 目录 `/root/qiaomes-dev`、编排 `docker-compose.dev.yml`、容器 `qiaomes-dev-*`、对外端口 8092
  * **JWT 与数据库密码在服务器上生成，不与生产共用**
    （共用 JWT 的话，开发环境签发的令牌在生产上是有效的；共用库密码等于把生产口令多存一份）
  * 不搬旧栈、不挂网关网络、不探 ai-iteration
"""

import base64
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))

# 必须在 import 之前设好：base 模块的 load_compose_b64() 读取的是这个环境变量
os.environ.setdefault(
    "COMPOSE_FILE", os.path.normpath(os.path.join(HERE, "..", "docker-compose.dev.yml"))
)

sys.path.insert(0, HERE)
import gh_deploy_aliyun as base  # noqa: E402  （必须在设置 COMPOSE_FILE 之后 import）

# 覆盖 base 模块里绑定到"生产"的常量。
# 这些值都是在函数里**调用时**读取的，所以在这里覆盖有效；生产脚本那边不受影响。
base.DEPLOY_LOG = "/root/qiaomes_dev_deploy.log"
base.REMOTE_SCRIPT = "/root/qiaomes_dev_deploy.sh"
base.REMOTE_B64 = "/root/qiaomes_dev_deploy.b64"
# 开发环境只下发这几个键 —— 绝不把生产那套 .env（JWT / 库密码 / 问数 key）带过来。
base.ENV_KEYS = [
    "QIAOMES_API_IMAGE",
    "QIAOMES_WEB_IMAGE",
    "DEV_WEB_PORT",
    "CORS_ORIGINS",
    "DEMO_DATA_ENABLED",
]


# ---------------------------------------------------------------- 服务器端脚本模板
# 同样用 __TOKEN__ 占位而不是 str.format()：脚本里全是 shell 的 ${VAR} 和 %{http_code}。
DEV_SERVER_SCRIPT = r"""#!/bin/bash
# 本脚本由 deploy/gh_deploy_dev.py 生成，请勿手工修改
DIR=/root/qiaomes-dev
COMPOSE=docker-compose.dev.yml
mkdir -p "$DIR" || { echo DEPLOY-FAIL-MKDIR; exit 1; }
cd "$DIR" || { echo DEPLOY-FAIL-CD; exit 1; }

echo "=== [1/6] 合并写入 .env（只覆盖本次提供的键）+ 生成开发环境专用密钥"
NEW=/tmp/qiaomes_dev.env.new
rm -f "$NEW"
base64 -d > "$NEW" <<'QIAOMES_DEV_ENV_B64'
__ENV_B64__
QIAOMES_DEV_ENV_B64
ENVF="$DIR/.env"
touch "$ENVF"
if [ -s "$NEW" ]; then
  while IFS= read -r line; do
    case "$line" in ""|\#*) continue;; esac
    key="${line%%=*}"
    tmp="$(mktemp)"
    grep -v "^${key}=" "$ENVF" > "$tmp" || true
    mv "$tmp" "$ENVF"
    printf '%s\n' "$line" >> "$ENVF"
  done < "$NEW"
fi

# 🔴 开发环境的密钥**在服务器上生成，绝不复用生产的**：
#   · JWT 共用 → 开发环境签发的令牌在生产上有效，等于把生产登录口开了一半；
#   · 数据库密码共用 → 生产库口令又多存了一份，而开发库根本不需要它。
gen_secret() { head -c 48 /dev/urandom | base64 | tr -d '\n=+/' | cut -c1-48; }
if ! grep -q '^JWT_SECRET_KEY=..*' "$ENVF"; then
  printf 'JWT_SECRET_KEY=%s\n' "$(gen_secret)" >> "$ENVF"
  echo "--- 已生成开发环境专用 JWT_SECRET_KEY（与生产不同）"
fi
if ! grep -q '^POSTGRES_PASSWORD=..*' "$ENVF"; then
  printf 'POSTGRES_PASSWORD=%s\n' "$(gen_secret)" >> "$ENVF"
  echo "--- 已生成开发库密码（只在开发卷**首次初始化**时生效；已初始化过的卷不要改它）"
fi
chmod 600 "$ENVF"
echo "--- .env 生效内容（值隐藏）"
sed 's/=.*/=<hidden>/' "$ENVF"

echo "=== [2/6] 刷新编排文件"
if [ -n "__COMPOSE_B64__" ]; then
  echo "__COMPOSE_B64__" | base64 -d > "$DIR/$COMPOSE.new"
  echo "（编排文件来自 CI 内联，未经网络）"
else
  echo "（CI 未内联编排文件，回退为从 raw 下载）"
  curl -fsSL --retry 3 --retry-delay 3 -o "$DIR/$COMPOSE.new" "__RAW_COMPOSE_URL__" \
    || { echo DEPLOY-FAIL-COMPOSE; exit 1; }
fi
[ -s "$DIR/$COMPOSE.new" ] || { echo DEPLOY-FAIL-COMPOSE-EMPTY; exit 1; }
grep -q '^services:' "$DIR/$COMPOSE.new" || { echo DEPLOY-FAIL-COMPOSE-INVALID; exit 1; }
mv -f "$DIR/$COMPOSE.new" "$DIR/$COMPOSE"

echo "=== [3/6] 登录镜像仓"
if [ -n "__RPW_B64__" ] && [ -n "__REGISTRY_USERNAME__" ]; then
  # 密码经 base64 传递，避免密码里的 $ / 反引号 / 引号被 shell 展开
  echo "__RPW_B64__" | base64 -d | docker login "__REGISTRY__" -u "__REGISTRY_USERNAME__" --password-stdin \
    || { echo DEPLOY-FAIL-LOGIN; exit 1; }
else
  echo "（未提供镜像仓用户名/密码，跳过 login；服务器已登录过可忽略）"
fi

echo "=== [4/6] 拉取 :dev 镜像并重建开发环境 api / web"
docker compose -f "$COMPOSE" pull api web || { echo DEPLOY-FAIL-PULL; exit 1; }
docker compose -f "$COMPOSE" up -d --force-recreate api web || { echo DEPLOY-FAIL-UP; exit 1; }

echo "=== [5/6] 健康检查"
API_CONTAINER=qiaomes-dev-api
WEB_CONTAINER=qiaomes-dev-web
DEV_WEB_PORT=$(grep -E '^DEV_WEB_PORT=' "$ENVF" | tail -1 | cut -d= -f2)
DEV_WEB_PORT=${DEV_WEB_PORT:-8092}

# 开发环境首次部署要在空库上跑全量迁移 + 建演示数据，比生产慢，所以这里等得更久（最多 4 分钟）。
code=000
for i in $(seq 1 80); do
  api_ip=$(docker inspect "$API_CONTAINER" --format '{{range .NetworkSettings.Networks}}{{println .IPAddress}}{{end}}' 2>/dev/null | head -1)
  if [ -n "$api_ip" ]; then
    code=$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://${api_ip}:8080/health/ready" || echo 000)
  fi
  [ "$code" = "200" ] && { echo "开发环境 API 健康检查通过（第 ${i} 次，ip=${api_ip}）"; break; }
  sleep 3
done
if [ "$code" != "200" ]; then
  echo "--- $API_CONTAINER 日志尾部 ---"
  docker logs --tail 60 "$API_CONTAINER" 2>&1 | tail -60
  echo "DEPLOY-FAIL-HEALTH-${code}"
  exit 1
fi

web_code=$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://127.0.0.1:${DEV_WEB_PORT}/" || echo 000)
if [ "$web_code" != "200" ]; then
  echo "--- $WEB_CONTAINER 日志尾部 ---"
  docker logs --tail 20 "$WEB_CONTAINER" 2>&1 | tail -20
  echo "DEPLOY-FAIL-WEB-${web_code}"
  exit 1
fi
echo "开发环境前端端口 ${DEV_WEB_PORT} 探活通过"

echo "=== [6/6] 收尾"
docker image prune -f --filter "until=72h" >/dev/null 2>&1 || true
docker ps --filter name=qiaomes-dev-
echo "--- 内存余量（这台机器只有 896MB，开发环境必须盯着它）"
free -m | head -2
echo DEPLOY-DONE-200
"""


def build_dev_script():
    env_text = "\n".join(base.build_env_lines()) + ("\n" if base.build_env_lines() else "")
    env_b64 = base64.b64encode(env_text.encode("utf-8")).decode("ascii")

    script = DEV_SERVER_SCRIPT
    script = script.replace("__ENV_B64__", env_b64)
    script = script.replace("__COMPOSE_B64__", base.load_compose_b64())
    script = script.replace("__RAW_COMPOSE_URL__", base.optional(
        "RAW_DEV_COMPOSE_URL",
        "https://raw.githubusercontent.com/qiao33128/QiaoMES/dev/docker-compose.dev.yml"))
    script = script.replace("__REGISTRY__", base.optional("REGISTRY", "ccr.ccs.tencentyun.com"))
    script = script.replace("__REGISTRY_USERNAME__", base.optional("REGISTRY_USERNAME"))
    script = script.replace(
        "__RPW_B64__",
        base64.b64encode(base.optional("REGISTRY_PASSWORD").encode("utf-8")).decode("ascii")
        if base.optional("REGISTRY_PASSWORD")
        else "",
    )
    return script


def deploy():
    script = build_dev_script()
    b64 = base64.b64encode(script.encode("utf-8")).decode("ascii")

    print("=== 下发开发环境部署脚本到服务器")
    size = base.upload_script(b64)
    # 编排文件内联在脚本里，正常情况下 5KB 以上；太小说明拼接/解码出了问题
    if size < 5000:
        sys.exit("ERROR: 开发环境部署脚本上传失败或内容不完整（服务器上只有 %d 字节）" % size)
    print("=== 服务器端脚本就绪（%d 字节），后台执行" % size)

    start = "rm -f {log} && nohup bash {script} > {log} 2>&1 & echo STARTED".format(
        log=base.DEPLOY_LOG, script=base.REMOTE_SCRIPT)
    if "STARTED" not in base.run_shell(start, "qiaomes-dev-start"):
        sys.exit("ERROR: 后台部署脚本启动失败")

    print("=== 轮询开发环境部署进度（最长 20 分钟；首次要跑全量迁移，比生产慢）")
    poll = (
        "tail -6 {log} 2>/dev/null; "
        "grep -q 'DEPLOY-DONE-200' {log} 2>/dev/null && echo POLL-OK || "
        "(grep -q 'DEPLOY-FAIL' {log} 2>/dev/null && echo POLL-FAIL || echo POLL-WAIT)"
    ).format(log=base.DEPLOY_LOG)

    deadline = time.time() + 1200
    while time.time() < deadline:
        time.sleep(15)
        out = base.run_shell(poll, "qiaomes-dev-poll")
        print(out.strip())
        if "POLL-OK" in out:
            print("=== 开发环境部署成功")
            return
        if "POLL-FAIL" in out:
            print("=== 开发环境部署失败，完整日志尾部如下")
            print(base.run_shell("tail -60 %s" % base.DEPLOY_LOG, "qiaomes-dev-show-failure"))
            sys.exit("ERROR: 开发环境部署失败（详见上面的服务器日志）")

    print(base.run_shell("tail -60 %s" % base.DEPLOY_LOG, "qiaomes-dev-show-timeout"))
    sys.exit("ERROR: 开发环境部署超时（20 分钟未收到 DEPLOY-DONE-200）")


if __name__ == "__main__":
    deploy()
