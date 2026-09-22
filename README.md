# QiaoMES

一个独立开发的开源 MES（制造执行系统），面向离散制造（优先 SMT / 电子组装）。用于学习和巩固开发技能，解决设想中的问题，提高开发能力。

**在线演示**：<https://mes.qiaoqiaoqiao.me> · **默认管理员**：`admin` / `Admin123!`

> 📌 开发计划、里程碑与验收标准见 **[ROADMAP.md](ROADMAP.md)**（本项目**唯一**的规划源）。
> 📖 专项文档见下方 [文档索引](#文档索引)，配置项不知道怎么填直接跳到 [配置项填在哪里](#配置项填在哪里)。

---

## 目录

- [这是什么](#这是什么)
- [技术栈](#技术栈)
- [30 秒跑起来](#30-秒跑起来)
- [配置项填在哪里](#配置项填在哪里) ← **「迭代服务还没配置」这类报错看这里**
- [本地开发](#本地开发)
- [部署](#部署)
- [演示数据](#演示数据)
- [已实现功能](#已实现功能)
- [架构说明](#架构说明)
- [文档索引](#文档索引)
- [技术支持与联系](#技术支持与联系)

---

## 这是什么

一个**模块化单体**的 MES：主数据 → 工单 → 工序展开 → 报工 / SN 过站 → 检验与不合格闭环 → 设备与 Andon → 报表与追溯，全链路可跑通、可追溯。

它不是「又一个工单 CRUD 演示」，两个差异化方向：

1. **SMT 场景做深**（上料防错、钢网锡膏时效、站位表…见 ROADMAP「SMT 特色线」）。
2. **两处 AI 落地**：`智能问数`（中文 → 只读 SQL → 结果与图表）与 `改进建议 / AI 自迭代`（提建议 → AI 评审 → 自动执行上线）。

---

## 技术栈

| 层 | 选型 |
|---|---|
| 后端 | ASP.NET Core 10（Clean Architecture，模块化单体） |
| 前端 | Vue 3 + Vite + Element Plus（前后端分离） |
| 数据库 | PostgreSQL 16（Docker 部署） |
| ORM | Entity Framework Core |
| 实时通信 | SignalR（生产看板 / Andon 大屏） |
| 认证授权 | JWT + RBAC（角色-权限；**服务端按请求查库判定，不信任令牌声明**） |
| 可观测性 | 统一 ProblemDetails 错误响应、请求日志 + TraceId、健康检查探针 |
| 测试 | xUnit（领域单元测试 + 真实 HTTP 管道集成测试） |

### 项目结构

```
QiaoMES/
├── docker-compose.yml          # 本地开发编排（镜像本地构建）
├── docker-compose.deploy.yml   # 生产部署编排（只拉镜像，服务器不构建）
├── .env.example                # 全部可配置环境变量的模板
├── ROADMAP.md                  # 开发路线图（唯一规划源）
├── deploy/                     # gh_deploy_aliyun.py（CI 用）· one-shot.sh（一条命令部署，幂等）
├── docs/                       # 专项文档（AI-QUERY / ITERATION / CICD / PERFORMANCE）
├── src/
│   ├── QiaoMES.slnx            # 解决方案
│   ├── BuildingBlocks/         # 共享基础设施
│   │   ├── Shared/             # Result / Error / 分页 / 权限目录 / 契约
│   │   └── Infrastructure/     # 事务工作单元、异常处理、授权、日志、健康检查
│   ├── Modules/                # 业务模块（模块化单体，每个模块四层齐备）
│   │   ├── Identity/           # 认证、用户、角色与权限
│   │   ├── Production/         # 生产（工单、报工、SN 过站）
│   │   ├── MasterData/         # 主数据（产品、物料、工序、工作中心、BOM、工艺路线）
│   │   ├── Quality/            # 质量（检验、不合格处置、SPC、来料批次谱系）
│   │   ├── Equipment/          # 设备与 Andon
│   │   ├── Reporting/          # 报表、班次与日历、预聚合指标、只读投影
│   │   └── Assistant/          # 智能问数（语义层 + NL2SQL + 只读护栏）
│   │       └── {Domain, Application, Infrastructure, Api}
│   └── QiaoMES.Api/            # 主机（组合所有模块）
├── tests/
│   ├── QiaoMES.Domain.Tests/   # 领域与权限目录单元测试（无需数据库）
│   └── QiaoMES.Api.Tests/      # 集成测试（需要 PostgreSQL）
├── tools/                      # seed-demo.ps1（演示数据）/ perf-probe.ps1（压测）
└── frontend/                   # Vue 3 前端
```

---

## 30 秒跑起来

```bash
docker compose up -d --build          # 改完代码必须带 --build，否则跑的还是旧镜像
```

- **前端**：<http://localhost:8080>（Nginx 托管前端并反代 API）
- **管理员**：`admin` / `Admin123!`
- **停止**：`docker compose down`（数据在 Docker 卷里，down 不会删）
  · 卷的**实际名字带项目前缀**，是 `qiaomes_qiaomes_pgdata` 而不是编排文件里写的 `qiaomes_pgdata`
  · 想连回环方便排查，可 `docker exec -it qiaomes-postgres psql -U qiaomes -d qiaomes`

---

## 配置项填在哪里

**所有配置只有一个来源规则**：`appsettings.json`（默认值）→ 环境变量（覆盖它）→ 少数项支持页面内配置（再覆盖它）。

`docker-compose*.yml` 里那些 `X__Y: ${X_Y}` 的写法，就是把 `.env` 里的环境变量透传成 ASP.NET Core 的配置键
（`__` 是层级分隔符，等价于 `X:Y`）。**别写成单冒号**——冒号在 Linux 环境变量名里非法。

### 总表

| appsettings 键 | 环境变量 | 填在哪 | 留空 / 不填会怎样 |
|---|---|---|---|
| `ConnectionStrings:DefaultDb` | `ConnectionStrings__DefaultDb`（compose 已内置） | compose 里已按服务名拼好，**一般不用动** | 连不上库，容器起不来 |
| `Jwt:SecretKey` | `JWT_SECRET_KEY`（`.env`） | `.env` / 仓库 Secrets | 用内置默认值（生产**务必**换成 ≥32 字符随机串；改了会让所有人重新登录） |
| `Jwt:Issuer` / `Jwt:Audience` | — | appsettings | 有默认值，单机部署不用改 |
| `Cors:Origins` | `CORS_ORIGINS`（`.env`） | `.env` / 仓库 Variables；多个用逗号分隔 | 退回 `http://localhost:8080`，跨域部署时前端调不到 API |
| `DemoData:Enabled` | `DEMO_DATA_ENABLED`（`.env`） | 想灌演示数据时临时设 `true` | `false`：`/api/dev/demo-data` 不放行（Development 环境自动放行） |
| `Assistant:Enabled` | `ASSISTANT_ENABLED`（`.env`） | `.env` / 仓库 Variables | `true`：启用智能问数 |
| `Assistant:Llm:BaseUrl` / `:Model` | `ASSISTANT_LLM_BASE_URL` / `ASSISTANT_LLM_MODEL` | `.env` / 仓库 Variables | 默认 DeepSeek 端点与 `deepseek-chat` |
| `Assistant:Llm:ApiKey` | `ASSISTANT_LLM_API_KEY` | **推荐页面内配**（菜单「智能问数」→ 模型配置，保存即生效，密钥掩码存储）；也可走 `.env` / 仓库 Secrets | 问数页显示「还没配置大模型」提示，**不报错、不触库** |
| `Assistant:ConnectionString` | `ASSISTANT_DB_CONNECTION` | `.env`，指向独立只读账号 | 复用主连接，靠 `SET TRANSACTION READ ONLY` 兜底 |
| `Iteration:BaseUrl` | `ITERATION_BASE_URL`（`.env`） | **推荐页面内配**（「改进建议」→ 迭代服务配置，保存即生效）；也可走 `.env` / 仓库 Variables / appsettings | **「改进建议」页显示「迭代服务还没配置」**（功能关闭，不报错） |
| `Iteration:AdminKey` | `ITERATION_ADMIN_KEY` | 同上，需与迭代服务端 `Admin__ApiKey` 一致 | 管理员操作（审阅 / 批准 / 推进周期）会失败；提交修改建议不受影响 |
| `Iteration:TimeoutSeconds` | — | 同上（默认 180） | 提建议要调大模型，默认值通常够用 |
| `Iteration:ConfigFile` | — | 配置文件路径（默认 `<内容根>/config/iteration.json`） | 默认值即可；改它只是换一个存放位置 |

> 变量名 → 配置键的完整映射、以及所有可复制项，见仓库根 [`.env.example`](.env.example)。

### 迭代服务配置怎么填

> 📌 **线上不需要单独部署自迭代服务**：容器 `ai-iteration` 已经写进 `docker-compose.deploy.yml`，
> 一次部署跟着 api/web 一起 pull & up（以前那个 `/root/ai-iteration/` 独立 compose 项目会被首次部署自动收编，
> 数据一并搬过来）。详见 [docs/ITERATION.md](docs/ITERATION.md)。


「改进建议」页报 **「迭代服务还没配置：请在「改进建议 → 迭代服务配置」里填上地址并保存」**
= 当前生效的 `Iteration:BaseUrl` 是空的，也就是这个功能**关着**（不是故障）。

**最省事的办法是页面上填**（与「智能问数」的模型配置同一套路）：左侧「改进建议」→ 右上角 **「迭代服务配置」**，
填地址、密钥可选，点「保存并生效」——**不用重启、不用重新部署**，还能点「测试连接」先验证。

⚠️ 改成放在页面上以后多了一条**会咬人的优先级**：**只要在页面上保存过一次，就以配置文件为准**，
之后再改 `.env` / 环境变量都不会生效（弹窗里的「当前来源」会显示"存在服务器的配置文件里"）。
要交还给部署配置就把文件删掉：`rm /root/qiaomes/config/iteration.json`（立即生效，不用重启）。

🔐 **管理员密钥存在那个配置文件里（Linux 上 `0600`），不进数据库** —— 与 `.env` 同一档保护，
也就不会被 `pg_dump` 备份带走。代价是：**备份/换机器时它要单独带走**。

没有管理员能登录页面时，才走部署配置（四选一）：

| 场景 | 填在哪 | 具体值 |
|---|---|---|
| **本机 `dotnet run`** | `src/QiaoMES.Api/appsettings.Development.json` 的 `Iteration` 节 | `BaseUrl` = `http://localhost:8091`，改完**重启后端** |
| **本机 Docker** | 仓库根 `.env` | `ITERATION_BASE_URL=http://host.docker.internal:8091`（容器里的 `localhost` 指容器自己） |
| **服务器 / 生产（推荐）** | GitHub 仓库 **Variables** 加 `ITERATION_BASE_URL`、**Secrets** 加 `ITERATION_ADMIN_KEY` | 与宿主同在容器网络时填服务名，如 `http://ai-iteration:8080` |
| **任意场景（通用）** | 环境变量 `Iteration__BaseUrl` / `Iteration__AdminKey` | 注意是**双下划线** |

📖 完整说明（地址该写什么、报错对照表、权限、周期节奏）见 **[docs/ITERATION.md](docs/ITERATION.md)**。

---

## 本地开发

```bash
# 1. 数据库
docker compose up -d postgres

# 2. 后端（启动时自动应用迁移并创建种子数据）
cd src/QiaoMES.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5100
#    Swagger:    http://localhost:5100/swagger
#    健康检查:   http://localhost:5100/health/live · /health/ready

# 3. 前端
cd frontend && npm install && npm run dev     # http://localhost:5173

# 4. 测试
dotnet test src/QiaoMES.slnx                  # 全部（集成测试需要本地 PostgreSQL）
dotnet test tests/QiaoMES.Domain.Tests        # 仅领域单元测试（无需数据库）
```

**新增数据库迁移**（模块各自维护迁移，设计时工厂已就绪，无需启动应用）：

```bash
dotnet ef migrations add <名称> \
  --project src/Modules/Identity/Infrastructure/QiaoMES.Identity.Infrastructure.csproj \
  --startup-project src/QiaoMES.Api/QiaoMES.Api.csproj \
  --context IdentityDbContext --output-dir Persistence/Migrations
```

### 一键启动本机（用镜像仓里的版本，不本地构建）

```powershell
pwsh tools/start-local.ps1                        # 拉最新镜像 → 起容器 → 打开浏览器
pwsh tools/start-local.ps1 -WaitHealthySeconds 0  # 不等健康检查
pwsh tools/start-local.ps1 -RegistryUser xxx -RegistryPassword yyy   # 镜像仓还没登录过时
```

🔴 它按 `docker-compose.deploy.yml` 起容器，所以跑完本机就是**镜像仓里的版本**（不再是本地构建版）。
容器名与数据卷相同，**数据不受影响**；想回到本地构建版跑 `docker compose up -d --build` 即可。

它还会**新建一份本机专用的 `.env`**（`WEB_PORT=8080`）—— 刻意**不复制 `.env.example`**：
那份是给服务器写的（8090 端口、域名 CORS），复制过来会把本机端口悄悄改掉，是个很难发现的坑。

### 让本机跟着「线上最新版本」自动更新（可选）

`tools/windows-auto-update.ps1` 是上面那条一键启动的「无人值守版」，做的正是
`restart: unless-stopped` **做不到**的那一步：去镜像仓 `pull` 新镜像再重建（重启策略只会用**现有镜像**把容器拉起来）。

```powershell
pwsh tools/windows-auto-update.ps1 -DryRun        # 先看要做什么，不调用 docker、不写文件
pwsh tools/windows-auto-update.ps1                # 手动执行一次（不打开浏览器）
pwsh tools/windows-auto-update.ps1 -RegisterTask  # 注册成「登录后延迟 2 分钟」自动执行
pwsh tools/windows-auto-update.ps1 -Status        # 查看注册状态与上次结果
pwsh tools/windows-auto-update.ps1 -UnregisterTask
```

两个要点：① 触发方式必须是**登录时**而不是系统启动 —— Docker Desktop 是用户级程序，开机时引擎还没起来，
所以脚本内部会先轮询等 `docker info` 就绪（另外请确认 Docker Desktop 已勾选
`Settings → General → Start Docker Desktop when you sign in to your computer`）；
② 它**只补 `JWT_SECRET_KEY`，绝不碰 `POSTGRES_PASSWORD`** —— 本机库是用默认值 `qiaomes_dev` 初始化的，凭空换密码必然连不上。

---

## 部署

### 四种方式

```bash
# ① 本地构建（开发机自验；改完代码必须带 --build，否则跑的还是旧镜像）
docker compose up -d --build

# ② 服务器人工部署（只拉镜像，服务器无需 .NET SDK / Node，也不吃内存去构建）
docker compose -f docker-compose.deploy.yml up -d

# ③ 自动部署（推荐，日常用这个）：push main 即发布
#    测试 → 构建镜像并推仓 → 云助手在服务器上 pull & up -d → 健康检查 + 公网冒烟
#    见 .github/workflows/deploy.yml，准备清单见 docs/CICD.md
```

**④ 一条命令（应急 / 换机器 / 没有 CI 时最省事）** —— `deploy/one-shot.sh`，**幂等**，可反复执行：

```bash
# 服务器上首次先把脚本取下来（这一行只需要一次）
curl -fsSL -o /root/qiaomes-one-shot.sh https://raw.githubusercontent.com/qiao33128/QiaoMES/main/deploy/one-shot.sh

# 之后每次「部署」就是这一行
bash /root/qiaomes-one-shot.sh
```

它会自动：建目录 → 就位编排文件 → **补齐 `.env`**（缺 `JWT_SECRET_KEY` 生成随机串；数据库密码只在**全新库**上生成，卷已存在时绝不凭空造新密码）→
建好 external 网关网络 → 需要时登录镜像仓 → `pull` → `up -d --wait` → 健康检查并打印访问地址。
**只 pull / up，不执行任何数据库脚本**，也不会删卷；重复执行结果一致（要传私有仓库凭证就加
`REGISTRY_USERNAME=... REGISTRY_PASSWORD=...`，脚本头部注释列出了全部可覆盖参数）。

方式 ②③④ 需要先把镜像推到镜像仓（腾讯云 TCR 或阿里云 ACR，见 [docs/CICD.md](docs/CICD.md) 第 2.1 节）：

```bash
docker login ccr.ccs.tencentyun.com
docker build -t ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-api:latest -f src/QiaoMES.Api/Dockerfile .
docker build -t ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-web:latest -f frontend/Dockerfile .
docker push ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-api:latest
docker push ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-web:latest
```

服务器上的可变项统一写在 `/root/qiaomes/.env`（CI 部署时**合并写入**，只覆盖本次显式提供的键），
**必须先有 `.env` 再 `up -d`**。

### 🔴 四个必读的坑

1. **改完代码没加 `--build`** → 跑的还是旧镜像（表现为前端页面/菜单是旧的）。
2. **漏写 `.env` 就 `up -d`** → `WEB_PORT` 退回 8080（外网端口变了）、`POSTGRES_PASSWORD` 退回默认值（API 连不上已初始化的数据库卷）。
3. **漏配 `JWT_SECRET_KEY`** → 生产编排会**直接拒绝启动**并打印「未设置 JWT_SECRET_KEY…」（CI 侧也会在最前面先拦一次，报错更清楚）。这是刻意的：以前 compose 和 `appsettings.Production.json` 里都带着一个公开的兜底密钥，漏配也照样「部署成功」，等于任何人都能伪造令牌。**失败方式很安全** —— compose 在动手前就退出，旧容器继续跑，不会造成线上中断。本地 `docker-compose.yml` 仍保留兜底值，所以「一行命令跑起来」不受影响。
4. **日志出现 `Npgsql 42703 column xxx does not exist`** → 库表结构与代码不一致（旧 volume 缺 `__EFMigrationsHistory`）。先 `pg_dump -Fc` 备份，再 `docker compose down -v` + `up -d --build` 让迁移从头应用。

> 数据库端口只绑回环（`127.0.0.1:5432`），不对公网开放；需要直连时走 SSH 隧道或云厂商会话通道。

---

## 演示数据

一键灌一套 SMT 场景（幂等 + 可精确清理，手工录入的数据不受影响）：

```bash
pwsh tools/seed-demo.ps1                                   # 本地 docker compose（8080 端口）
pwsh tools/seed-demo.ps1 -BaseUrl https://mes.qiaoqiaoqiao.me   # 服务器 / 域名部署
pwsh tools/seed-demo.ps1 -Days 45 -WorkOrders 12 -SnPerOrder 80  # 自定义规模
pwsh tools/seed-demo.ps1 -Cleanup                           # 只清理
```

灌入内容：3 条 SMT 产线 × 5 个机台工位、3 个产品（各带 BOM 与工艺路线）、5 道工序、12 个 SMT 不良代码、
2 个班次（含跨天夜班）、8 张工单（草稿 → 已完工）与 40 条工序任务、约 300 颗 SN 与约 1800 条过站轨迹、
8 张 IQC + 30 张 IPQC + 6 张 FQC 检验单、不合格处置、15 台设备（含停机历史与点检）、6 条 Andon 呼叫，
并把时间线铺开到最近 N 天、重算预聚合指标。

所有演示数据以 `DEMO-` 前缀标识，`DELETE /api/dev/demo-data` 可精确清除。
该端点默认**仅 Development 环境**放行；要在演示服务器上开启，显式设置 `DemoData:Enabled=true`。

---

## 已实现功能

> 完整的任务清单与验收标准在 [ROADMAP.md](ROADMAP.md)，这里只给概览。

**基础**：注册/登录（JWT）、RBAC 角色权限（权限变更**即时生效**，服务端按请求查库判定）、统一错误响应（ProblemDetails + 业务错误码 + TraceId）、请求级单事务（跨模块写入原子提交）、健康检查、结构化日志、GitHub Actions CI。

**主数据与生产**：产品 / 物料 / 工序 / 工作中心 + BOM 与工艺路线（多版本、同产品单一生效版本、生效版本受保护）→ 工单（状态流转、按日递增的并发安全单号）→ 下达时按生效路线展开**工序任务**并快照 BOM/工艺路线版本 → 工序级报工（良品/不良/报废 + 不良代码，越序拦截，全部工序完成自动结单）→ **SN 与 WIP 过站**（进站/出站/不合格停留/整颗完工）→ 完整流转轨迹追溯。主数据支持 CSV 导入导出。

**质量 / 设备 / 追溯**：检验单（IQC/IPQC/FQC/OQC，定量规格自动判定、AQL 抽样）→ 不合格处置与维修/复检闭环 → 不良代码 Pareto、SPC 判异（前端控制图四线叠加）；设备台账 + 状态机（故障强制填原因）+ 点检保养 + 停机 Pareto + Andon 一键呼叫与**超时自动升级**（SignalR）；按 SN 输出「人机料法环」完整报告 + 批次影响范围；来料批次谱系（入库 → IQC 判定回写准入 → SN 绑定用料 → 正反向双向追溯）。

**报表与集成**：OEE / 达成率 / FPY / 不良 TOP N / 停机 Pareto；报表按生产日 / 产线 / 班次下钻 + CSV 导出 + 打印视图（浏览器另存 PDF）；班次与生产日历（跨天夜班归属生产日）；预聚合汇总表（报表 P95 从 1479ms → 16ms）；模块间事件化（Outbox 表 + 指数退避重试）；开放 API `/api/open/v1`（`X-Api-Key` 独立鉴权 + 按密钥限流 120 次/分，ERP 工单下发幂等、设备采集上报、主数据交换）；运维持快照 `/api/monitoring/snapshot`；车间大屏 `/display` 三屏轮播。

**AI 两个落地**：

- **智能问数**：中文 → 大模型生成只读 SQL → 守卫校验 → 执行 → 表格/柱状/折线/饼图。语义层（19 张表业务注解，列结构实时读 `information_schema`）+ 自我修复（报错回灌重写）+ **三道防线**（`SqlGuard` 白名单 / `SET TRANSACTION READ ONLY` / 只读账号），SQL 全程可审计。见 [docs/AI-QUERY.md](docs/AI-QUERY.md)。
- **改进建议 / AI 自迭代**：持 `iteration:suggest` 的角色可对**自己可用的功能**提修改建议（后端按该页面权限码再校验），“新增功能”仅管理员可提；AI 评审并写入迭代计划，同时与已有计划**交叉对比**（冲突/歧义当场抛出）。周期：周一~周四提建议 → 周五冻结+审阅 → 周五 24:00 结算（低风险沉默放行，中/高风险沉默顺延）→ 周六自动执行、全量测试通过即合并上线。宿主只做入口与权限闸门，数据全在独立的 AI 迭代服务里。见 [docs/ITERATION.md](docs/ITERATION.md)。

**交互细节**：列表/检索页 `keep-alive` 白名单（含滚动位置还原）；问数会话与进行中的请求放 Pinia store（切页面不中断，F5 后仍在）；大屏顶栏显示当前看板名。

---

## 架构说明

采用**模块化单体（Modular Monolith）**：每个业务模块内部按 Clean Architecture 分层（Domain / Application / Infrastructure / Api），模块间边界清晰，未来可平滑拆分为微服务。

几条不变量：

1. **模块不互相引用对方类型**，只依赖共享契约与 `BuildingBlocks`。
2. **Domain 层不引用 EF Core / ASP.NET Core**。
3. **业务失败用 `Result`/`Error` 表达**，异常只用于不可恢复的技术故障。
4. **横切关注点集中在 `BuildingBlocks/Infrastructure`**：事务、异常、日志、授权只在主机实现一次。
5. **子实体必须显式持久化**：EF 对「通过导航集合发现、主键已有值」的实体会判定为 `Modified`，因此新增关联一律通过仓储 `Add*` 方法显式 `Add`。

---

## 文档索引

| 文档 | 内容 |
|---|---|
| [ROADMAP.md](ROADMAP.md) | 阶段计划、验收标准、变更记录（**唯一规划源**） |
| [docs/AI-QUERY.md](docs/AI-QUERY.md) | 智能问数：开箱即用的能力、你还需要准备什么、安全设计、常见问题 |
| [docs/ITERATION.md](docs/ITERATION.md) | 改进建议 / AI 迭代：**配置填在哪里**、报错对照表、权限、周期节奏 |
| [docs/CICD.md](docs/CICD.md) | 自动部署：镜像仓、仓库 Secrets/Variables、服务器上会被改成什么样、回滚、排错 |
| [docs/PERFORMANCE.md](docs/PERFORMANCE.md) | 性能实测报告与优化路径 |
| [.env.example](.env.example) | 全部可配置环境变量模板 |

---

## 技术支持与联系

欢迎使用本 MES 系统！如果在部署或使用过程中遇到问题，可以联系我**免费提供技术支持**：

- **QQ**：2684923183

我会在有时间的时候帮忙处理。也欢迎提 Issue 一起完善这个项目。

## License

MIT
