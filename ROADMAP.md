# QiaoMES 开发路线图（ROADMAP）

> 一个独立开发的开源 MES。本文件是本项目的**唯一规划源**，所有开发任务、里程碑、验收标准都在此登记。
> 状态图例：`[x]` 已完成 · `[~]` 进行中 · `[ ]` 未开始

---

## 0. 项目定位

| 项 | 说明 |
|---|---|
| 定位 | 面向离散制造（优先 SMT / 电子组装）的开源 MES |
| 技术栈 | ASP.NET Core 10 + EF Core 10 + PostgreSQL 16 + Vue 3 + Element Plus + SignalR |
| 架构 | **模块化单体（Modular Monolith）**，每模块内部 Clean Architecture 分层 |
| 长期形态 | 模块边界稳定后，按模块拆分为微服务（见阶段 5） |

### 架构原则（不可违反）

1. **模块自包含**：`Modules/<X>/{Domain,Application,Infrastructure,Api}` 四层齐备，模块间**不直接引用对方类型**，只通过共享契约或集成事件通信。
2. **依赖方向单向**：`Api → Application → Domain`；`Infrastructure → Application/Domain`；`BuildingBlocks` 不依赖任何模块。
3. **领域不碰框架**：Domain 层不引用 EF Core / ASP.NET Core。
4. **业务失败不抛异常**：统一用 `Result` / `Error` 表达，异常只用于不可恢复的技术故障。
5. **横切关注点集中**：事务、异常、日志、授权一律在 `BuildingBlocks/Infrastructure` 或主机统一处理，模块内不重复实现。

### 当前状态（2026-09-22 更新）

**阶段 1 已完成**（33 个自动化测试全绿）：统一异常处理 + ProblemDetails、结构化日志与 TraceId、RBAC 权限体系（角色-权限持久化、动态授权策略、角色/用户管理接口与页面）、查询下推数据库、并发安全的工单号生成、跨模块单事务（共享连接 + 提交后动作）、健康检查端点、GitHub Actions CI。

**已有能力**：Identity（登录/注册/me、JWT、BCrypt、角色与权限管理）、Production（工单聚合 + 状态机、报工、SignalR 实时推送）、BuildingBlocks（`Result`/`Error`/`Pagination`/`Entity`、工作单元事务、权限授权、异常处理、请求日志）、前端 4 页（工单管理、生产看板、角色与权限、用户管理）、Docker Compose 一键部署。

**阶段 2 已完成（M2 交付）**：主数据（产品/物料/工序/工作中心/BOM/工艺路线）、工单按生效路线展开工序任务、工序级报工（含工时与设备）、SN 与 WIP 过站追溯、主数据 CSV 导入导出全部落地。

**已打通完整链路**：建产品 → 建 BOM/工艺路线 → 下工单 → 展开工序任务 → 逐工序报工（含不良/报废/工时）→ SN 过站流转与追溯 → 完工。

**阶段 3 全部完成（M3 交付）**：质量（检验 / NCR 维修复检闭环 / SPC）、设备与 Andon（超时自动升级 + SignalR）、SN 追溯与批次影响范围、车间大屏轮播、来料批次谱系（IQC ↔ SN 双向）全部落地。质量模块的检验 / 处置 / 批次 / 不良代码四个前端页与 **SPC 控制图**（均值 / ±3σ / 规格限 + 判异）已于阶段 6 一并补齐。

**阶段 4 全部完成（M4 交付）**：指标体系（OEE / 达成率 / FPY / 不良 TOP N / 停机 Pareto）、报表与看板（下钻 + 班次逐档 + CSV + 打印视图）、班次与日历（跨天夜班归属生产日）、对外集成（开放 API + 独立鉴权 + 限流 + ERP 幂等下发）、模块间事件化（Outbox + 指数退避）、性能与容量（预聚合汇总表，1000 万行复测 P95 8316ms → 24ms）。

**阶段 5 已评估结案**：5.1 库拆分 / 5.2 服务拆分**主动放弃**（无真实独立伸缩诉求），5.3 网关 / 5.4 消息队列延后，5.5 幂等消费已具备，5.6 可观测性务实版已完成。

**阶段 6 已完成（M6 交付）**：**智能问数（AI 接入数据库）** —— 语义层（19 张表的业务注解，列结构实时读 `information_schema`）+ OpenAI 兼容 NL2SQL + 失败自我修复 + 只读三道防线（`SqlGuard` 白名单 / `SET TRANSACTION READ ONLY` / 只读账号）+ 前端对话式问数页（SQL 可审计、表格与柱状 / 折线 / 饼图切换、CSV 导出）；同时交付 **一键演示数据**（`tools/seed-demo.ps1`，幂等可清理）。

**部署（X.9 自动部署已完成）**：`qiaomes-api:latest` / `qiaomes-web:latest` 已推 TCR 并部署到阿里云 —— `http://139.196.195.44:8090` 与 `https://mes.qiaoqiaoqiao.me`（当前版本 = v3.0 阶段 6）。CI/CD 已就绪：push `main` 自动「测试 → 构建推镜像 → 云助手部署 → 健康检查 + 公网冒烟」，见 `.github/workflows/deploy.yml` 与 `docs/CICD.md`（需在仓库配 Secrets：`ALIYUN_AK/SK/INSTANCE_ID` + 镜像仓凭证 + `JWT_SECRET_KEY`）。

**阶段 7 已接入（试验）**：**AI 自迭代**（改进建议 → AI 评审与交叉对比 → 周五冻结审阅 → 周六自动执行并上线）。宿主只做入口、权限闸门与密钥代持，业务数据全在独立的 AI 迭代服务里；配置与排错见 `docs/ITERATION.md`。

**下一步**：横切改进池（认证增强、审计日志、i18n、PDA 扫码）与 SMT 特色线（上料防错、钢网锡膏时效管控）；运维侧只剩一条待办 —— 把 `ALIYUN_AK/SK` 换成 RAM 子用户（`5432` 已收回 `127.0.0.1`；生产默认密钥已从 `appsettings.Production.json` 移除并加了启动体检）。

---

## 阶段 1 · 工程化收口（M1）

**目标**：把骨架级的"能跑"提升到"能长期演进"——授权真正生效、查询不炸内存、写入有事务边界、有测试和 CI。

**前置依赖**：无。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 1.1 | 全局异常处理 + 统一错误响应 | ① 未捕获异常一律返回 `application/problem+json`，含 `traceId`，**不泄漏堆栈**；② 所有 Controller 不再手写 `BadRequest(new { error, message })`，业务失败经统一映射返回对应状态码（400/404/409/500）；③ 生产环境响应体不含 Exception 详情 | [x] |
| 1.2 | 结构化日志 | ① 请求/响应带 `TraceId`、耗时、状态码；② 用 `CorrelationId` 贯穿同一次请求的所有日志；③ 日志可输出为 JSON，便于采集 | [x] |
| 1.3 | RBAC 权限落地 | ① 新增权限目录 `Permissions`（常量）+ 角色-权限持久化（`role_permissions` 表）；② 支持 `[HasPermission("workorders:write")]` 声明式授权，由 `IAuthorizationPolicyProvider` 动态生成策略；③ JWT 内含 `permission` claims，且**服务端每次请求以 DB 权限为准**（防令牌越权）；④ 每个写接口都有权限约束，`operator` 不能建单/取消单 | [x] |
| 1.4 | 角色与用户管理接口 | ① `GET/POST/PUT/DELETE /api/roles`、`GET/PUT /api/roles/{id}/permissions`；② `GET /api/users`、`PUT /api/users/{id}/roles`、`PUT /api/users/{id}/status`；③ 管理接口仅 `admin` 可访问；④ 前端"用户管理""角色管理"两个页面可完成全流程操作 | [x] |
| 1.5 | 查询下推数据库 | ① 工单列表**不再**先 `GetAllAsync()` 再内存分页；② 分页/筛选/排序全部生成 SQL（`LIMIT/OFFSET`），用 `EXPLAIN` 验证无全表加载；③ 单页 20 条时 SQL 只返回 20 行 + 1 次 `COUNT` | [x] |
| 1.6 | 工单号并发安全 | ① 单号由数据库原子生成（日序列表 `INSERT ... ON CONFLICT DO UPDATE ... RETURNING`），不再用 `COUNT()+1`；② 并发压测 50 个请求创建工单，**0 冲突、0 重复**；③ 保留 `work_orders.order_number` 唯一索引作为最后防线 | [x] |
| 1.7 | 跨模块事务原子化 | ① 一次 HTTP 请求内多个 `DbContext` 的变更**在同一事务**内提交，任一失败全部回滚；② 事务提交成功后才发送 SignalR 通知（不出现"通知已发但数据回滚"）；③ 有集成测试覆盖"跨模块写入中途失败 → 双方均回滚" | [x] |
| 1.8 | 自动化测试 | ① 建 `tests/` 测试项目，`dotnet test` 全绿；② 覆盖：`WorkOrder` 状态机全部分支、工单号生成、权限判定、`Result`→HTTP 状态码映射；③ 行覆盖 ≥ 60%（阶段 1 只要求核心域与横切） | [x] |
| 1.9 | CI 流水线 | ① GitHub Actions 在 PR 与 main 上执行 `dotnet restore/build/test`；② 构建失败或测试失败会阻止合并；③ 工作流包含前端 `npm ci && npm run build` 校验 | [x] |
| 1.10 | 健康检查与可观测性入口 | ① `/health/live`、`/health/ready`（含 PostgreSQL 连通性探针）；② Docker Compose 与 K8s 探针可复用该端点 | [x] |

