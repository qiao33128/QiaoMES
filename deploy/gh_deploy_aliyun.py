#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
QiaoMES · GitHub Actions → 阿里云轻量应用服务器 自动部署脚本

## 为什么是「推镜像 + 服务器只拉不构建」
服务器是 1GB 内存的轻量应用服务器，同时跑着看板站与 PostgreSQL，**在上面 docker build
（.NET SDK 还原 + publish、或 npm build）会直接把内存打满**。所以构建放在 GitHub Runner 上，
镜像推到镜像仓，服务器只做 `docker compose pull && up -d`。

> 参考：看板站走的是「codeload 拉源码 → 服务器本地构建」，那套对纯静态站可行，
> 对 QiaoMES（.NET + Node 双构建）不可行，故此处改用镜像仓路线。

## 为什么不用 SSH
公司网络禁 22 端口，且本机/CI 都不保证能 SSH。这里复用与看板站同一条已验证通道：
阿里云**轻量应用服务器 OpenAPI RunCommand（云助手）**。

## 关键设计
1. 服务器端脚本用 base64 内联下发，避免云端 shell 吞引号 / CRLF 问题；
   **编排文件也同样内联**（取自当次提交的仓库文件）—— 实测服务器直连
   raw.githubusercontent.com 会偶发 `curl: (56) … SSL_ERROR_SYSCALL, errno 110`，上一版部署就是死在这一步；
   脚本含内联编排文件后 base64 约 25000 字符，**超过 RunCommand 单次约 6000 字符的上限**
   （会报 `CmdContent.ExceedLimit`），因此改为分块追加到 `/root/qiaomes_deploy.b64` 再统一解码；
2. 用 `nohup` 后台执行 + 外层轮询日志 —— RunCommand 单次调用 15 分钟超时会杀掉耗时任务；
3. `.env` 采用**合并写入**：只覆盖本次显式提供的键，其余保留服务器上已有值
   （最重要的一条：POSTGRES_PASSWORD 必须与数据库卷初始化时一致，不能被 CI 覆盖成默认值）；
4. 先写 `.env` 再 `up -d`，否则 WEB_PORT / POSTGRES_PASSWORD 会退回 compose 默认值；
5. 健康检查走 `127.0.0.1:<API_PROBE_PORT>/health/ready`（api 端口只绑回环，
   因为 aspnet 运行时镜像里没有 curl/wget，无法在容器内探活）。

## 需要的环境变量（全部由 workflow 从 Secrets 注入）
ALIYUN_AK / ALIYUN_SK / ALIYUN_INSTANCE_ID / ALIYUN_REGION
REGISTRY / REGISTRY_USERNAME / REGISTRY_PASSWORD   （服务器要 docker login 才能拉私有镜像）
QIAOMES_API_IMAGE / QIAOMES_WEB_IMAGE              （含 registry 与 tag 的完整地址）
JWT_SECRET_KEY / POSTGRES_PASSWORD / WEB_PORT / CORS_ORIGINS / GATEWAY_NETWORK
DEMO_DATA_ENABLED
ASSISTANT_ENABLED / ASSISTANT_LLM_BASE_URL / ASSISTANT_LLM_API_KEY / ASSISTANT_LLM_MODEL / ASSISTANT_DB_CONNECTION
RAW_COMPOSE_URL / PUBLIC_BASE_URL / SMOKE_USER / SMOKE_PASSWORD
"""

import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request

# ---------------------------------------------------------------- 服务器端脚本模板
# 用 __TOKEN__ 占位而不是 str.format()：脚本里有大量 shell 的 ${VAR} 与 %{http_code}，
# 用 format 会被花括号搞死。
SERVER_SCRIPT = r"""#!/bin/bash
# 本脚本由 deploy/gh_deploy_aliyun.py 生成，请勿手工修改
DIR=/root/qiaomes
COMPOSE=docker-compose.deploy.yml
mkdir -p "$DIR" || { echo DEPLOY-FAIL-MKDIR; exit 1; }
cd "$DIR" || { echo DEPLOY-FAIL-CD; exit 1; }

