# QiaoMES

一个独立开发的开源 MES（制造执行系统），用于学习和巩固开发技能，解决设想中的问题，提高开发能力。

> 📌 开发计划、里程碑与验收标准见 **[ROADMAP.md](ROADMAP.md)**。

## 技术栈

- **后端**：ASP.NET Core 10（Clean Architecture，模块化单体）
- **前端**：Vue 3 + Vite + Element Plus（前后端分离）
- **数据库**：PostgreSQL 16（Docker 部署）
- **ORM**：Entity Framework Core
- **实时通信**：SignalR（生产看板）
- **认证授权**：JWT + RBAC（角色-权限，服务端按请求查库判定，不信任令牌声明）
- **可观测性**：统一 ProblemDetails 错误响应、请求日志 + TraceId、健康检查探针
- **测试**：xUnit（领域单元测试 + 真实 HTTP 管道集成测试）

## 项目结构

```
QiaoMES/
├── docker-compose.yml          # PostgreSQL + API + 前端
├── ROADMAP.md                  # 开发路线图（唯一规划源）
├── src/
│   ├── QiaoMES.slnx            # 解决方案
│   ├── BuildingBlocks/         # 共享基础设施
│   │   ├── Shared/             # Result / Error / 分页 / 权限目录 / 契约
│   │   └── Infrastructure/     # 事务工作单元、异常处理、授权、日志、健康检查
│   ├── Modules/                # 业务模块（模块化单体）
│   │   ├── Identity/           # 认证、用户、角色与权限
│   │   │   └── {Domain, Application, Infrastructure, Api}
│   │   ├── Production/         # 生产（工单、报工）
│   │   │   └── {Domain, Application, Infrastructure, Api}
│   │   ├── MasterData/         # 主数据（产品、物料、工序、工作中心、BOM、工艺路线）
│   │   │   └── {Domain, Application, Infrastructure, Api}
│   │   ├── Quality/            # 质量（检验、不合格处置、SPC、来料批次谱系）
│   │   ├── Equipment/          # 设备与 Andon
│   │   ├── Reporting/          # 报表、班次与日历、预聚合指标、只读投影
│   │   └── Assistant/          # 智能问数（语义层 + NL2SQL + 只读护栏）
│   └── QiaoMES.Api/            # 主机（组合所有模块）
├── tests/
│   ├── QiaoMES.Domain.Tests/   # 领域与权限目录单元测试
│   └── QiaoMES.Api.Tests/      # 集成测试（需要 PostgreSQL）
└── frontend/                   # Vue 3 前端
```

## 快速开始

### 三种部署方式

```bash
# ① 本地构建（开发机自验；改完代码必须带 --build，否则跑的还是旧镜像）
docker compose up -d --build

# ② 服务器人工部署（只拉镜像，服务器无需 .NET SDK / Node，也不吃内存去构建）
docker compose -f docker-compose.deploy.yml up -d

# ③ 自动部署（推荐）：push main 即发布
#    GitHub Actions：测试 → 构建镜像并推仓 → 云助手在服务器上 pull & up -d → 健康检查 + 公网冒烟
#    见 .github/workflows/deploy.yml，准备清单见 docs/CICD.md
```

方式 ②③ 需要先把镜像推到镜像仓（腾讯云 TCR 或阿里云 ACR，见 [docs/CICD.md](docs/CICD.md) 第 2.1 节）：

```bash
docker login ccr.ccs.tencentyun.com
docker build -t ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-api:latest -f src/QiaoMES.Api/Dockerfile .
docker build -t ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-web:latest -f frontend/Dockerfile .
docker push ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-api:latest
docker push ccr.ccs.tencentyun.com/qiaoqiao11/qiaomes-web:latest
```

可用环境变量覆盖：`QIAOMES_API_IMAGE` / `QIAOMES_WEB_IMAGE` / `JWT_SECRET_KEY` / `POSTGRES_PASSWORD` / `WEB_PORT` / `CORS_ORIGINS` / `GATEWAY_NETWORK` / `DemoData__Enabled` / `Assistant__*`。
服务器上统一写在 `/root/qiaomes/.env`（CI 部署时会合并写入），**必须先有 `.env` 再 `up -d`**。

