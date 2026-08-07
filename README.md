# QiaoMES

一个独立开发的开源 MES（制造执行系统），用于学习和巩固开发技能，解决设想中的问题，提高开发能力。

## 技术栈

- **后端**：ASP.NET Core 10（Clean Architecture，模块化单体）
- **前端**：Vue 3 + Vite + Element Plus（前后端分离）
- **数据库**：PostgreSQL 16（Docker 部署）
- **ORM**：Entity Framework Core
- **实时通信**：SignalR（生产看板）
- **认证授权**：JWT + RBAC

## 项目结构

```
QiaoMES/
├── docker-compose.yml          # PostgreSQL 数据库
├── src/
│   ├── QiaoMES.slnx            # 解决方案
│   ├── BuildingBlocks/         # 共享基础设施
│   │   ├── Shared/             # 结果类型、分页、领域基类
│   │   └── Infrastructure/     # EF Core、数据库
│   ├── Modules/                # 业务模块（模块化单体）
│   │   ├── Identity/           # 认证授权模块
│   │   │   ├── Domain/         # 用户、角色领域模型
│   │   │   ├── Application/    # 认证服务
│   │   │   ├── Infrastructure/ # DbContext、仓储、JWT
│   │   │   └── Api/            # 认证控制器
│   │   └── Production/         # 生产（工单）模块
│   │       ├── Domain/         # 工单、报工领域模型
│   │       ├── Application/    # 工单服务、状态机
│   │       ├── Infrastructure/ # DbContext、仓储
│   │       └── Api/            # 工单控制器、SignalR Hub
│   └── QiaoMES.Api/            # 主机（组合所有模块）
└── frontend/                   # Vue 3 前端
```

## 快速开始

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

### 3. 启动前端

```bash
cd frontend
npm install
npm run dev
```

前端访问：`http://localhost:5173`

## 已实现功能

- [x] 用户注册、登录（JWT 认证）
- [x] RBAC 角色权限（admin / supervisor / operator）
- [x] 工单管理（创建、查询、编辑）
- [x] 工单状态流转（草稿 → 已下达 → 生产中 → 已完成 / 已取消）
- [x] 生产报工（自动完成工单）
- [x] SignalR 实时生产看板
- [x] 前端 Vue3 界面（登录、工单管理、生产看板）

## 架构说明

采用**模块化单体（Modular Monolith）**架构：每个业务模块内部按 Clean Architecture 分层（Domain / Application / Infrastructure / Api），模块之间边界清晰，未来可平滑拆分为微服务。

## License

MIT
