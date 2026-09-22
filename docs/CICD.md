# CI/CD 自动部署（GitHub Actions → 阿里云）

> push 到 `main` → 测试 → 构建镜像 → 推镜像仓 → 云助手在服务器上 `pull & up -d` → 健康检查 + 公网冒烟。
> 全程不需要 SSH，也不需要登录服务器。

---

## 1. 为什么是这条路

服务器是 **1GB 内存的轻量应用服务器**，同时跑着看板站 + Caddy + PostgreSQL。
在它上面 `docker build`（.NET SDK 还原 + publish、或 npm build）会直接把内存打满 —— 看板站的
CI 就吃过这个亏（最终改成"服务器本地构建纯静态站"）。QiaoMES 是 **.NET + Node 双构建**，
服务器侧构建完全不可行，所以：

| 环节 | 放在哪 | 为什么 |
|---|---|---|
| 测试 / 构建镜像 | GitHub Runner | CPU 与带宽都充裕，且能复用 GHA 层缓存 |
| 分发镜像 | 镜像仓（TCR / ACR） | 服务器只需 `pull`，产物可追溯、可回滚 |
| 执行部署 | 阿里云**云助手 RunCommand** | 公司网络禁 SSH 22 端口；走 HTTPS OpenAPI，无需任何入站端口 |

另外，RunCommand 单次调用有 **15 分钟超时**，所以服务器端脚本用 `nohup` 后台跑，
CI 侧轮询日志（`DEPLOY-DONE-200` / `DEPLOY-FAIL-*`）——这套与看板站验证过的通道一致。

```
GitHub Actions
├── test      复用 ci.yml（后端 build+test / 前端 npm ci+build）
├── publish   docker buildx → 推 <REGISTRY>/<NS>/qiaomes-api:<sha> + :latest
│                            → 推 <REGISTRY>/<NS>/qiaomes-web:<sha> + :latest
└── deploy    python deploy/gh_deploy_aliyun.py
              ├── base64 下发服务器脚本（避开云端 shell 吞引号 / CRLF）
              ├── nohup 后台执行 + 轮询日志（最长 25 分钟）
              └── 服务器上：写 .env → 拉 docker-compose.deploy.yml → docker login
                            → compose pull api web → up -d --force-recreate
                            → 兜底挂 kanban_net → 健康检查 → DEPLOY-DONE-200
              └── Runner 侧：公网冒烟（登录 + 调 /api/assistant/status）
```

**镜像 tag 用 git SHA**，所以每次部署都是可回滚的（见第 5 节）。

---

## 2. 一次性准备

### 2.1 镜像仓（二选一）

#### 方案 A：沿用腾讯云 TCR（当前默认，零新增配置）

`ci.yml` / `deploy.yml` 的默认值就是 `ccr.ccs.tencentyun.com/qiaoqiao11`，服务器已 `docker login` 过。
只需在仓库 Secrets 里配：

| Secret | 值 |
|---|---|
| `REGISTRY` | `ccr.ccs.tencentyun.com` |
| `REGISTRY_NAMESPACE` | `qiaoqiao11` |
| `REGISTRY_USERNAME` | TCR 个人版的登录名（**容器镜像服务控制台 → 访问凭证** 可见，形如一串数字账号 ID） |
| `REGISTRY_PASSWORD` | TCR 个人版访问凭证密码 |

> ⚠️ 已知风险：GitHub Runner 在境外，**推腾讯 TCR 可能慢或不稳**（看板站当初记录过"跨境 push TCR 不可靠"，
> 那次最终放弃了镜像仓路线）。如果 `publish` 阶段 push 反复失败或超时，直接切方案 B。

#### 方案 B：阿里云 ACR 个人版（推荐，与服务器同地域）

1. 打开 **容器镜像服务 ACR 控制台** → 创建**个人版实例**（免费；一个阿里云账号限一个）。
2. 在实例里创建命名空间（例如 `qiaoqiao11`）和两个仓库：`qiaomes-api`、`qiaomes-web`。
3. 在 **访问凭证** 里设置**固定密码**（ACR 个人版必须用它做 `docker login`，不是登录密码）。
4. 仓库 Secrets 改成：

| Secret | 值 |
|---|---|
| `REGISTRY` | `registry.cn-shanghai.aliyuncs.com` |
| `REGISTRY_NAMESPACE` | `qiaoqiao11`（或你建的命名空间） |
| `REGISTRY_USERNAME` | 阿里云账号全名（如 `qiao33128`） |
| `REGISTRY_PASSWORD` | 上一步设置的**固定密码** |