echo "=== [1/7] 记录当前镜像（回滚参考）"
docker inspect qiaomes-api --format '{{.Config.Image}}' > "$DIR/.last_api_image" 2>/dev/null || true
docker inspect qiaomes-web --format '{{.Config.Image}}' > "$DIR/.last_web_image" 2>/dev/null || true
cat "$DIR/.last_api_image" 2>/dev/null || true

echo "=== [2/7] 合并写入 .env（只覆盖本次提供的键）"
NEW=/tmp/qiaomes.env.new
rm -f "$NEW"
base64 -d > "$NEW" <<'QIAOMES_ENV_B64'
__ENV_B64__
QIAOMES_ENV_B64
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
chmod 600 "$ENVF"
echo "--- .env 生效内容（密码只显示键名）"
sed 's/=.*/=<hidden>/' "$ENVF"

echo "=== [3/7] 刷新编排文件"
if [ -n "__COMPOSE_B64__" ]; then
  # 编排文件由 CI 内联下发。
  # 为什么不用 curl 从 GitHub raw 拉：实测这台服务器到 raw.githubusercontent.com 的连接会偶发
  # `curl: (56) OpenSSL SSL_read: SSL_ERROR_SYSCALL, errno 110`（超时/RST），一旦拉不到部署就直接失败。
  # 编排文件本来就在 CI 的检出里，内联过来既确定性又少一次外网依赖。
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
if [ -f "$DIR/docker-compose.tcr.yml" ]; then
  mv -f "$DIR/docker-compose.tcr.yml" "$DIR/docker-compose.tcr.yml.bak"
  echo "（旧文件 docker-compose.tcr.yml 已改名为 .bak 留底）"
fi

echo "=== [4/7] 登录镜像仓"
if [ -n "__RPW_B64__" ] && [ -n "__REGISTRY_USERNAME__" ]; then
  # 密码经 base64 传递，避免密码里的 $ / 反引号 / 引号被 shell 展开
  echo "__RPW_B64__" | base64 -d | docker login "__REGISTRY__" -u "__REGISTRY_USERNAME__" --password-stdin \
    || { echo DEPLOY-FAIL-LOGIN; exit 1; }
else
  echo "（未提供镜像仓用户名/密码，跳过 login；公开镜像或服务器已登录过可忽略）"
fi

echo "=== [5/7] 拉取镜像并重建 api / web"
docker compose -f "$COMPOSE" pull api web || { echo DEPLOY-FAIL-PULL; exit 1; }
docker compose -f "$COMPOSE" up -d --force-recreate api web || { echo DEPLOY-FAIL-UP; exit 1; }

echo "=== [6/7] 兜底挂网关网络（compose 已声明 external，这里再幂等补一次）"
docker network connect "__GATEWAY_NETWORK__" qiaomes-web 2>/dev/null || true

echo "=== [7/7] 健康检查"
WEB_PORT=$(grep -E '^WEB_PORT=' "$ENVF" | tail -1 | cut -d= -f2)
WEB_PORT=${WEB_PORT:-8080}

# api 容器不发布宿主机端口（aspnet 运行时镜像里没有 curl/wget，进容器也探不了），
# 所以在宿主机上取它的容器 IP 直接打 8080。容器重建后 IP 会变，故每次重新取。
code=000
for i in $(seq 1 40); do
  api_ip=$(docker inspect qiaomes-api --format '{{range .NetworkSettings.Networks}}{{println .IPAddress}}{{end}}' 2>/dev/null | head -1)
  if [ -n "$api_ip" ]; then
    code=$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://${api_ip}:8080/health/ready" || echo 000)
  fi
  [ "$code" = "200" ] && { echo "API 健康检查通过（第 ${i} 次，ip=${api_ip}）"; break; }
  sleep 3
done
if [ "$code" != "200" ]; then
  echo "--- qiaomes-api 日志尾部 ---"
  docker logs --tail 40 qiaomes-api 2>&1 | tail -40
  echo "DEPLOY-FAIL-HEALTH-${code}"
  exit 1
fi

web_code=$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://127.0.0.1:${WEB_PORT}/" || echo 000)
if [ "$web_code" != "200" ]; then
  echo "--- qiaomes-web 日志尾部 ---"
  docker logs --tail 20 qiaomes-web 2>&1 | tail -20
  echo "DEPLOY-FAIL-WEB-${web_code}"
  exit 1
