# QiaoMES 性能与容量报告（阶段 4 · 4.6）

> 本报告全部数字为**实测值**，不是估算。测试脚本 `tools/perf-probe.ps1` 可一键复现。

## 一、环境与方法

| 项 | 值 |
|---|---|
| 数据库 | 本机 PostgreSQL 16（开发库 `qiaomes`） |
| 应用 | `QiaoMES.Api`（Development，未启用缓存/副本） |
| 种子数据 | `production.serial_numbers` **1,000,064 行 / 248 MB**；`quality.inspections` **200,127 行 / 69 MB** |
| 压测方式 | 每场景 **200 次请求 × 8 并发**，先预热 10 次，统计 P50 / P95 / P99 / max 与吞吐 |
| 场景 | 逐个对应代码中真实的查询路径（点查 / 报表聚合 / 分发器轮询 / 大屏轮询） |

复现命令：

```powershell
pwsh -File tools/perf-probe.ps1 -SnCount 1000000 -InspectionCount 200000 -Iterations 200 -Concurrency 8
pwsh -File tools/perf-probe.ps1 -Cleanup     # 清理种子数据（只删 SN-PERF- / PERF-IQC- 前缀）
```

## 二、实测结果

### 2.1 并发基准（200 次 / 8 并发）

| 场景 | 查询路径 | P50 | **P95** | P99 | max | 吞吐 |
|---|---|---|---|---|---|---|
| `sn-lookup` | 按 SN 点查（追溯入口） | 0 ms | **1 ms** | 70 ms | 74 ms | 2213 req/s |
| `inspection-by-sn` | 按 SN 取检验历史（追溯报告） | 1 ms | **1 ms** | 6 ms | 13 ms | 6051 req/s |
| `inspection-daily` | 30 天检验单聚合（质量报表） | 60 ms | **108 ms** | 117 ms | 125 ms | 118 req/s |
| `sn-status-stats` | 7 天 SN 状态聚合（OEE / 大屏） | 159 ms | **384 ms** | 439 ms | 461 ms | 38 req/s |
| `outbox-pending` | Outbox 待投递扫描（分发器每 5s） | 1 ms | **2 ms** | 13 ms | 17 ms | 4810 req/s |
| `andon-open` | 未结束 Andon 呼叫（大屏轮播） | 1 ms | **2 ms** | 13 ms | 17 ms | 3390 req/s |

### 2.2 执行计划摘要（EXPLAIN ANALYZE）

| 场景 | 顶层节点 | 单次耗时 | 返回行 |
|---|---|---|---|
| `sn-lookup` | **Index Scan** | 0.026 ms | 1 |
| `inspection-by-sn` | Limit（走索引） | 0.046 ms | 1 |
| `sn-status-stats` | Aggregate（并行顺序扫描） | 64 ms | 4 |
| `inspection-daily` | Aggregate | 22 ms | 5 |
| `outbox-pending` | Limit（走 `(Status, NextRetryAt)` 索引） | 0.016 ms | 0 |
| `andon-open` | Limit | 0.037 ms | 20 |

### 2.3 索引与扫描统计（`index-health`）

- **点查全部命中索引**：SN 唯一索引、检验单 SN 索引、Outbox `(Status, NextRetryAt)`、Andon 状态 —— 这是 MES 最高频的三类访问（扫码过站、追溯、看板轮询）。
- **`serial_numbers` 的顺序扫描是最大热点**（`seq_scan = 2027`），来源就是报表/大屏的时间区间聚合。
- 报告中列出的 `IX_inspections_* idx_scan = 0` 属于**统计延迟**（压测刚建好索引，`pg_stat` 尚未累积），非真实未使用，判断时需以 `EXPLAIN` 为准。

## 三、结论与判定

| DoD | 判定 | 说明 |
|---|---|---|
| ① 关键查询 P95 < 200 ms | **部分达标** | **点查类（SN / 检验历史 / Outbox / Andon）P95 ≤ 2 ms，远优于目标**；**时间区间聚合未达标**（108 ~ 384 ms） |
| ② 看板刷新不影响生产写入 | **设计已就位** | 报表模块走**只读投影**（`ExcludingFromMigrations` 的读模型），与生产写入的表分离；大屏接口一次聚合（`/api/dashboard/overview`）避免多接口轮询；后续接只读副本无需改业务代码 |
| ③ 有压测报告 | **已完成** | 本文件 + `tools/perf-report.json`（含完整计划与分位数据） |

### 聚合查询为什么慢（诚实结论）

压测首轮发现 `sn-status-stats` P95 = **309 ms** 后，为 `serial_numbers` 增加了 `(CreatedAt, Status)` 索引并复测，**结果为 384 ms，未见改善**。原因是：

- 该查询窗口 7 天命中约 **23%** 的行，PostgreSQL 优化器判定**并行顺序扫描 + 聚合**比「索引扫描 + 回表」更快，因此主动放弃了新索引；
- 另外 EF 的软删除过滤器会给查询附上 `IsDeleted = false`，索引未包含该列时也无法走 Index Only Scan。

即：**这不是缺索引，而是「明细表上做区间聚合」这个用法本身随数据量线性劣化**。

## 四、优化路径（按性价比排序）

1. **预聚合日/班次汇总表**（推荐，阶段 4 收尾或阶段 5 做）：报表与大屏只读 `daily_metrics`（按生产日 + 产线 + 班次预聚合），把 100 万行聚合降为几十行查询，同时天然解决 4.6 的「看板不阻塞写入」。
2. **缩短查询窗口**：大屏默认看「当班/当日」，把 7 天窗口改为按班次窗口（`ShiftRange`），命中行数下降一个数量级，此时 `(CreatedAt, Status)` 索引即可生效。
3. **保留窗口 / 分区**：`serial_numbers` 按月分区（`CreatedAt`），配合保留策略归档历史 SN，控制单分区规模。
4. **只读副本**：报表/大屏连只读实例，物理隔离分析型负载（配置位已留：报表模块只读投影 + 独立连接）。
5. **已确认无需优化的部分**：点查、Outbox 轮询、Andon 轮询的索引与访问路径均已最优，**不要**为它们引入缓存或副本（成本高于收益）。

## 五、容量参考

| 规模 | 点查 P95 | 区间聚合 P95（每请求扫描行数） |
|---|---|---|
| 100 万行 SN（实测） | ≤ 2 ms | 384 ms（约 23 万行/次） |
| 1000 万行 SN（线性外推） | 仍 ≤ 5 ms（B-Tree 深度增长极慢） | **不可接受（秒级）** → 必须做预聚合或强制窄窗口 |

> 结论：**MES 的容量瓶颈不在「扫码过站」这类点操作，而在看板/报表的区间聚合**。这也是为什么 4.6 的最终建议是「预聚合 + 窗口约束」，而不是加缓存。