> 服务器与 ACR 同地域，`docker pull` 走阿里云内网链路，又快又稳。
> 想再快一点/省流量，可把 `REGISTRY` 换成 VPC 域名 `registry-vpc.cn-shanghai.aliyuncs.com`
> （服务器必须与 ACR 同地域；CI 侧仍需用公网域名推送，所以这一步只适合手工验证）。

### 2.2 仓库 Secrets（Settings → Secrets and variables → Actions → Secrets）

| Secret | 必填 | 说明 |
|---|---|---|
| `ALIYUN_AK` / `ALIYUN_SK` | ✅ | 阿里云 AccessKey。**建议建 RAM 子用户**（授权 `AliyunECSRunCommand` 等命令执行相关权限即可），不要用主账号 |
| `ALIYUN_INSTANCE_ID` | ✅ | 轻量应用服务器实例 ID：`7defc9fc6f3140b38a26336030b3c487` |
| `REGISTRY` / `REGISTRY_NAMESPACE` / `REGISTRY_USERNAME` / `REGISTRY_PASSWORD` | ✅ | 见 2.1 |
| `JWT_SECRET_KEY` | ✅ **强制** | ≥32 字符的随机串（**改了会让所有人重新登录**；问数页已保存的模型密钥也会失效，需重填）。🔴 没配就没有兜底：生产编排用它做 `${JWT_SECRET_KEY:?}` 必填校验，CI 预检也会先拦一次，服务器上的 compose 会直接拒绝启动（**安全**：旧容器继续跑，不会中断线上） |
| `ASSISTANT_LLM_API_KEY` | 可空 | 智能问数的大模型密钥；不配就是"未配置模型"状态（页面会给提示，功能不报错） |
| `POSTGRES_PASSWORD` | ⚠️ 建议**不配** | 部署脚本只在 CI 提供了值时才覆盖服务器 `.env`。**一旦配错会让 API 连不上已初始化的数据库卷** |
| `SMOKE_USER` / `SMOKE_PASSWORD` | 可空 | 冒烟测试账号，默认 `admin` / `Admin123!` |
| `ITERATION_ADMIN_KEY` | 可空 | AI 迭代服务的管理员密钥，必须与服务端 `Admin__ApiKey` 一致。不配则「改进建议」功能关闭（页面给明确提示，不报错） |

🔴 **`POSTGRES_PASSWORD` 为什么危险**：PostgreSQL 的密码在**数据卷首次初始化时**就写死了。
如果 CI 把一个新值推上去，API 会用新密码连一个只认旧密码的库 → 连接失败、容器起不来。

### 2.3 仓库 Variables（同一页面 → Variables）

| Variable | 默认值 | 说明 |
|---|---|---|
| `WEB_PORT` | `8090` | 前端对外端口（与看板站共存，故不用 80） |
| `CORS_ORIGINS` | `https://mes.qiaoqiaoqiao.me,http://139.196.195.44:8090` | 允许的跨域来源 |
| `GATEWAY_NETWORK` | `kanban_net` | 看板站 Caddy 的网关网络（caddy 靠它反代 `qiaomes-web:80`） |
| `PUBLIC_BASE_URL` | `https://mes.qiaoqiaoqiao.me` | 部署后 Runner 打这个地址做冒烟；留空则跳过 |
| `ASSISTANT_LLM_BASE_URL` | `https://api.deepseek.com/v1` | 任何 OpenAI 兼容端点 |
| `ASSISTANT_LLM_MODEL` | `deepseek-chat` | |
| `DEMO_DATA_ENABLED` | `false` | 设为 `true` 才能在服务器上用 `tools/seed-demo.ps1` 灌演示数据 |
| `ITERATION_BASE_URL` | 空（= 功能关闭） | AI 迭代服务地址。与宿主同容器网络时填服务名，如 `http://ai-iteration:8080`；**详细说明见 [ITERATION.md](ITERATION.md)** |

> Variables 不是密钥，改完直接生效，不用动 workflow。

---

## 3. 启用

1. 把仓库推到 `main`（workflow 必须在默认分支上才能被触发）。
2. 打开 **Actions → Deploy → Run workflow**：
   - `image_tag` 留空（用本次提交的 SHA）
   - `skip_deploy` 勾上可以「只构建推镜像、不发布」
3. 观察 3 个 job：`测试` → `构建并推送镜像` → `部署到阿里云`。
4. 之后每次 push `main` 都会自动跑（只改 `**.md` / `docs/**` / `tools/**` 的提交会被跳过）。

---

## 4. 服务器上会被改成什么样