fi
echo "前端端口 ${WEB_PORT} 探活通过"

docker image prune -f --filter "until=168h" >/dev/null 2>&1 || true
docker ps --filter name=qiaomes-
echo DEPLOY-DONE-200
"""

# ---------------------------------------------------------------- 参数


def need(name, default=None):
    value = os.environ.get(name)
    if value is None or value == "":
        if default is not None:
            return default
        sys.exit("ERROR: 缺少环境变量 %s（请在仓库 Secrets / Variables 里配置，见 docs/CICD.md）" % name)
    return value


def optional(name, default=""):
    return os.environ.get(name) or default


REGION = optional("ALIYUN_REGION", "cn-shanghai")
API_VERSION = "2020-06-01"
API_DOMAIN = "swas.%s.aliyuncs.com" % REGION

DEPLOY_LOG = "/root/qiaomes_deploy.log"

# 写入服务器 .env 的键：值为空则**不下发**（保留服务器已有值），
# 避免 CI 把 POSTGRES_PASSWORD 之类的既有配置覆盖成默认值。
ENV_KEYS = [
    "QIAOMES_API_IMAGE",
    "QIAOMES_WEB_IMAGE",
    "JWT_SECRET_KEY",
    "POSTGRES_PASSWORD",
    "WEB_PORT",
    "CORS_ORIGINS",
    "GATEWAY_NETWORK",
    "DEMO_DATA_ENABLED",
    "ASSISTANT_ENABLED",
    "ASSISTANT_LLM_BASE_URL",
    "ASSISTANT_LLM_API_KEY",
    "ASSISTANT_LLM_MODEL",
    "ASSISTANT_DB_CONNECTION",
]

_client = None


def client():
    global _client
    if _client is None:
        from aliyunsdkcore.client import AcsClient

        _client = AcsClient(need("ALIYUN_AK"), need("ALIYUN_SK"), REGION)
    return _client


def request(action):
    from aliyunsdkcore.request import CommonRequest

    req = CommonRequest()
    req.set_domain(API_DOMAIN)
    req.set_version(API_VERSION)
    req.set_action_name(action)
    req.set_method("POST")
    req.set_protocol_type("https")
    req.add_query_param("RegionId", REGION)
    return req


FINAL_STATES = {"Finished", "Success", "Failed", "Stopped", "Timeout", "Cancelled", "Aborted", "PartialFailed"}


def run_shell(command, name, timeout=60):
    """执行一条命令并等到结束（最多 240 秒），返回 stdout+stderr。"""
    req = request("RunCommand")
    req.add_query_param("InstanceId", need("ALIYUN_INSTANCE_ID"))
    req.add_query_param("CommandContent", command)
    req.add_query_param("Type", "RunShellScript")
    req.add_query_param("Name", name)
    req.add_query_param("Timeout", timeout)
    req.add_query_param("WorkingDir", "/root")

    raw = client().do_action_with_exception(req)
    invoke_id = json.loads(raw.decode("utf-8")).get("InvokeId")
    if not invoke_id:
        sys.exit("ERROR: RunCommand 未返回 InvokeId")

    deadline = time.time() + 240
    while time.time() < deadline:
        time.sleep(3)
        rr = request("DescribeInvocationResult")
        rr.add_query_param("InstanceId", need("ALIYUN_INSTANCE_ID"))
        rr.add_query_param("InvokeId", invoke_id)
        data = json.loads(client().do_action_with_exception(rr).decode("utf-8"))
        result = data.get("InvocationResult") or {}
        status = result.get("InvokeRecordStatus") or result.get("Status") or ""
        if status in FINAL_STATES:
            output = result.get("Output") or ""
            try:
                output = base64.b64decode(output).decode("utf-8", "replace")
            except Exception:  # noqa: BLE001 - 输出可能不是 base64
                pass
            return output
    sys.exit("ERROR: 等待 RunCommand 结果超时（%s）" % name)


def build_env_lines():
    lines = []
    for key in ENV_KEYS:
        value = os.environ.get(key)
        if value is None or value == "":
            continue
        lines.append("%s=%s" % (key, value))
    return lines


def load_compose_b64():
    """把仓库里的编排文件内联成 base64；找不到就返回空串（服务器侧回退为 curl 下载）。"""
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        os.environ.get("COMPOSE_FILE"),
        os.path.normpath(os.path.join(here, "..", "docker-compose.deploy.yml")),
        "docker-compose.deploy.yml",
    ]
    for path in candidates:
        if path and os.path.isfile(path):
            with open(path, "rb") as handle:
                content = handle.read()
            print("=== 内联编排文件 %s（%d 字节）" % (path, len(content)))
            return base64.b64encode(content).decode("ascii")
    print("=== 警告：找不到 docker-compose.deploy.yml，服务器将回退为从 raw 下载")
    return ""


def build_server_script():
    env_text = "\n".join(build_env_lines()) + ("\n" if build_env_lines() else "")
    env_b64 = base64.b64encode(env_text.encode("utf-8")).decode("ascii")

    script = SERVER_SCRIPT
    script = script.replace("__ENV_B64__", env_b64)
    script = script.replace("__COMPOSE_B64__", load_compose_b64())
    script = script.replace("__RAW_COMPOSE_URL__", optional(
        "RAW_COMPOSE_URL",
        "https://raw.githubusercontent.com/qiao33128/QiaoMES/main/docker-compose.deploy.yml"))
    script = script.replace("__REGISTRY__", optional("REGISTRY", "ccr.ccs.tencentyun.com"))
    script = script.replace("__REGISTRY_USERNAME__", optional("REGISTRY_USERNAME"))
    script = script.replace(
        "__RPW_B64__",
        base64.b64encode(optional("REGISTRY_PASSWORD").encode("utf-8")).decode("ascii")
        if optional("REGISTRY_PASSWORD")
        else "",
    )
    script = script.replace("__GATEWAY_NETWORK__", optional("GATEWAY_NETWORK", "kanban_net"))
    return script


# RunCommand 的 CommandContent 上限约 6000 字符（超了报 CmdContent.ExceedLimit），
# 而「服务器脚本 + 内联编排文件」base64 后约 25000 字符，所以必须分块追加再解码。
CHUNK_SIZE = 4500

REMOTE_SCRIPT = "/root/qiaomes_deploy.sh"
REMOTE_B64 = "/root/qiaomes_deploy.b64"


def upload_script(base64_text):
    """分块把 base64 脚本写到服务器并解码，返回解出的脚本字节数（失败返回 0）。"""
    # base64 字母表只有 A-Za-z0-9+/=，不含空格与 shell 元字符，可以不加引号直接 echo（顺带绕开云端 shell 吞引号的问题）
    run_shell("rm -f %s" % REMOTE_B64, "qiaomes-upload-reset")

    chunks = [base64_text[i:i + CHUNK_SIZE] for i in range(0, len(base64_text), CHUNK_SIZE)]
    print("=== 分块上传部署脚本：base64 %d 字符 / %d 块（每块 %d）" % (len(base64_text), len(chunks), CHUNK_SIZE))

    for index, chunk in enumerate(chunks, start=1):
        output = run_shell("echo %s >> %s" % (chunk, REMOTE_B64), "qiaomes-upload-%d" % index)
        if output.strip():
            print("    第 %d 块返回了非预期输出：%s" % (index, output.strip()[:200]))

    decode = (
        "base64 -d {b64} > {script} && rm -f {b64} && chmod +x {script} && wc -c < {script}"
    ).format(b64=REMOTE_B64, script=REMOTE_SCRIPT)
    output = run_shell(decode, "qiaomes-upload-decode")

    for line in reversed(output.splitlines()):
        line = line.strip()
        if line.isdigit():
            return int(line)
    print("    decode 输出：%s" % output.strip()[:300])
    return 0


def deploy():
    script = build_server_script()
    b64 = base64.b64encode(script.encode("utf-8")).decode("ascii")

    print("=== 下发部署脚本到服务器")
    size = upload_script(b64)
    # 编排文件内联在脚本里，正常情况下 10KB 以上；太小说明拼接/解码出了问题
    if size < 5000:
        sys.exit("ERROR: 部署脚本上传失败或内容不完整（服务器上只有 %d 字节）" % size)
    print("=== 服务器端脚本就绪（%d 字节），后台执行" % size)

    start = "rm -f {log} && nohup bash {script} > {log} 2>&1 & echo STARTED".format(
        log=DEPLOY_LOG, script=REMOTE_SCRIPT)
    if "STARTED" not in run_shell(start, "qiaomes-start-deploy"):
        sys.exit("ERROR: 后台部署脚本启动失败")

    print("=== 轮询部署进度（最长 25 分钟）")
    poll = (
        "tail -6 {log} 2>/dev/null; "
        "grep -q 'DEPLOY-DONE-200' {log} 2>/dev/null && echo POLL-OK || "
        "(grep -q 'DEPLOY-FAIL' {log} 2>/dev/null && echo POLL-FAIL || echo POLL-WAIT)"
    ).format(log=DEPLOY_LOG)

    deadline = time.time() + 1500
    while time.time() < deadline:
        time.sleep(15)
        out = run_shell(poll, "qiaomes-poll-deploy")
        print(out.strip())
        if "POLL-OK" in out:
            print("=== 服务器侧部署成功")
            return
        if "POLL-FAIL" in out:
            print("=== 服务器侧部署失败，完整日志尾部如下")
            print(run_shell("tail -60 %s" % DEPLOY_LOG, "qiaomes-show-failure"))
            sys.exit("ERROR: 部署失败（详见上面的服务器日志）")

    print(run_shell("tail -60 %s" % DEPLOY_LOG, "qiaomes-show-timeout"))
    sys.exit("ERROR: 部署超时（25 分钟未收到 DEPLOY-DONE-200）")


def smoke_test():
    """从 Runner 侧打一次真实公网请求：Caddy / DNS / TLS / 应用任一层断了都能暴露。

    区分两类结果（重要）：
      * **HTTP 层失败**（服务应答了错误码、或返回内容不对）→ 真问题，判失败；
      * **网络层不可达**（连不上 / 被 reset）→ 只告警不判失败。实测 GitHub Runner（境外）
        访问该域名 443 会被 `Connection reset by peer`，而服务器本机与国内网络访问正常 ——
        这是跨境链路问题，不该把部署判定为失败。权威结论由服务器端健康检查给出。
    """
    base = optional("PUBLIC_BASE_URL")
    if not base:
        print("=== 未配置 PUBLIC_BASE_URL，跳过后置冒烟测试")
        return

    base = base.rstrip("/")
    user = optional("SMOKE_USER", "admin")
    password = optional("SMOKE_PASSWORD", "Admin123!")
    print("=== 公网冒烟测试 %s" % base)

    body = json.dumps({"username": user, "password": password}).encode("utf-8")
    req = urllib.request.Request(
        base + "/api/auth/login", data=body, headers={"Content-Type": "application/json"}, method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            login = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        sys.exit("ERROR: 冒烟测试登录失败 HTTP %s %s" % (exc.code, exc.read()[:300]))
    except urllib.error.URLError as exc:
        print("    ⚠️ 网络层不可达（%s），跳过公网冒烟。" % exc)
        print("    服务器端健康检查已通过，部署本身有效；跨境链路问题请用国内网络或服务器本机复核。")
        return
    except Exception as exc:  # noqa: BLE001
        print("    ⚠️ 冒烟测试异常（%s），跳过。" % exc)
        return

    token = login.get("accessToken")
    if not token:
        sys.exit("ERROR: 登录响应里没有 accessToken：%s" % login)

    status_req = urllib.request.Request(base + "/api/assistant/status", headers={"Authorization": "Bearer %s" % token})
    try:
        with urllib.request.urlopen(status_req, timeout=30) as resp:
            status = json.loads(resp.read().decode("utf-8"))
        print("    智能问数状态：%s" % json.dumps(status, ensure_ascii=False))
    except urllib.error.URLError as exc:
        print("    ⚠️ 调用 /api/assistant/status 时网络不可达（%s），跳过。" % exc)
        return
    except Exception as exc:  # noqa: BLE001
        sys.exit("ERROR: 冒烟测试调用 /api/assistant/status 失败：%s" % exc)

    print("=== 冒烟测试通过")


if __name__ == "__main__":
    print("=== QiaoMES 自动部署开始（region=%s）" % REGION)
    deploy()
    smoke_test()
    print("=== 全部完成")