> 🔴 **三个必读的坑**
> 1. 改完代码没加 `--build` → 跑的还是旧镜像（表现为前端页面/菜单是旧的）。
> 2. **漏写 `.env` 就 `up -d`** → `WEB_PORT` 退回 8080（外网端口变了）、`POSTGRES_PASSWORD` 退回默认值（API 连不上已初始化的数据库卷）。
> 3. 日志出现 `Npgsql 42703 column xxx does not exist` → 库表结构与代码不一致（旧 volume 缺 `__EFMigrationsHistory`）。先 `pg_dump -Fc` 备份，再 `docker compose down -v` + `up -d --build` 让迁移从头应用。

### 本机跑起来

整个系统（PostgreSQL + 后端 + 前端）通过 Docker Compose 一条命令启动：

```bash
docker compose up -d --build
```

部署完成后访问：

- **前端页面**：`http://localhost:8080`（Nginx 托管前端并反代 API）
- 默认管理员账号：`admin` / `Admin123!`

停止系统：

```bash
docker compose down
```

数据保存在 Docker 卷 `qiaomes_pgdata` 中，`docker compose down` 不会删除数据。

### 生产环境配置

生产配置通过环境变量注入（见 `docker-compose.deploy.yml`），关键项包括：

- `ConnectionStrings__DefaultDb`：数据库连接（**所有模块共用同一连接**，用于跨模块事务）
- `Jwt__SecretKey`：JWT 密钥（**生产环境务必修改**）
- `Cors__Origins`：允许的跨域来源
- `Assistant__*`：智能问数（模型端点与密钥，见 [docs/AI-QUERY.md](docs/AI-QUERY.md)）
- `DemoData__Enabled`：演示数据端点（默认关闭）+ `GATEWAY_NETWORK`：网关网络（域名反代用）

---

## 本地开发

### 1. 启动数据库（PostgreSQL）

```bash
docker compose up -d postgres
```

### 2. 启动后端

```bash
cd src/QiaoMES.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5100
```

后端启动时自动应用数据库迁移并创建种子数据。默认管理员账号：

- 用户名：`admin`
- 密码：`Admin123!`

Swagger 文档：`http://localhost:5100/swagger`
健康检查：`http://localhost:5100/health/live`、`http://localhost:5100/health/ready`

### 3. 启动前端

```bash
cd frontend
npm install
npm run dev
```

前端访问：`http://localhost:5173`

### 4. 运行测试

```bash
# 全部测试（集成测试需要本地 PostgreSQL）
dotnet test src/QiaoMES.slnx

# 仅领域单元测试（无需数据库）
dotnet test tests/QiaoMES.Domain.Tests
```

### 5. 新增数据库迁移

```bash
# 模块各自维护迁移（设计时工厂已就绪，无需启动应用）
dotnet ef migrations add <名称> \
  --project src/Modules/Identity/Infrastructure/QiaoMES.Identity.Infrastructure.csproj \
  --startup-project src/QiaoMES.Api/QiaoMES.Api.csproj \
  --context IdentityDbContext --output-dir Persistence/Migrations
```

## 演示数据与智能问数

### 一键灌一套 SMT 演示数据

```bash
# 本地 docker compose（8080 端口）
pwsh tools/seed-demo.ps1

# 服务器 / 域名部署
pwsh tools/seed-demo.ps1 -BaseUrl https://mes.qiaoqiaoqiao.me

# 自定义规模 / 只清理
pwsh tools/seed-demo.ps1 -Days 45 -WorkOrders 12 -SnPerOrder 80
pwsh tools/seed-demo.ps1 -Cleanup
```

它会灌入：3 条 SMT 产线 × 5 个机台工位、3 个产品（各带 BOM 与工艺路线）、5 道工序、12 个 SMT 不良代码、
2 个班次（含跨天夜班）、8 张工单（草稿 → 已完工）与 40 条工序任务、
约 300 颗 SN 与约 1800 条过站轨迹、8 张 IQC + 30 张 IPQC + 6 张 FQC 检验单、
不合格处置、15 台设备（含停机历史与点检）、6 条 Andon 呼叫，
并把时间线铺开到最近 N 天、重算预聚合指标。