`/root/qiaomes/`：

| 文件 | 说明 |
|---|---|
| `docker-compose.deploy.yml` | 每次部署由 CI **内联下发**（内容取自当次提交的仓库文件），与仓库强一致。由 CI 维护，别手工改 |
| `.env` | 部署脚本**合并写入**：只覆盖本次显式提供的键，其余保留原值 |
| `.env`（示例键） | `QIAOMES_API_IMAGE` / `QIAOMES_WEB_IMAGE` / `JWT_SECRET_KEY` / `POSTGRES_PASSWORD` / `WEB_PORT` / `CORS_ORIGINS` / `GATEWAY_NETWORK` / `ASSISTANT_*` / `ITERATION_*` —— 完整模板见仓库根 [`.env.example`](../.env.example) |
| `config/` | **迭代服务配置目录**：挂到容器的 `/app/config`，页面上配的地址与管理员密钥写在 `config/iteration.json`。部署脚本会创建并 `chown 1654`（容器以非 root 运行）。⚠️ **换机器 / 重装时要一并备份** —— 它不在数据库备份里 |
| `docker-compose.tcr.yml.bak` | 旧文件名（首次部署时自动改名留底） |
| `.last_api_image` / `.last_web_image` | 上一次的镜像地址，回滚时参考 |
| `/root/qiaomes_deploy.log` | 部署日志（排查第一现场） |
| `/root/qiaomes_deploy.sh` | CI 下发的服务器端脚本（内容可读，便于核对） |

**编排文件与 `.env` 都在服务器上，所以手工部署依然可用**：

```bash
cd /root/qiaomes
docker compose -f docker-compose.deploy.yml pull
docker compose -f docker-compose.deploy.yml up -d --force-recreate api web
```

⚠️ **两个必踩的坑（都已在 CI 里规避，手工操作时请注意）**：

1. **`.env` 必须先就位再 `up -d`**。否则 `WEB_PORT` 退回 8080（外网端口变了）、
   `POSTGRES_PASSWORD` 退回默认值（API 连不上库）。
2. **`gateway`（`kanban_net`）必须挂在 `qiaomes-web` 上**，否则 `mes.qiaoqiaoqiao.me` 会 502/无法解析。
   手工 `docker network connect` 不是持久的，**容器一重建就丢** ——
   所以 `docker-compose.deploy.yml` 把它声明成了 `external` 网络，重建时会自动挂上。

---

## 5. 回滚

镜像 tag 是 git SHA，所以回滚 = 用旧 SHA 再部署一次：

**Actions → Deploy → Run workflow → `image_tag` 填旧 SHA** → 运行。

服务器上还留有上一次的镜像地址（`.last_api_image`），也可以直接手工回滚：

```bash
cd /root/qiaomes
sed -i 's#^QIAOMES_API_IMAGE=.*#QIAOMES_API_IMAGE='"$(cat .last_api_image)"'#' .env
sed -i 's#^QIAOMES_WEB_IMAGE=.*#QIAOMES_WEB_IMAGE='"$(cat .last_web_image)"'#' .env
docker compose -f docker-compose.deploy.yml up -d --force-recreate api web
```

> 数据库**不做版本回滚**：EF 迁移是向前兼容的，回滚应用镜像不会回滚表结构。
> 真要回滚数据得用 `backup/` 里的 `pg_dump`。

---

## 6. 排错

| 现象 | 原因 / 处理 |
|---|---|
| `test` job 失败 | 代码问题，看测试输出；可以先用 `skip_deploy` 单独验证 `publish` |
| `publish` 卡在 push / 超时 | 境外 Runner 推 TCR 不稳 → 按 2.1 切阿里云 ACR |
| `deploy` 报 `缺少环境变量 ALIYUN_*` | Secrets 没配全 |
| 日志里 `DEPLOY-FAIL-PULL` | 服务器拉不到镜像：镜像仓地址/凭证错，或镜像还没推成功 |
| 日志里 `DEPLOY-FAIL-LOGIN` | 镜像仓用户名/密码错（ACR 个人版要用**固定密码**） |
| 日志里 `DEPLOY-FAIL-HEALTH-000` | 容器起来了但 API 没就绪 → 日志里会跟一段 `docker logs qiaomes-api`，多数是数据库迁移失败或 `POSTGRES_PASSWORD` 被改错 |
| 日志里 `DEPLOY-FAIL-COMPOSE` | 只出现在"回退下载"路径（CI 没内联编排文件时）：服务器访问 GitHub raw 失败，实测偶发 `curl: (56) SSL_ERROR_SYSCALL, errno 110` 超时。正常路径编排文件由 CI 内联，不走网络 |
| 部署脚本报 `CmdContent.ExceedLimit` | RunCommand 单次命令约 **6000 字符**上限。脚本（含内联编排文件）base64 后约 25000 字符，超限，所以 `deploy/gh_deploy_aliyun.py` 改成**分块追加到 `qiaomes_deploy.b64` 再统一解码**（每块 4500 字符，约 6 次 RunCommand）。以后要内联更大的东西，调小 `CHUNK_SIZE` 即可 |
| 部署成功但域名打不开 | `qiaomes-web` 没挂上 `kanban_net`，或 Caddyfile 里的 mes 站点被看板站的 CI 覆盖了（看板仓 `deploy/Caddyfile` 必须带 mes 段） |
| 前端页面/菜单是旧的 | 镜像没更新：确认 `up -d` 带了 `--force-recreate`（脚本里有），或手动 `docker compose pull` 后重建 |