### 阶段 1 实施记录（2026-09-15）

| 项 | 实际落地情况 |
|---|---|
| 异常处理 | `GlobalExceptionHandler`（`IExceptionHandler`）+ `ApiResults` 统一映射；开发环境附带堆栈，生产环境仅 `traceId` |
| 日志 | `RequestLoggingMiddleware` 输出方法/路径/状态码/耗时/TraceId/用户，并回写 `X-Trace-Id` 响应头；生产环境切 JSON 控制台格式 |
| 权限模型 | 权限目录 `Permissions`（反射收集）+ `PermissionCatalog`（界面元数据，有测试保证一一对应）；`role_permissions` 表；`HasPermissionAttribute` + `PermissionPolicyProvider` 动态策略；`PermissionAuthorizationHandler` 按用户查库判定（带 30s 缓存 + 代次失效），**不信任令牌中的权限声明** |
| 越权防护 | 用户被停用 → 权限解析返回空集合，在线令牌立即失效；角色权限变更 → 代次 +1 使缓存立即失效 |
| 分页下推 | `WorkOrderQuery` / `UserQuery` 描述查询意图，仓储生成单条 SQL（`ILIKE` + `OFFSET/LIMIT` + `COUNT`），并转义 LIKE 通配符 |
| 单号生成 | `work_order_daily_sequences` 表 + `INSERT ... ON CONFLICT DO UPDATE ... RETURNING`（与业务写入同事务）；集成测试并发 10 次创建，0 重复 |
| 事务边界 | 所有模块共用同一 `DbConnection`，`UnitOfWorkFilter` 为每个请求开启单事务，并在提交后才执行 `IPostCommitActions`（SignalR 通知） |
| 测试 | `tests/QiaoMES.Domain.Tests`（22 个，纯领域/权限目录）、`tests/QiaoMES.Api.Tests`（11 个，真实 HTTP 管道 + 真实 PostgreSQL，含跨模块事务提交/回滚验证） |
| 踩坑记录 | EF 对「通过导航集合发现、主键已有值」的子实体会判定为 `Modified`（生成 UPDATE → `DbUpdateConcurrencyException`）。解决方案：子实体一律显式 `Add`（`AddPermission` / `AddRoleLink` / `AddReport`），并对已跟踪实体不再调用 `Update` |

**里程碑 M1 出口条件**：阶段 1 全部 DoD 勾选 ✅ → 打 tag `v0.2.0`。

---

## 阶段 2 · MES 核心域（M2）

**目标**：让项目从"工单 CRUD 演示"变成真正的 MES——有主数据、有工序、有工序级报工。

**前置依赖**：阶段 1 完成（尤其 1.5 查询下推、1.7 事务）。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 2.1 | `MasterData` 模块 | 产品（Product）✅、物料（Material）✅、工序（Operation）✅、工作中心（WorkCenter：产线/单元/工位/设备）✅、BOM（含明细与损耗率）✅、工艺路线（Routing + RoutingStep，含质检点）✅ **全部完成** | [x] |
| 2.2 | BOM 与工艺路线版本化 | ① 同一产品多版本、仅一个生效版本 ✅（激活新版本时自动失效旧版本）；② 生效版本不允许删除 ✅；③ 版本号在产品内唯一 ✅；④ 工单下达时**快照**所用 BOM/工艺路线版本 ✅（`routingId/routingVersion/bomId/bomVersion` 落库，后续主数据变更不影响在制工单） | [x] |
| 2.3 | 工单按工艺路线展开 | ① 下达自动生成工序任务 ✅（顺序、工作中心、工序编码/名称、标准工时、计划数量全部快照）；② 越序报工被拒绝（前序工序必须已完成）✅；③ 工单完成数取最后一道工序良品数 ✅，并按工序标准工时**加权计算整体进度** ✅ | [x] |
| 2.4 | 工序级报工 | 工序 ✅、数量 ✅、良品/不良/报废 ✅、不良代码 ✅、操作员 ✅、**实际工时** ✅、**执行设备** ✅、**返工类型** ✅、开始/结束时间 ✅ | [x] |
| 2.5 | 在制品（WIP）与过站 | ① SN 批量生成 + 进站/出站（`serial_numbers` + `wip_trackings`）✅；② 不合格停留在本工序、合格且为最后一道工序则整颗完工 ✅；③ 任意 SN 可查当前工序、完整历史轨迹、操作人与设备 ✅ | [x] |
| 2.6 | 主数据管理界面 | 产品/物料/工序/工作中心/BOM/工艺路线 CRUD 页面 ✅；工单页产品下拉 + 工序报工对话框 ✅；**SN 过站页面** ✅；**CSV 导入导出**（按编码 upsert）✅ | [x] |

**里程碑 M2 出口条件**：能完整走完"建产品 → 建 BOM/工艺路线 → 下工单 → 展开工序 → 逐工序报工（含不良）→ 入库完工"，且全程可追溯。 ✅ **2026-09-15 达成**

---

## 阶段 3 · 质量 / 设备 / 追溯（M3）

**目标**：补齐制造现场三大支柱——质量管控、设备管理、全链路追溯。

**前置依赖**：阶段 2 完成。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 3.1 | `Quality` 模块 | ① IQC / IPQC / FQC / OQC 检验单 ✅（`IQC-20260915-0001` 数据库原子编号）；② 检验项支持定性判定与**定量规格上下限自动判定** ✅；③ 抽样标准 AQL（Ac/Re）✅；④ 判定与结论（合格/不合格/让步接收）✅；⑤ **前端「检验单」页**（建单 / 录结果 / 判定 / 按 SN 查历史）✅ | [x] |
| 3.2 | 不良与维修闭环 | ① 不良代码库 CRUD + **不良 Pareto 统计** ✅；② 不合格品处置（返工/返修/让步接收/报废/退货）✅；③ 维修记录 → 待复检 → 复检合格关闭 / 不合格退回处理中 / 兜底报废 ✅；④ SN 绑定 + 按 SN 查检验历史 ✅；⑤ 检验判定不合格可**自动派生处置单** ✅；⑥ **前端「不合格处置」「来料批次」页**（含 NCR 闭环操作与批次正反向谱系查询）✅ | [x] |
| 3.3 | SPC 基础 | ① 按检验项汇总历史数值 ✅；② 返回均值 / 标准差 / ±3σ 控制限 ✅；③ 判异：单点超控制限 + **连续 7 点同侧** ✅；④ **前端「SPC 控制图」页** ✅：检验项下拉（`GET /api/quality/spc/items`，不用猜名字）+ 均值线 / ±3σ 控制限 / 规格上下限四线叠加 + 判异结论横幅 + 超限点标红（纯 SVG，不引图表库） | [x] |
| 3.4 | `Equipment` 模块 | ① 设备台账（编号 / 型号 / 序列号 / 所属产线 / 工作中心 / 状态）✅；② 状态机（运行/待机/故障/保养/离线）+ **故障必须填停机原因码** + 累计停机时长与状态轨迹 ✅；③ 点检 / 保养 / 维修记录（含异常描述）✅；④ 设备状态汇总与**停机原因 Pareto** ✅；前端页面 ✅ | [x] |
| 3.5 | Andon 与实时告警 | ① 设备故障 / 质量异常 / 缺料一键呼叫（数据库原子编号 `ANDON-yyyyMMdd-0001`）✅；② 流转 待响应 → 已响应 → 已解决 → 已关闭 ✅；③ **超时未响应由后台任务自动升级为红灯**（30 秒扫描）✅；④ SignalR `/hubs/andon` 实时推送看板 ✅；⑤ **车间大屏 `/display`**：三屏轮播（Andon 红黄绿看板 / 产量与良率 / 质量与设备），超时红灯闪烁、10 秒轮播、30 秒刷新、一键全屏 ✅ | [x] |
| 3.6 | 追溯与谱系 | ① 按 SN 输出**「人机料法环」完整报告**（产品身份 / 工艺路线快照 / 过站轨迹含设备 / 上游来料批次 / BOM / 检验与处置记录）✅；② **批次影响范围**（工单下全部 SN 状态汇总 + 明细，可一键跳转单颗追溯）✅；③ **来料批次 ↔ SN 谱系已打通**：批次入库 → IQC 判定自动回写批次准入 → SN 绑定用料（幂等扣减余量）→ 正向「SN ← 批次」与反向「批次 → 受影响 SN」双向追溯 ✅；④ 报告由组合根聚合各模块只读服务，不重复实现业务逻辑 ✅ | [x] |