**幂等 + 可清理**：所有演示数据以 `DEMO-` 前缀标识，反复执行只会重建，
`DELETE /api/dev/demo-data` 可精确清除，手工录入的数据完全不受影响。

> 该端点默认**仅 Development 环境**放行；要在演示服务器上开启，显式设置 `DemoData:Enabled=true`。

### 智能问数（中文 → 只读 SQL）

左侧菜单「智能问数」：用中文提问，服务端让大模型生成 SQL，经只读护栏校验后执行，直接给结果与图表。

```jsonc
// appsettings.json / 环境变量（任何 OpenAI 兼容端点都行）
"Assistant": {
  "Enabled": true,
  "Llm": { "BaseUrl": "https://api.deepseek.com/v1", "ApiKey": "sk-xxx", "Model": "deepseek-chat" }
}
```

部署时也可以走环境变量：

```bash
ASSISTANT_LLM_API_KEY=sk-xxx ASSISTANT_LLM_MODEL=deepseek-chat docker compose -f docker-compose.deploy.yml up -d
```

安全设计是**三道防线**：`SqlGuard` 白名单校验（只允许单条 `SELECT`/`WITH`）→
数据库层 `SET TRANSACTION READ ONLY` → （生产建议）独立的只读账号。
模型只拿到「表结构文本」，拿不到连接串；生成的 SQL 完整展示、可复制、可审计。

📖 **完整准备清单与运维说明见 [docs/AI-QUERY.md](docs/AI-QUERY.md)**。

---

## 已实现功能