---

## 7. 与手工部署的关系

| 方式 | 命令 | 用途 |
|---|---|---|
| 本地构建 | `docker compose up -d --build` | 开发机自验 |
| **一条命令（幂等脚本）** | `bash deploy/one-shot.sh`（服务器上没有仓库时先 `curl` 把脚本拉下来，见 `deploy/one-shot.sh` 头部注释） | **应急 / 换机器 / 没有 CI**；把准备 + 拉取 + 启动 + 健康检查全做完，可反复执行 |
| 手工推镜像 + 服务器拉 | `docker build -t <reg>/... && docker push` + 服务器 `compose pull && up -d` | 手工控制每一步 |
| **CI/CD（本文）** | push `main` | 日常发布 |

四种方式共用同一份 `docker-compose.deploy.yml` 与同一套 `.env` 约定，不会打架。

> `deploy/one-shot.sh` 与 CI 的差异只在「谁提供配置」：CI 从仓库 Secrets 合并写 `.env`、编排文件由 CI 内联下发；
> 脚本则**自动补齐** `.env`（缺 `JWT_SECRET_KEY` 就生成随机串；数据库密码只在**全新库**上生成 —— 卷已存在时绝不凭空造新密码）、
> 建好 external 网络，再 `pull` + `up -d --wait` + 健康检查。它**只 pull / up，不执行任何数据库脚本**。

---

## 8. 安全提醒

- `ALIYUN_AK/SK` 目前既存在本机 `mcp.json`、也存在服务器 `~/.acme.sh/account.conf`，
  **建议换成一个 RAM 子用户**（仅授予命令执行 + DNS 解析所需权限），三处同步替换。
- 🔴 **生产默认密钥已从配置里移除，改为「漏配就起不来」**：
  `appsettings.Production.json` 不再带 `Jwt:SecretKey` 与含 `qiaomes_dev` 的连接串（放进去等于把弱口令随镜像发出去）。
  现在 `JWT_SECRET_KEY` / `ConnectionStrings__DefaultDb` 必须由环境变量提供，否则 API 启动时**当场报错并把缺什么说清楚**
  （而不是抛 `IDX10703` 或"连接串为空"）。⚠️ **确认仓库 Secrets 里已有 `JWT_SECRET_KEY` 再部署**，否则新版本会起不来。
  另外，若密钥仍是内置占位值（`QiaoMES_*` / 含 `Change_Me`），生产环境会打一条醒目警告但不阻断启动 ——
  本地 `docker compose up` 依赖这个兜底值，一刀切会让它直接起不来。
- 数据库 `5432` 端口**默认只绑回环**（`${POSTGRES_BIND:-127.0.0.1}:5432:5432`，此前是 `0.0.0.0` ——
  这台机器有公网 IP，等于把库暴露在扫描面上）。容器内互连走 `qiaomes-net` 的服务名 `postgres`，不受影响；
  需要在服务器上直连时用 `docker exec -it qiaomes-postgres psql -U qiaomes -d qiaomes`。
  ⚠️ **服务器上不要设 `POSTGRES_BIND`**。
  它存在的唯一理由是本机：**Docker Desktop（Windows/macOS）只代理 `0.0.0.0` 的端口映射**，
  绑 `127.0.0.1` 时端口只存在于 WSL 虚拟机内部，Windows 宿主连不上（`dotnet run` 会报 connection refused）——
  所以 `tools/start-local.ps1` 会在本机 `.env` 里写 `POSTGRES_BIND=0.0.0.0`。
- 部署只跑 `pull / up -d`，**不执行任何数据库脚本**；迁移由 API 启动时自动应用。