**里程碑 M3 出口条件**：任取一个成品 SN，可一键导出完整"人机料法环"追溯报告。

---

## 阶段 4 · 报表与集成（M4）

**目标**：让数据产生决策价值，并打通上下游系统；同时为拆分微服务铺好通信基础。

**前置依赖**：阶段 2、3 完成。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 4.1 | 指标体系 | ① **OEE = 可用率 × 性能 × 良率**（计划时间按班次 × 日历推算 / 可用率扣设备故障停机 / 性能按工序标准工时与实际工时 / 良率按 SN 完工与报废）✅；② 达成率（工单计划 vs 完工）✅；③ 直通率 FPY、一次合格率 ✅；④ 不良代码 TOP N ✅；⑤ 停机 Pareto（时长 + 次数）✅；⑥ 产能 / 工时对比（理论 vs 实际）✅ | [x] |
| 4.2 | 报表与看板 | ① 指标可按**生产日区间 + 产线**下钻，并按**班次逐档汇总**（跨天夜班归属其生产日）✅；② 车间大屏（Andon + 产量 + 良率）自动轮播 ✅；③ **导出 CSV**（UTF-8 BOM，Excel 直开）：OEE / 班次 / 质量 / 达成 / 停机五类 ✅；④ **PDF 导出**：服务端输出自包含打印视图 `/api/reports/print-html`（A4 横向 + `display:table-header-group` 跨页重复表头），前端带 JWT 取回 HTML 后写入新窗口唤起打印「另存为 PDF」——**刻意不引入 PDF 生成库**，中文字体嵌入与分页交给浏览器 ✅ | [x] |
| 4.3 | 班次与日历 | ① 班次定义（代码 / 名称 / 起止时间 / 产线级或全局 / 排序 / 启停）✅；② **跨天夜班**自动识别并归属「开始日」为生产日 ✅；③ 节假日与调休日历，非生产日不计入计划生产时间 ✅；④ 未配置班次时退化为自然日全天，报表口径不中断 ✅ | [x] |
| 4.4 | 对外集成 | ① **开放 API v1**（`/api/open/v1`）**独立鉴权**：`X-Api-Key` 方案与 JWT 完全分离（仅存 SHA-256 摘要 + 前缀，明文只在创建时返回一次，可单独停用 / 设过期）✅；② **按密钥限流**（固定窗口 120 次/分钟，超限 429；无密钥按来源 IP 分片）✅；③ **ERP 工单下发 + 回读**（`POST/GET /api/open/v1/work-orders`，以工单号为**幂等键**：重复推送返回既有工单、不重复建单）✅；④ **设备数据采集接入**：MQTT / OPC UA 网关适配后上报 `equipment-telemetry` → 落 Outbox → 设备模块异步应用状态机（服务器侧无需引入 broker）✅；⑤ **物料 / BOM 交换与产品编码映射已补齐**：`GET /api/open/v1/products | materials | boms`（编码映射与对账）、`POST materials`（**幂等键 = 物料编码**）、`POST boms`（**幂等键 = 产品 + 版本**，明细含用量 / 单位 / 损耗率）✅ | [x] |
| 4.5 | 模块间事件化 | ① 集成事件契约统一在 `BuildingBlocks/Shared/IntegrationEvents`（基类自带 `EventId` / `OccurredAt`；**只新增事件、不改旧字段语义**即版本兼容策略）✅；② **Outbox**（`infrastructure.outbox_messages`）：事件与业务数据**同一事务落库**（由工作单元统一提交），后台分发器异步投递、失败**指数退避重试**（2s→…→10min，8 次后转 `Failed` 待人工）✅；③ 已落地跨模块场景：**检验判定不合格 → 自动在设备模块发起 Andon 红灯呼叫**（质量模块无需反向依赖设备）✅；④ 顺手修复工作单元缺陷：EF 的 `AddDbContext<T>` 不注册 `DbContext` 基类，导致 `UnitOfWorkFilter` 收集不到上下文、事务空转 → 补 `RegisterDbContexts()` 后**跨模块单事务真正生效** ✅ | [x] |
| 4.6 | 性能与容量 | ① **压测工具链**：`tools/perf-probe.ps1` + `/api/performance/*`（种子生成 / 索引体检 / EXPLAIN ANALYZE / 并发基准 P50·P95·P99），一键复现 ✅；② **实测**（SN 100 万行 / 检验单 20 万行，200 次 × 8 并发）：点查类（SN、检验历史、Outbox 轮询、Andon 轮询）**P95 ≤ 2 ms 远优于目标**，区间聚合 108~384 ms **未达标** ✅；③ 报告 `docs/PERFORMANCE.md` 给出判定、根因（明细表区间聚合随数据量线性劣化，**不是缺索引**）与优化路径（预聚合日/班次汇总表 > 窄窗口 > 分区与保留 > 只读副本）✅；④ 看板/报表已走**只读投影 + 单接口聚合**，只读副本接入位已留 ✅；⑤ **预聚合汇总表已落地**：新增 `reporting.daily_shift_metrics`（聚合键 = 生产日 + 班次 + 产线），报表/看板改读汇总表，**P95 1479 ms → 16 ms（约 92 倍）**，未完整覆盖时自动回退实时聚合；后台 `MetricsAggregationWorker` 每 5 分钟滚动重算「昨天 + 今天」（成本与历史量无关），历史由 `POST /api/reports/rebuild-metrics` 补齐 ✅；⑥ **1000 万行复测完成**：实时聚合 P95 **8316 ms** → 读汇总表 **24 ms（346 倍）**，且读耗时几乎不随数据量增长（100 万行 16 ms → 1000 万行 24 ms）✅ | [x] |

**里程碑 M4 出口条件**：ERP 能通过 API 推工单并回读进度；车间大屏稳定运行 7×24 无人工干预。

---

## 阶段 5 · 微服务化（M5，可选）

**目标**：在模块边界已被证明稳定、且**确有独立伸缩需求**后，按模块拆分为微服务。

**前置依赖**：阶段 4 完成（尤其 4.5 事件化 + Outbox）。