- [x] 用户注册、登录（JWT 认证）
- [x] RBAC 角色权限：权限目录、角色-权限配置、用户角色分配、启用/停用
- [x] 权限变更**即时生效**（服务端每次请求按用户查库判定权限，带短缓存与代次失效）
- [x] 统一错误响应（ProblemDetails + 业务错误码 + TraceId）
- [x] 主数据管理：产品 / 物料 / 工序 / 工作中心（编码唯一、关键字查询、启停、权限约束）
- [x] BOM 与工艺路线：多版本管理（同产品单一生效版本、生效版本受保护）、BOM 明细含损耗率、工序步骤含质检点
- [x] 工单管理（创建、查询、编辑），查询条件全部下推到数据库
- [x] 工单状态流转（草稿 → 已下达 → 生产中 → 已完成 / 已取消）
- [x] 工单下达时按生效工艺路线展开**工序任务**，并快照 BOM/工艺路线版本
- [x] **工序级报工**：良品 / 不良 / 报废 + 不良代码，越序报工被拒绝，全部工序完成则工单自动完成
- [x] 工时与设备：报工记录实际工时、执行设备与返工类型；工单按标准工时加权计算整体进度
- [x] **SN 与 WIP 过站**：批量生成 SN、进站 / 出站、不合格停留在本工序、按 SN 追溯完整流转轨迹
- [x] 质量管理：检验单（IQC / IPQC / FQC / OQC）、定量规格自动判定、不合格处置与维修/复检闭环、不良代码 Pareto、SPC 判异
- [x] 设备与 Andon：设备台账、状态机（故障强制填原因）、点检保养、停机 Pareto、一键呼叫与超时自动升级（SignalR 实时推送）
- [x] 追溯：按 SN 输出「人机料法环」完整报告（含上游来料批次）+ 批次影响范围查询
- [x] 来料批次谱系：批次入库 → IQC 判定自动回写批次准入 → SN 绑定用料（幂等扣减）→ 正反向双向追溯
- [x] 车间大屏：`/display` 三屏自动轮播（Andon 红黄绿 / 产量与良率 / 质量与设备），超时红灯闪烁，一键全屏
- [x] 指标体系（阶段 4）：OEE = 可用率 × 性能 × 良率、达成率、一次性合格率 FPY、不良 TOP N、停机 Pareto（时长 + 次数）
- [x] 班次与生产日历：跨天夜班归属生产日，节假日不计入计划生产时间，全系统统一统计口径
- [x] 报表与导出：按生产日 / 产线 / 班次下钻，CSV 导出（UTF-8 BOM，Excel 直开）+ 打印视图（浏览器另存为 PDF，无需 PDF 生成库）
- [x] 模块间事件化（阶段 4）：集成事件契约 + Outbox 表（事务内落库 / 异步投递 / 指数退避重试），示例：检验不合格自动发起 Andon 红灯呼叫
- [x] 工作单元修复：跨模块 DbContext 真正纳入同一事务（此前 EF 未注册 `DbContext` 基类导致 `UnitOfWorkFilter` 事务空转）
- [x] 对外集成（阶段 4）：开放 API `/api/open/v1` 用 `X-Api-Key` 独立鉴权 + 按密钥限流（120 次/分钟）；ERP 工单下发/回读（工单号幂等）；设备采集上报经 Outbox 异步应用
- [x] 开放 API 客户端管理：密钥只存 SHA-256 摘要、明文创建时返回一次，可单独停用与设过期
- [x] 性能与容量（阶段 4）：`tools/perf-probe.ps1` 一键压测（种子生成 / 索引体检 / EXPLAIN / 并发分位），实测报告见 `docs/PERFORMANCE.md`
- [x] 预聚合汇总表：`reporting.daily_shift_metrics`（生产日 + 班次），报表/看板 P95 从 1479ms 降到 16ms（约 92 倍），后台每 5 分钟滚动重算、未覆盖自动回退实时
- [x] 主数据交换（开放 API）：产品/物料/BOM 查询与下发，物料按编码、BOM 按「产品 + 版本」幂等
- [x] 运维可观测性：`/api/monitoring/snapshot` 暴露 Outbox 积压、预聚合新鲜度、数据库指标与告警阈值（零外部依赖）
- [x] 主数据 CSV 导入导出（产品 / 物料 / 工序 / 工作中心，按编码 upsert）
- [x] 并发安全的工单号生成（按日递增，数据库原子取号）
- [x] SignalR 实时生产看板（通知在事务提交后发送）
- [x] **智能问数（AI + 数据库）**：中文提问 → 大模型生成只读 SQL → 守卫校验 → 执行 → 表格 / 柱状 / 折线 / 饼图，SQL 全程可审计
- [x] **智能问数的安全三层**：`SqlGuard`（白名单 + 黑名单 + 强制 LIMIT）、`SET TRANSACTION READ ONLY` + `statement_timeout`、可选独立只读账号
- [x] **语义层**：19 张核心表的中文业务名 / 枚举取值 / 业务口径；列结构实时读 `information_schema`，不会讲错列名
- [x] **NL2SQL 自我修复**：SQL 报错回灌模型重写（默认 2 轮），列名写错、漏软删除条件基本能自动纠回
- [x] **一键演示数据**：`tools/seed-demo.ps1` 灌入完整 SMT 场景（3 产线 × 8 工单 × 300 SN × 检验 / 设备 / Andon），幂等且可精确清理
- [x] **SPC 控制图前端**：均值 / ±3σ 控制限 / 规格限四线叠加，判异结论与超限点高亮
- [x] 前端 Vue3 界面（登录、工单管理、生产看板、SN 过站、质量管理、设备与 Andon、追溯查询、主数据、报表与班次、智能问数、角色与权限、用户管理、车间大屏）
- [x] 请求级单事务（跨模块写入原子提交）
- [x] 健康检查、结构化日志、GitHub Actions CI

## 架构说明

采用**模块化单体（Modular Monolith）**架构：每个业务模块内部按 Clean Architecture 分层（Domain / Application / Infrastructure / Api），模块之间边界清晰，未来可平滑拆分为微服务。

几条不变量：

1. **模块不互相引用对方类型**，只依赖共享契约与 `BuildingBlocks`。
2. **Domain 层不引用 EF Core / ASP.NET Core**。
3. **业务失败用 `Result`/`Error` 表达**，异常只用于不可恢复的技术故障。
4. **横切关注点集中在 `BuildingBlocks/Infrastructure`**：事务、异常、日志、授权只在主机实现一次。
5. **子实体必须显式持久化**：EF 对「通过导航集合发现、主键已有值」的实体会判定为 `Modified`，因此新增关联一律通过仓储 `Add*` 方法显式 `Add`。

## 技术支持与联系

欢迎使用本 MES 系统！如果在部署或使用过程中遇到问题，可以联系我**免费提供技术支持**：

- **QQ**：2684923183

我会在有时间的时候帮忙处理。也欢迎提 Issue 一起完善这个项目。

## License

MIT
