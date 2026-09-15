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
│   │   └── MasterData/         # 主数据（产品、物料、工序、工作中心）
│   │       └── {Domain, Application, Infrastructure, Api}
│   └── QiaoMES.Api/            # 主机（组合所有模块）
├── tests/
│   ├── QiaoMES.Domain.Tests/   # 领域与权限目录单元测试
│   └── QiaoMES.Api.Tests/      # 集成测试（需要 PostgreSQL）
└── frontend/                   # Vue 3 前端
```

## 快速开始（Docker 一键部署，推荐）

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

生产配置通过环境变量注入（见 `docker-compose.yml`），关键项包括：

- `ConnectionStrings__DefaultDb`：数据库连接（**所有模块共用同一连接**，用于跨模块事务）
- `Jwt__SecretKey`：JWT 密钥（**生产环境务必修改**）
- `Cors__Origins`：前端访问地址

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
- [x] 并发安全的工单号生成（按日递增，数据库原子取号）
- [x] SignalR 实时生产看板（通知在事务提交后发送）
- [x] 前端 Vue3 界面（登录、工单管理、生产看板、角色与权限、用户管理）
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