> ⚠️ 前置判断：若单体未出现"某模块需独立扩容/独立发布/独立技术栈"的真实诉求，**不要拆**。拆分是成本而非成就。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 5.1 | 数据库拆分 | 每模块独立库（或独立 schema + 独立账号），跨库只允许通过事件/API 访问 | ⛔ 不做 | **按本阶段前置判断主动放弃**：当前无「某模块需独立伸缩 / 独立发布 / 独立技术栈」的真实诉求，拆分只增成本。各模块已是独立 schema（`production` / `quality` / `equipment` / `reporting` / `infrastructure` / `integration`），**边界已就位，需要时再拆库成本很低**。⚠️ 一旦拆库，当前依赖「共享连接 + 工作单元单事务」的原子性必须改为 Outbox + Saga（4.5 已备好 Outbox 地基） |
| 5.2 | 服务拆分 | 按模块独立进程/独立容器，各自独立发布与扩容 | ⛔ 不做 | 同上：无真实诉求。模块间已通过**集成事件（Outbox）**解耦，同步调用只剩组合根（`TraceabilityController` / `DashboardController` / `OpenApiController`）的只读聚合，将来按模块切进程时改动面可控 |
| 5.3 | API 网关 | YARP 网关统一入口：路由、鉴权、限流、熔断 | [ ] 延后 | 单进程部署下网关无收益（多一跳、多一处配置）。**开放 API 的鉴权与限流已在应用内实现**（`X-Api-Key` + 按密钥分片限流），网关真正需要时再前置即可 |
| 5.4 | 服务通信 | 服务间 gRPC/HTTP + 事件总线（RabbitMQ/Kafka），契约版本兼容策略明确 | [ ] 延后 | 事件契约已版本化（§4.5：只新增事件、不改旧字段语义）✅；事件投递当前是**进程内 Outbox + 后台分发器**，语义与消息队列一致（至少一次 + 幂等消费），换 RabbitMQ 只需替换分发起 |
| 5.5 | 分布式一致性 | Saga/编排式补偿 + 幂等消费 + 死信队列；关键链路有对账任务 | [ ] 部分 | **幂等消费已具备**（Outbox 至少一次投递 + 处理幂等 + 无订阅者直接消费）；Saga 与死信队列待有跨服务事务需求时再做；当前单事务内跨模块写入无需 Saga |
| 5.6 | 可观测性 | 分布式链路追踪（OpenTelemetry）+ 集中日志 + 指标告警 | [x] 务实版 | ① **请求日志 + TraceId 贯穿**（`RequestLoggingMiddleware` + ProblemDetails 回传 `traceId`）✅；② **运维快照 `/api/monitoring/snapshot`**（零外部依赖）：Outbox 积压 / 最老待投递时长 / `Failed` 数、预聚合新鲜度、数据库连接数与库体积，**并随快照返回建议告警阈值**，便于脚本化巡检 ✅；③ 健康检查分区 `/health`（含数据库探针）✅；④ 接入 OTel Collector / Prometheus 需引入 5+ 包与外部采集器，**留待有集中监控设施时再做**，指标定义可直接复用本快照 |

**里程碑 M5 出口条件**：单模块可独立部署、独立扩容、独立回滚，且全链路可追踪。

---

## 阶段 6 · 智能问数（M6）

**目标**：让不懂 SQL 的人也能直接问数据——「自然语言 → 只读 SQL → 结果与图表」，
且**数据安全是结构性的、而不是靠提示词祈祷**。

**前置依赖**：阶段 2、3、4 完成（有真实业务数据与指标口径可问）。

> 核心判断：Text-to-SQL 的效果上限不在模型，而在**语义层**。
> 把 `"Status" = 3` 翻译成「已完工」、把 `CompletedSn` 翻译成「完工 SN 数」，
> 模型才可能写对 WHERE 与 GROUP BY。因此本项目把语义层做成一等公民（可维护、可查看、可测试）。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 6.1 | 语义层 | ① 覆盖 19 张核心表（工单 / 工序任务 / 报工 / SN / 过站 / 产品 / 物料 / 工作中心 / 检验单 / 检验项 / 处置单 / 不良代码 / 来料批次 / 设备 / 停机轨迹 / Andon / 班次 / 日历 / 班次指标）✅；② 每列带中文业务名，状态列带**枚举取值**（`0=草稿,1=已下达…`）✅；③ 词汇表给出良率 / FPY / 达成率 / OEE 口径与班次规则 ✅；④ **列名与类型实时读 `information_schema`**，与业务注解合并——表结构变了不会讲错列名 ✅；⑤ `GET /api/assistant/schema` 可查看真正喂给模型的文本 ✅；⑥ 带缓存（默认 10 分钟）✅ | [x] |
| 6.2 | NL2SQL | ① 走 OpenAI 兼容 `/chat/completions`，DeepSeek / 通义 / Kimi / Ollama 换配置即可 ✅；② 强制 JSON 输出（`sql` / `chart` / `xField` / `yField` / `explanation`），宽松解析（容忍代码块包裹）✅；③ **失败自我修复**：护栏拒绝或数据库报错都把原因回灌模型重写，默认 2 轮 ✅；④ 支持追问（带上一轮问题与 SQL）✅；⑤ 未配置模型时**优雅降级**（明确提示 + 指向文档），不抛 500 ✅ | [x] |
| 6.3 | 只读安全 | ① **第 1 道**：`SqlGuard` 纯逻辑护栏（必须单条 SELECT/WITH + 关键字黑名单 + 敏感 schema + 危险函数 + 禁注释 / 多语句 / 行级锁 + 强制外包 LIMIT），**25 个单元测试**覆盖 ✅；② **第 2 道**：独立连接 + `SET TRANSACTION READ ONLY` + `SET LOCAL statement_timeout`，绕过护栏也写不进 ✅；③ **第 3 道**：可配置独立只读账号（文档给出建账号 SQL），前端状态标签区分两种模式 ✅；④ 结果集上界（默认 200 行）+ 截断标记 ✅ | [x] |
| 6.4 | 前端问数页 | ① 对话式提问 + 示例问题一键试 ✅；② 生成的 SQL 可展开、可复制（**不做黑盒，可人工对账**）✅；③ 表格 / 柱状 / 折线 / 饼图四视图切换（纯 SVG + conic-gradient，**不引图表库**）✅；④ CSV 导出（带 BOM，Excel 直开）✅；⑤ 结果元信息：行数 / 耗时 / 是否截断 / 修复轮次 / 模型名 ✅；⑥ 追问上下文开关 ✅ | [x] |
| 6.5 | 演示数据 | ① `tools/seed-demo.ps1` 一条命令灌完整 SMT 场景（3 产线 / 3 产品 / 8 工单 / 300 SN / 1800 过站 / 8 IQC+30 IPQC+6 FQC / 15 设备含停机历史 / 6 Andon）✅；② **幂等**：反复执行只重建，不叠加 ✅；③ **可精确清理**：全部以 `DEMO-` 前缀标识，清完连带重算预聚合（不留"派生数据比明细还旧"的坑）✅；④ **时间线铺开**：把时间戳散到最近 N 天并落在班次窗口内，报表 / SPC / OEE 立刻有横轴 ✅；⑤ 默认仅 Development 放行，可显式开 `DemoData:Enabled` ✅ | [x] |
| 6.6 | 权限与审计 | ① 新增 `assistant:read`（看状态与语义层）与 `assistant:ask`（发起提问，消耗模型额度），默认授予 `admin` / `supervisor` ✅；② 每次成功问数记日志（问题 / 行数 / 耗时 / 修复轮次）✅ | [x] |

**里程碑 M6 出口条件**：给一份中文业务问题清单，问数页能答出与手写 SQL 一致的结果，且**任何写操作都被拦下**。✅ **2026-09-16 达成**

**实测记录**（本地 Docker + 演示数据）：

| 项 | 结果 |
|---|---|
| 语义层规模 | 19 张表 / 11,918 字符提示词，缓存 10 分钟 |
| 演示数据生成耗时 | 默认规模（8 工单 / 300 SN / 1786 过站 / 44 检验单）**2.8 秒**，单事务 |
| 未配模型时的行为 | `answered=false` + 明确提示，**不触库、不报 500** |
| SPC 控制图 | 30 个点算出均值 0.1039 / σ 0.0101 / UCL 0.1342 / LCL 0.0736，并正确报出判异 |
| 数据集时间铺开 | 300 颗 SN 覆盖 30 个生产日，预聚合重算 90 个班次窗口 |

### 阶段 6 的已知边界（刻意不做）

| 项 | 说明 |
|---|---|
| 不生成写操作 / 不自动建视图 | 问数只读。想固化某个查询就写成正式接口或视图，而不是让模型每次现编 |
| 不做多步 Agent | 当前是「一问一 SQL」，可控、可审计、成本可预测。真要 Agent 化，`IAssistantService` 换编排即可，语义层 / 护栏 / 执行器都能复用 |
| 不绕过软删除 | 语义层规则强制 `AND "IsDeleted" = false`；生成的 SQL 可见，能人工核对 |
| 不托管密钥 | `Assistant:Llm:ApiKey` 走配置/环境变量；生产建议换只读账号 + RAM 子用户级别的密钥管理（与既有部署约定一致） |

---

## 阶段 7 · AI 自迭代（改进建议 → 迭代计划 → 自动执行）· 试验

**目标**：把「提改进建议 → 评审排期 → 改代码 → 跑测试 → 上线」这条链本身自动化，用来迭代 QiaoMES 自己。

**分工（刻意的取舍）**：QiaoMES 只做 **入口 + 权限闸门 + 密钥代持**；建议、迭代计划、审阅留痕、执行审计
全部在**独立的 AI 迭代服务**里（私有仓 `qiao33128/ai-iteration`）。宿主不存一份 —— 再存一份必然出现两份真相；
迭代服务的管理员密钥由宿主服务端代持、**不下发浏览器**。

**前置依赖**：阶段 6（AI 接入的编排与「配置/环境变量」约定）与 X.9（自动部署，自动执行最终要靠它上线）。

| # | 任务 | 验收标准（DoD） | 状态 |
|---|---|---|---|
| 7.1 | 权限与权限闸门 | ① 新增 `iteration:suggest`（提「修改现有功能」的建议，主管默认持有）/ `iteration:manage`（提「新增功能」建议 + 审阅计划，仅 `admin`）；② 「只能对自己可用的功能提建议」**由服务端按该页面的权限码强制**（复用 `perm:` 动态策略），前端下拉只是可选项、不是安全边界 | [x] |
| 7.2 | 宿主侧客户端 | ① `IterationClient` 只做转发 + `X-Admin-Key` 代持 + 独立超时（默认 180s，提建议要调大模型做评审与一致性检查）；② 失败一律用 `IterationResult` 表达（**不抛异常**），调用方能区分「服务没配 / 服务不可达 / 服务说不行」并给出不同提示 | [x] |
| 7.3 | 前端「改进建议」页 | ① 展示当前周期与本期计划（风险标签 / 影响面 / 风险依据）；② **冲突与歧义当场列出**（未裁决前该条目不自动执行）；③ 提建议表单（类型 + 针对功能 + 标题 + 详细说明）；④ 管理员可批准 / 否决 / 提意见（提意见即本期顺延）并手动推进周期 | [x] |
| 7.4 | 周期编排 | 周一 ~ 周四提建议（AI 评审 + 与已有计划交叉对比，干净才合并）；**周五 00:00 冻结收集**并留一整天审阅；周五 24:00 结算（**低风险沉默即放行，中 / 高风险沉默即顺延**，有意见或报过冲突的一律顺延）；**周六 08:00 自动执行** | [x] |
| 7.5 | 安全边界 | ① 自动合并只做**快进式推送、绝不 `--force`**（`main` 这期间前进过就如实报错转人工）；② 判据硬性：编译通过 + **全量测试全绿** + 无越界改动（超出声明影响面即失败）+ 无残留未提交文件；③ 执行环境与生产**在资源上隔离**（容器限内存 / CPU + 独立 swap） | [x] |
| 7.6 | 部署配置透传 | ① `docker-compose.yml` / `docker-compose.deploy.yml` 均透传 `Iteration__BaseUrl` / `Iteration__AdminKey`（本地与线上一致，否则本地开发配不了这一项）；② CI 侧走仓库 Variables（`ITERATION_BASE_URL`）/ Secrets（`ITERATION_ADMIN_KEY`），`gh_deploy_aliyun.py` 的 `ENV_KEYS` 已纳入**合并写入**；③ **两个都留空即功能关闭**（页面给明确提示，不报错）；④ 配置来源与排错见 `docs/ITERATION.md` | [x] |

**已知限制**：① 执行跑在 2 vCPU / 896MB + swap 的机器上，**慢**（首次还要冷 NuGet 缓存，之后走持久化缓存）；
② 规划器看不到代码仓库，`impact.files` 有时为空，会让「越界校验」对那类条目不生效。

> 📖 配置填在哪里 / 报错对照表 / 权限 / 周期节奏见 **`docs/ITERATION.md`**。

---

## 横切改进池（随时可做，不属于阶段）

| # | 任务 | 说明 | 状态 |
|---|---|---|---|
| X.1 | 认证增强 | RefreshToken、登出黑名单、密码修改/重置、登录失败锁定、密码复杂度策略 | [ ] |
| X.2 | 审计日志 | 谁在何时改了什么（变更前后值），关键实体全覆盖 | [ ] |
| X.3 | 数据字典与国际化 | 枚举/状态字典可配置；前端 i18n（zh-CN / en） | [ ] |
| X.4 | API 版本化 | `/api/v1/...`，旧版本兼容期策略 | [ ] |
| X.5 | 权限粒度细化 | 从"功能级"细化到"数据级"（本人/本班组/本产线） | [ ] |
| X.6 | 移动端 / PDA | 扫码报工、扫码过站（SMT 现场刚需） | [ ] |
| X.7 | 消息通知 | 钉钉/企业微信/邮件 通知渠道抽象 | [ ] |
| X.8 | 多租户 | 多工厂/多组织隔离 | [ ] |
| X.9 | **自动部署（CI/CD）** | push `main` → 测试 → `docker buildx` 构建镜像并推仓 → 阿里云**云助手 RunCommand** 在服务器上 `compose pull && up -d --force-recreate` → 容器 IP 健康检查 + 公网冒烟；**服务器侧不构建**（1GB 内存构建必 OOM），走 HTTPS OpenAPI 绕开公司禁 SSH；镜像 tag = git SHA，`workflow_dispatch` 输入旧 SHA 即可回滚。见 `.github/workflows/deploy.yml` + `deploy/gh_deploy_aliyun.py` + `docs/CICD.md` | [x] |

---

## 差异化方向：SMT 特色线（建议主线）

本项目不追求做成"又一个通用工单系统"。优先用 SMT / 电子组装场景做深，形成差异化：

| # | 任务 | 验收标准 | 阶段 |
|---|---|---|---|
| S.1 | 上料防错（Feeder/Reel 校验） | 扫码上料时校验"工单-BOM-站位-物料"四者一致，不符即拦截并记录 | 2 |
| S.2 | 钢网 / 锡膏 / 回温时效管控 | 锡膏回温超时、钢网使用次数超限自动拦截 | 2 |
| S.3 | 站位表（Feeder Setup）管理 | 机台站位排布可视化，换线时校验与工单一致 | 2 |
| S.4 | 不良代码按 SMT 工序预置 | 印刷/贴片/回流焊/AOI/测试 分工序的不良代码库 | 3 |
| S.5 | 产线实时看板（Andon + 产量 + 良率） | 对标工业现场大屏，5 秒级刷新 | 4 |

---

## 变更记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-09-23 | v3.7 | **分支 = 环境（`dev` 开发环境 → `main` 生产）**。此前 AI 自迭代判据一过就**快进推 main**，等于「AI 改完直接上生产」，中间没人看过真实效果。现在：① AI 只推 `dev`（`Scheduler:MergeTargetBranch` 默认 `dev`，Worker 的 `ITERATION_MERGE_TARGET` 同步；`WorkspaceProvisioner.PromoteAsync` 加**代码闸门**——目标是 `main`/`master` 直接抛错，配错环境变量也推不上去；工作区 `defaultBranch` 必须是 `dev`，否则 `dev` 领先之后推送必然 non-fast-forward）；② 新增开发环境 `docker-compose.dev.yml`（同一台服务器上的**独立栈**：容器 `qiaomes-dev-*`、库卷 `qiaomes_dev_pgdata`、网络 `qiaomes-dev-net`、端口 **8092**；**JWT 与库密码由部署脚本在服务器上生成，刻意不与生产共用**——共用 JWT 等于开发环境签发的令牌在生产有效；三个服务都有内存硬限制，它撑爆只杀自己）；③ 新增 `deploy/gh_deploy_dev.py` + `.github/workflows/deploy-dev.yml`：push `dev` → 复用 `ci.yml` 测试 → 构建推 `:dev` 镜像 → 部署开发环境。开发环境与自迭代服务**刻意不同网络**（隔离优先），因此它的「改进建议」页显示未配置属正常。④ 新增 `docs/BRANCHING.md`（分支-环境对照 / 完整流向 / AI 侧三处硬约束 / 人工同步生产的命令 / 排错表）。开发环境固定占用约 190MB，服务器 896MB 内存余量已不到 300MB —— 不用时 `docker compose -p qiaomes-dev stop` 腾出来。 |
| 2026-09-22 | v3.6 | **AI 自迭代服务收编进生产编排**。此前它是 `/root/ai-iteration/docker-compose.yml` 这个**独立项目** —— 要手工拉起来、要把 8091 暴露在公网、部署 QiaoMES 时它不会被更新。现在写进 QiaoMES 的 `docker-compose.deploy.yml` 作为 `ai-iteration` 服务，一次部署跟着 api/web 一起 pull & up。部署脚本第 5 步**幂等收编**旧栈：先 `down`（SQLite 正在写入时拷会拿到不完整的库，`-wal` 里还有未 checkpoint 的数据）→ 打 tar 备份 → `cp -a` 数据 → **校验 `aiiteration.db` 存在才继续**（否则中止，不带着空库上线）→ 旧编排改名 `.migrated`（防止又被当成第二个栈拉起来）；第 7 步用容器内的 `curl` 探它的 `/health/ready`（该端点会查一次 SQLite，所以同时是「数据卷就绪」探针）。线上**只跑规划侧**：`Scheduler__ExecutorEnabled=false` —— ⚠️ 键是 `Scheduler:`，不是代码注释里写的 `Iteration:`（用后者不生效、会静默在这台 1GB 机器上跑构建）；执行交给开发机上的 `AiIteration.Worker`（`POST /api/tasks/lease`）。模型密钥走 `.env` 的 `ITERATION_LLM_API_KEY`，首次由部署脚本从旧栈自动带入（不打印明文），与问数的 key 相互独立。CI 侧把 JWT 预检从「拦截」降级为「警告」（服务器 `.env` 里可能早有值，CI 看不到它，不该因此挡住部署；真正的兜底是编排里的 `${JWT_SECRET_KEY:?}`）。⚠️ 遗留：8091 仍对公网开放（Worker 要租任务，SSH 又被公司网络阻断），而它有 `POST /api/tasks`、`POST /api/workspaces` 这类**不带鉴权**的接口 —— 不需要 Worker 时应收回回环。 |
| 2026-09-22 | v3.5 | **迭代服务配置做成「页面上就能填」（保存即生效、密钥不进数据库、只回掩码）**：新增配置文件 `config/iteration.json`（编排把宿主目录挂到容器 `/app/config`）+ `IterationSettingsFile` + `IIterationSettingsStore` / `IterationSettingsStore`（未提供的项沿用当前值；地址留空 = 显式关停）+ 三个接口 `GET/PUT /api/iteration/config`、`POST /api/iteration/config/test`（均需 `iteration:manage`），前端「改进建议」页新增配置弹窗。要点：① 🔴 **密钥只写在服务器的配置文件里**（Linux 上 `0600`、与 `.env` 同一档保护），因此**不进数据库、也不会出现在任何 `pg_dump` 备份里**；路径可用 `Iteration:ConfigFile` 覆盖；② 文件优先、**没有文件才回落到部署配置**（appsettings / 环境变量）—— 删掉文件立刻回退，**不用重启**；③ **不缓存**（每次直接读盘）：任何缓存都会造成"手工删了 / 改了文件，页面还显示旧值"这种最难解释的现象（原先的 2 秒缓存在实测中确实出现过），而读一个几百字节文件的成本可以忽略；④ 为了让"页面改完立即生效"，`IterationClient` 改为**每次请求现读配置并现算绝对地址** —— 原先靠 `HttpClient.BaseAddress`，那是启动时定型的，会出现"页面提示保存成功、请求却还打向老地址"；⑤ `BaseUrl` 的 `null` 与 `''` 语义分开（`null`=未指定沿用部署配置，`''`=页面上显式关停），因为"地址为空 = 功能关闭"在这个功能里是一等状态；⑥ 「测试连接」只做**只读**探活（`GET /health/ready`），**刻意不校验密钥值是否一致**：迭代服务的管理员接口全是带副作用的 POST（冻结周期 / 批准条目 / 领任务），拿来做探活会真的改数据 —— 如实告诉用户"密钥对不对要用一次真实的管理员操作验证"，而不是给一个假的"全部正常"。⑦ 配套：`.gitignore` 排除 `config/`（里面有密钥）；`deploy/gh_deploy_aliyun.py` 与 `deploy/one-shot.sh` 创建该目录并 `chown 1654`（容器以非 root 运行，否则写不进去）；`tools/start-local.ps1` 本机也预建。⑧ 顺带把 `SecretProtector`（凭据加密/掩码工具）从 `Assistant.Domain` 下沉到 `QiaoMES.Shared.Security`：避免让另一个功能为一个小工具去引用业务模块；盐保持不变，已保存的密钥不受影响，原有单测一行未改即通过。<br/>**注：** 本项一度按"加密落库"实现（新增 `integration.iteration_settings` 单行表 + `IterationSetting` 实体 + EF 迁移），复核后判断"页面可改的配置落库"会把密钥从**文件权限级**保护降到**库读权限级**保护（备份 / 只读账号都会顺带带走它），用户明确要求**密钥不进库**，故改为上述文件方案，并**已把落库那套完全撤回**（`dotnet ef migrations remove` 撤销迁移，本机库的表与历史行一并清理）。<br/>**另修一个自引入的回归**：上一版把 postgres 端口从 `0.0.0.0` 收到 `127.0.0.1` 时没考虑 **Docker Desktop 只代理 `0.0.0.0` 的端口映射**，导致本机 `dotnet run` 连不上库（实测 connection refused）→ 改为 `${POSTGRES_BIND:-127.0.0.1}`：服务器保持回环（安全），本机由 `tools/start-local.ps1` 写入 `0.0.0.0`。 |
| 2026-09-22 | v3.4 | **本机一键启动 + 两个平台差异修复**：新增 `tools/start-local.ps1`（薄壳，委托 `windows-auto-update.ps1` + `-Open`），实现 `pwsh tools/start-local.ps1` 一条命令「拉镜像仓最新镜像 → 起容器 → 打开浏览器」。配套修正：① 🔴 **Docker Desktop 的宿主机路由不到容器 IP** —— 原本沿用部署脚本 `gh_deploy_aliyun.py` 的「取容器 IP 探 /health/ready」思路，那在 Linux 服务器上可行，在 Windows（WSL2 后端）必然超时，导致健康检查把**已经跑好的服务**判成失败；改为「容器 IP → 经前端端口 /health/ready → 经前端端口打 401 接口」三级回退，并把轮询从「轮数×每轮耗时」改成**墙钟预算**（原先会从 180 秒拖成 8 分钟）。② 🔴 **nginx 的 `location /` 会把未知路径回退到 index.html**，所以 `/health/ready` 在没专门代理它的镜像上**也返回 200**（内容是 SPA 的 HTML）—— 只看状态码的健康检查等于永远通过；现在校验**响应不是 text/html** 才算数，并给 `nginx.conf` 补了 `location /health/` 真正代理到 API。③ 本机 `.env` 改为**自己生成**（`WEB_PORT=8080`），不再复制服务器向的 `.env.example`（8090 端口会把本机端口悄悄改掉）；`JWT_SECRET_KEY` 优先**沿用运行中容器**的值，避免启动脚本擅自换密钥导致所有人重新登录、问数页已存模型密钥失效。④ 新增镜像仓登录步骤（密码走 stdin）。实测：一键启动 15 秒内完成、`docker ps` 显示 api/web 为镜像仓镜像、真实登录返回 29 个权限。 |
| 2026-09-22 | v3.3 | **本机（Windows + Docker Desktop）自动更新**：新增 `tools/windows-auto-update.ps1`。出发点是一个容易误解的点 —— 编排文件里的 `restart: unless-stopped` 只能让容器**用现有镜像**重新起来，**不会**去镜像仓拉新镜像，所以「重启后自动更新」必须有人做 `pull` + 重建。脚本流程：① 轮询等 Docker 引擎就绪（开机时 Docker Desktop 还在启动，这是最容易失败的一环）；② 就位 `.env`，缺 `JWT_SECRET_KEY`（或还是模板里的 `Change_Me` 占位）就生成随机串；③ 确保 external 网关网络存在；④ `compose pull` + `up -d --force-recreate --wait`；⑤ 健康检查。🔴 **刻意不碰 `POSTGRES_PASSWORD`** —— 本机库是用默认值 `qiaomes_dev` 初始化的，凭空换密码必然连不上（与 `deploy/one-shot.sh` 同一口径）。计划任务只支持 `-RegisterTask` / `-UnregisterTask` 两条命令，且必须用**登录时**触发（Docker Desktop 是用户级程序，系统启动时引擎还没起来）；脚本本身幂等，可反复执行。 |
| 2026-09-22 | v3.2 | **阶段 7 · AI 自迭代接入（试验）**：新增 `src/QiaoMES.Api/Iteration`（`IterationOptions` + `IterationClient`，只做转发 + `X-Admin-Key` 代持 + 独立超时）与 `IterationController`（`/api/iteration/*`，权限闸门 `iteration:suggest` / `iteration:manage`），前端新增「改进建议」页。周期节奏：周一~周四提建议（AI 评审 + 与已有计划交叉对比，冲突/歧义当场抛出）→ 周五 00:00 冻结 → 周五 24:00 结算（低风险沉默放行、中/高风险沉默顺延）→ 周六 08:00 自动执行并合并。**宿主不存业务数据**（再存一份必然两份真相），管理员密钥不下发浏览器。同时补齐**配置落地面**：① `appsettings.json` / `appsettings.Development.json` 补 `Iteration` 节 —— 之前只在代码里读了配置，配置样例里根本没有这个键，这是「报迭代服务还没配置却找不到地方填」的根因；② `docker-compose.yml` 补透传（此前只有部署版有，本地开发配不了）；③ CI 链路打通（`deploy.yml` 透传 `ITERATION_BASE_URL` / `ITERATION_ADMIN_KEY`，`gh_deploy_aliyun.py` 的 `ENV_KEYS` 纳入合并写入）；④ `docs/CICD.md` 的 Secrets / Variables 表补全这两个键；⑤ 新增 `docs/ITERATION.md`（配置填在哪里 / 报错对照表 / 权限 / 周期节奏）与仓库根 `.env.example`。另：**Docker 封装改进** —— 新增 `.dockerignore`（此前会把 `frontend/node_modules`（1 万+ 文件）、`src/**/bin｜obj`、`.git`、本地日志全部传给 build daemon）、`frontend/Dockerfile` 的 Node 版本由 22 对齐到 CI 的 20、`docker-compose.deploy.yml` 给 `web` 补 healthcheck（盯住「容器 Up 但端口无人应答」这类 nginx entrypoint 卡死的坑）。README 重写为目录化结构 + 新增「配置项填在哪里」总表。**另做两项安全加固**：① `appsettings.Production.json` 删掉明文的默认 `Jwt:SecretKey` 与含 `qiaomes_dev` 的 `DefaultDb` —— 放进去等于把弱口令随镜像发出去，改为**启动体检**（密钥为空直接报错并说清缺什么；仍是内置占位值则在生产打醒目警告、不阻断本地 compose 兜底）；② `postgres` 的 `5432` 端口由 `0.0.0.0` 收回 `127.0.0.1`（这台机器有公网 IP，等于把库暴露在扫描面上），容器间互连不受影响；③ 生产编排的 `Jwt__SecretKey` 由「内置兜底值」改为**必填**（`${JWT_SECRET_KEY:?}`）+ CI 预检提前拦截 —— 此前漏配 Secret 时生产会静默使用一个公开密钥，漏配也照样"部署成功"；本地 compose 仍保留兜底，保证 `docker compose up -d --build` 开箱即用。另新增 **`deploy/one-shot.sh`**（**一条命令、幂等**的服务器部署脚本：自动补齐 `.env` / 建 external 网络 / 需要时登录镜像仓 / `pull` / `up -d --wait` / 健康检查与失败提示 —— 🔴 数据库密码**只在全新库上生成**，卷已存在时绝不凭空造新密码，因为那必然连不上已初始化的库）与 **`.gitattributes`**（强制 `*.sh` / `.dockerignore` / `*.yml` 用 LF：仓库此前没有此文件而本机 `core.autocrlf=true`，CRLF 的 `.sh` 在 Linux 上会以 bad interpreter 直接失败、CRLF 的 `.dockerignore` 会让规则**静默失配**）。 |
| 2026-09-16 | v3.1 | **自动化部署上线（X.9）**：新增 `Deploy` 工作流 —— `push main` → 复用 `ci.yml` 跑测试 → `docker buildx` 构建两个镜像并推镜像仓（GHA 层缓存）→ `deploy/gh_deploy_aliyun.py` 经**阿里云云助手 RunCommand** 在服务器上执行 `compose pull && up -d --force-recreate`。要点：① **服务器侧不构建**（1GB 内存跑 .NET/Node 构建必 OOM，这也是看板站当初放弃镜像仓、改为服务器本地构建的原因，而 QiaoMES 双构建走不了那条路）；② 走 HTTPS OpenAPI，**不依赖被公司防火墙封锁的 SSH**；③ 服务器脚本 base64 下发 + `nohup` 后台执行 + 双层轮询日志（RunCommand 单次 15 分钟超时会杀掉长任务）；④ `.env` 采用**合并写入**（只覆盖本次提供的键）—— `POSTGRES_PASSWORD` 一旦被 CI 覆盖成默认值就会让 API 连不上已初始化的数据库卷，这条是硬约束；⑤ 健康检查用 `docker inspect` 取容器 IP 探 8080（aspnet 运行时镜像里没有 curl/wget，且不给宿主机加端口）；⑥ Runner 侧再做一次**公网冒烟**（登录 + `/api/assistant/status`），DNS/TLS/Caddy/应用任一层断了都能立刻发现。编排文件由 `docker-compose.tcr.yml` 升级重命名为 **`docker-compose.deploy.yml`**（registry 中立、`gateway` 网络声明为 external 以免容器重建后丢失域名反代）。 |
| 2026-09-16 | v3.0 | **阶段 6 完成（M6 交付）：智能问数（AI 接入数据库）**。新增 `Assistant` 模块（Domain/Application/Infrastructure/Api 四层齐备）：① **语义层** —— 19 张核心表的业务注解（中文名 / 枚举取值 / 指标口径），列名与类型**实时读 `information_schema`** 再与注解合并，表结构变了也不会讲错列名，带 10 分钟缓存，`GET /api/assistant/schema` 可查看；② **NL2SQL** —— OpenAI 兼容 `/chat/completions`（DeepSeek / 通义 / Kimi / 本地 Ollama 换配置即可），强制 JSON 输出 + 宽松解析，**失败把报错回灌模型自我修复**（默认 2 轮），支持追问上下文，未配模型时优雅降级不抛 500；③ **只读三道防线** —— `SqlGuard`（单条 SELECT/WITH + 关键字黑名单 + 敏感 schema + 危险函数 + 禁注释/多语句/行级锁 + 强制外包 LIMIT，**25 个单元测试**）+ 独立连接 `SET TRANSACTION READ ONLY` 与 `statement_timeout` + 可配置只读账号；④ **前端「智能问数」页** —— SQL 可展开可复制（不做黑盒）、表格/柱状/折线/饼图四视图（纯 SVG，不引图表库）、CSV 导出、行数与耗时与修复轮次元信息；⑤ 新增权限 `assistant:read` / `assistant:ask`。同时交付 **一键演示数据** `tools/seed-demo.ps1`（幂等 + `DEMO-` 前缀可精确清理 + 时间线铺开到最近 N 天 + 连带重算预聚合）与 **SPC 控制图前端**（均值/±3σ/规格限四线叠加 + 判异横幅 + 超限点标红，新增 `GET /api/quality/spc/items` 供下拉选择）。测试增至 **132 个全绿**（领域 49 + 集成 83），新增 `docs/AI-QUERY.md` |
| 2026-09-15 | v1.0 | 首次建立路线图；确定 5 阶段 + 横切改进池 + SMT 特色线 |
| 2026-09-15 | v1.1 | **阶段 1（工程化收口）全部完成并验收**：异常处理/日志/RBAC/角色与用户管理/分页下推/并发安全单号/跨模块单事务/健康检查/测试/CI；补充「阶段 1 实施记录」 |
| 2026-09-15 | v1.2 | 阶段 2 启动：新增 `MasterData` 模块（产品/物料/工序/工作中心 CRUD + 前端配置驱动维护界面 + `masterdata:read/manage` 权限）；CI 首次全绿并在 Actions v5 上验证通过 |
| 2026-09-15 | v1.3 | `MasterData` 模块补齐 **BOM 与工艺路线**（多版本、单一生效版本、生效版本保护、明细行/工序步骤、前端专用表单）→ **2.1 完成**；测试增至 46 个 |
| 2026-09-15 | v1.4 | **工单按生效工艺路线展开工序任务**（`work_order_operations`，BOM/工艺路线版本快照）+ **工序级报工**（良品/不良/报废、不良代码、操作员、越序拦截、全部工序完成自动结单）；主数据对外提供只读查询契约 `IMasterDataQueryService`；测试增至 53 个 |
| 2026-09-15 | v1.5 | **阶段 2 全部完成（M2 交付）**：报工补齐**实际工时 / 执行设备 / 返工类型**，工单按标准工时**加权进度**；新增 **SN 与 WIP 过站**（`serial_numbers` + `wip_trackings`，进站/出站/不合格停留/整颗完工/轨迹追溯）；新增**主数据 CSV 导入导出**（按编码 upsert）；前端新增 SN 过站页面、报工工时输入、主数据导入导出按钮；测试增至 62 个 |
| 2026-09-15 | v1.6 | **阶段 3 主体完成**：新增 `Quality` 模块（检验单 IQC/IPQC/FQC/OQC + 定量规格自动判定 + 不合格处置与维修/复检闭环 + 不良代码 Pareto + SPC 判异）；新增 `Equipment` 模块（设备台账 / 状态机 / 点检保养 / 停机 Pareto + Andon 一键呼叫与**超时自动升级** + SignalR `/hubs/andon`）；新增**「人机料法环」SN 追溯报告**与批次影响范围；前端新增质量管理、设备与 Andon、追溯查询页面；测试增至 80 个 |
| 2026-09-16 | v2.4 | **阶段 4 全部完成（M4 交付）**：补上最后一项 **PDF 导出** —— 服务端输出自包含打印视图 `GET /api/reports/print-html`（A4 横向、`display:table-header-group` 跨页重复表头、斑马纹、中文字体栈），前端带 JWT 取回 HTML 后写入新窗口唤起打印「另存为 PDF」，**刻意不引入 PDF 生成库**（避开字体嵌入与分页处理）；前端「导出 / 打印」下拉统一为 `export:*`（CSV）与 `print:*`（PDF）两类命令。至此 **4.1 指标体系 / 4.2 报表与看板（下钻 + CSV + PDF）/ 4.3 班次与日历 / 4.4 对外集成 / 4.5 模块间事件化 / 4.6 性能与容量 全部完成 ✅**，测试 **108 个全绿** |
| 2026-09-16 | v2.3 | **4.4 收尾 + 阶段 5 评估**：① 开放 API 补齐**主数据交换** —— `GET products / materials / boms`（编码映射与对账）、`POST materials`（幂等键 = 物料编码）、`POST boms`（幂等键 = 产品 + 版本，明细含用量 / 单位 / 损耗率）✅；② 新增**运维可观测性** `/api/monitoring/snapshot`（**零外部依赖**）：Outbox 积压 / 最老待投递时长 / `Failed` 数、预聚合新鲜度、数据库连接数与库体积，并随快照返回建议告警阈值 ✅；③ **阶段 5 评估结论**：5.1 库拆分与 5.2 服务拆分**主动放弃**（无真实独立伸缩诉求；模块已分 schema、事件已解耦，需要时成本低），5.3 网关与 5.4 消息队列**延后**（应用内鉴权限流、进程内 Outbox 语义已满足），5.5 幂等消费**已具备**、Saga 待真有跨服务事务时再做，5.6 务实版**完成**；测试增至 **107 个全绿** |
| 2026-09-16 | v2.2 | **阶段 4 · 4.6 闭环：预聚合汇总表落地**。新增 `reporting.daily_shift_metrics`（聚合键 = 生产日 + 班次 + 产线，含产量/质量/工时/停机四类分量）；`MetricsService` 的 OEE 与班次报表**优先读汇总表**，未完整覆盖范围时**自动回退实时聚合**（不给不完整报表）；新增 `MetricsAggregationWorker`（每 5 分钟滚动重算「昨天 + 今天」，跨天夜班按 `ShiftRange` 正确归属，重算成本与历史数据量无关）与 `POST /api/reports/rebuild-metrics`（历史补齐，需 `reporting:manage`）。**实测（7 天 / 100 万行 SN）：同一报表接口 P95 由 1479 ms 降到 16 ms（约 92 倍）**，OEE 接口 24 ms，均达标；一致性由测试断言「汇总行 SN 口径 = 该班次窗口明细 count」保障。测试增至 **104 个全绿** |
| 2026-09-16 | v2.1 | **阶段 4 · 4.6 性能与容量（主体完成，含实测）**：新增性能工具链 —— `tools/perf-probe.ps1`（启动 API → 造种子 → 索引体检 → EXPLAIN ANALYZE → 并发基准 → 输出报告）+ `/api/performance/*`（**仅 Development 可用**：`seed` / `index-health` / `explain` / `benchmark` / `scenarios` / `schema` / `DELETE seed` 清理）；**实测 100 万行 SN + 20 万检验单（200 次 × 8 并发）**：点查类 P95 ≤ 2 ms（SN 点查 1ms / 检验历史 1ms / Outbox 轮询 2ms / Andon 轮询 2ms），时间区间聚合 P95 108~384 ms **未达标**；根因定位为「明细表做区间聚合随数据量线性劣化」（7 天窗口命中 23% 行 → 优化器放弃索引），并为 `serial_numbers` 增加 `(CreatedAt, Status)` 索引；报告 `docs/PERFORMANCE.md` 给出判定与优化路径（**预聚合日/班次汇总表** 为首选）。另修复压测并发下共享连接报 `NpgsqlOperationInProgressException` 的问题（压测改用独立连接） |
| 2026-09-16 | v2.0 | **阶段 4 · 4.4 对外集成完成（主体）**：新增 `integration` schema 与 `api_clients` 表（密钥仅存 **SHA-256 摘要 + 可识别前缀**，明文只在创建时返回一次）；新增 **`X-Api-Key` 独立鉴权方案**（与 JWT 并存、可单独吊销/过期）与 **按密钥分片限流**（固定窗口 120 次/分钟，超限 429）；新增开放 API v1：**ERP 工单下发（工单号幂等，重复推送不重复建单）+ 进度回读 + 设备采集上报**（MQTT/OPC UA 网关适配后上报 → 落 Outbox → 设备模块异步应用状态机）；新增客户端管理接口 `/api/integration/api-clients` 与权限 `integration:read|manage`；测试增至 **102 个全绿** |
| 2026-09-16 | v1.9 | **阶段 4 · 4.5 模块间事件化完成**：新增集成事件契约层（`IIntegrationEvent` + 6 个事件 + `IIntegrationEventHandler<T>` + `IOutboxWriter`）与 **Outbox**（`infrastructure.outbox_messages`：事务内落库、后台分发器异步投递、指数退避重试、超限转 `Failed`）；首个跨模块订阅落地「**检验不合格 → 设备模块 Andon 红灯呼叫**」（含幂等判重）；🔴 **同时修复了一个隐藏的架构缺陷** —— EF 的 `AddDbContext<T>` 只注册具体类型、不注册 `DbContext` 基类，`UnitOfWorkFilter` 的 `GetServices<DbContext>()` 一直是空集合（跨模块事务空转），补 `RegisterDbContexts()` 后**真正实现一请求一事务、跨模块统一提交**；测试增至 **98 个全绿** |
| 2026-09-15 | v1.8 | **阶段 4 起步**：新增 `Reporting` 模块（schema `reporting`）—— 班次定义（支持跨天夜班，产量归属开始日）、生产日历（节假日 / 调休）、以及**其它模块的只读投影**（`ExcludeFromMigrations`，报表直接做 SQL 级聚合）；新增指标体系 `/api/reports/*`：**OEE = 可用率 × 性能 × 良率**、达成率、FPY、不良 TOP N、停机 Pareto（时长 + 次数）；新增 **CSV 导出**（UTF-8 BOM，Excel 中文字不乱码）五类报表；前端新增「报表与班次」页面（指标总览 + 班次配置 + 生产日历）；测试增至 **96 个全绿** |
| 2026-09-15 | v1.7 | **阶段 3 全部完成（M3 交付）**：① **车间大屏** `/display` —— 三屏自动轮播（Andon 红黄绿看板 / 产量与良率 / 质量与设备 + 停机 TOP5），超时呼叫闪烁、一键全屏；② **来料批次谱系** —— 批次入库 → IQC 判定**自动回写批次准入**（不合格 / 冻结批次禁止投产）→ SN 绑定用料（幂等 + 扣减余量）→ 正向「SN ← 批次」/ 反向「批次 → 受影响 SN」双向追溯；③ 新增指标聚合 `/api/dashboard/overview`（产量统计、一次合格率 FPY、良率、设备状态、Andon 快照、停机 Pareto），统计口径按本地「今日」换算 UTC 查询；④ 测试增至 **88 个全绿** |
