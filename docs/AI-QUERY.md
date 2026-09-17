# 智能问数（AI + 数据库）

> 用中文提问 → 大模型生成只读 SQL → 安全执行 → 直接给结果与图表。
> 例如：「最近 7 天各产线的良率是多少」「本月不良代码 TOP 10 及占比」「停机时长最长的 5 台设备」。

这份文档回答两个问题：**代码里已经做好了什么**、**你还需要额外准备什么**。

---

## 1. 代码里已经做好的（开箱即用）

| 能力 | 说明 |
|---|---|
| 语义层 | 内置 19 张核心表的业务注解（中文名 / 枚举取值 / 业务口径）。真实列结构从 `information_schema` 实时读取，**永远不会讲错列名**，表结构变了也不用改代码 |
| NL2SQL | OpenAI 兼容端点适配层，换模型只改 3 行配置 |
| 自我修复 | SQL 执行报错会把错误回灌给模型重写，默认最多 2 轮；列名写错、漏软删除条件这类问题基本能自动纠回来 |
| 只读护栏 | `SqlGuard`：白名单（必须 `SELECT`/`WITH`）+ 黑名单（写操作 / DDL / 注释 / 多语句 / 敏感 schema / 危险函数）+ 强制外包 `LIMIT` |
| 数据库级兜底 | 每次执行都新开连接并 `SET TRANSACTION READ ONLY` + `statement_timeout`——护栏被绕过也写不进数据 |
| 结果集上界 | 默认最多 200 行，超出标记「已截断」 |
| 前端页面 | `智能问数` 页：对话式提问、生成 SQL 可展开可复制、表格 / 柱状 / 折线 / 饼图切换、CSV 导出、追问上下文 |
| 权限 | `assistant:read`（看状态与语义层）、`assistant:ask`（发起提问，消耗模型额度） |
| 可观测 | 每次成功的问数都记日志（问题 / 行数 / 耗时 / 修复轮次） |

新增文件位置：

```
src/Modules/Assistant/
├── Domain/          # SqlGuard（纯逻辑，带单元测试）、语义层模型、配置 POCO
├── Application/     # 编排：语义层 → 生成 → 校验 → 执行 → 修复
├── Infrastructure/  # 语义层提供者、OpenAI 兼容客户端、只读执行器
└── Api/             # /api/assistant/status | ask | schema
docs/AI-QUERY.md     # 本文
tools/seed-demo.ps1  # 一键灌演示数据（问数的最佳练手数据集）
```

---

## 2. 你还需要额外准备什么

一共 **4 件事**，其中第 1 件必须做，第 2 件生产环境强烈建议做。

### 2.1 一个大模型端点（必须）

#### 方式 A：页面上直接配（推荐，改完立即生效）

登录 QiaoMES → 左侧「智能问数」→ 状态卡右上角 **「模型配置」**（需要 `assistant:manage` 权限，`admin` 默认就有）：

| 字段 | 说明 |
|---|---|
| 总开关 | 关掉后问数页给出提示，不会静默失败 |
| 模型地址 | OpenAI 兼容端点，如 `https://api.deepseek.com/v1`；本地 Ollama 用 `http://host.docker.internal:11434/v1` |
| 模型名 | 如 `deepseek-chat` / `qwen-plus` / `qwen2.5-coder:14b` |
| API Key | **只以掩码回显**（`sk-a****wxyz`），明文不会通过接口返回；留空 = 不改动现有密钥 |
| 模型超时 / 返回行数上限 / SQL 超时 / 修复轮数 | 按需收紧 |

配完点 **「测试连接」**（只发一个 1 token 的请求）确认地址 / 密钥 / 模型名都对，再点 **「保存并生效」**——
**保存即生效，不需要重启、也不需要重新部署**。

> 🔐 **密钥怎么存的**：`AES-GCM` 加密后落库，加密口令由 `Jwt:SecretKey` 经 PBKDF2 派生（因此不需要额外的密钥管理服务，也不会像 DataProtection 那样因容器重建丢密钥环）。
> 接口永远只返回掩码 + 「是否已配置」。
> ⚠️ 若更换 `Jwt:SecretKey`，已保存的密钥将解不开——页面会提示「密钥已失效，请重新填写」，不会抛错。
> 数据库里存的清单里**不含明文密钥**，但仍建议把数据库备份按敏感数据处理。

配置的优先级：**页面上保存过就以数据库为准**；没保存过则沿用 `appsettings` / 环境变量。
状态卡上的提示会告诉你当前用的是哪一种（「当前配置来自数据库」/「当前仍在用 appsettings」）。

#### 方式 B：配置文件 / 环境变量（适合无人值守的部署）

问数本身不训练模型，只是调用一个「OpenAI 兼容」的 `chat/completions` 接口。三种选择：

| 方案 | 适合 | 配置 |
|---|---|---|
| **公有云 API**（DeepSeek / 通义千问 / Kimi / 硅基流动…） | 效果最好、按量付费、几分钟就能跑通 | `BaseUrl` + `ApiKey` + `Model` |
| **公司内网模型**（如果有统一网关） | 数据不出内网、可报销 | 把 `BaseUrl` 指向内网网关地址 |
| **本地 Ollama** | 完全离线、零成本、数据绝不出网 | `BaseUrl = http://host.docker.internal:11434/v1`，`Model = qwen2.5-coder:14b`，`ApiKey` 可留空 |

`src/QiaoMES.Api/appsettings.Development.json`（或环境变量）：

```json
{
  "Assistant": {
    "Enabled": true,
    "Llm": {
      "BaseUrl": "https://api.deepseek.com/v1",
      "ApiKey": "sk-你的密钥",
      "Model": "deepseek-chat",
      "TimeoutSeconds": 90,
      "Temperature": 0
    }
  }
}
```

Docker 部署时用环境变量（`docker-compose.yml` 已经预留）：

```bash
ASSISTANT_LLM_BASE_URL=https://api.deepseek.com/v1 \
ASSISTANT_LLM_API_KEY=sk-xxx \
ASSISTANT_LLM_MODEL=deepseek-chat \
docker compose -f docker-compose.deploy.yml up -d
```

> **没配 Key 会怎样？** 不会报错、不会连数据库。`智能问数` 页会显示「还没配置大模型」的黄色提示，并给出配置位置。

**选模型的建议**：优先选代码能力强的（DeepSeek-chat / Qwen-coder / GPT 系）。普通对话模型也能用，但复杂多表 JOIN 的首次成功率会低一些——好在有自我修复兜底。

### 2.2 一个只读数据库账号（生产强烈建议）

护栏是应用层的，最硬的一道防线还是「数据库账号本身没有写权限」。

```sql
-- 在 QiaoMES 所在库里执行一次
CREATE ROLE qiaomes_ai_ro LOGIN PASSWORD '换成一个强密码';

-- 只给「查」的权限，不给任何写权限
GRANT CONNECT ON DATABASE qiaomes TO qiaomes_ai_ro;
GRANT USAGE ON SCHEMA masterdata, production, quality, equipment, reporting TO qiaomes_ai_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA masterdata, production, quality, equipment, reporting TO qiaomes_ai_ro;

-- 以后新建的表也自动带上（迁移会不断加表，这句很重要）
ALTER DEFAULT PRIVILEGES IN SCHEMA masterdata, production, quality, equipment, reporting
    GRANT SELECT ON TABLES TO qiaomes_ai_ro;

-- 顺带明确：身份库一律不给
REVOKE ALL ON SCHEMA identity FROM qiaomes_ai_ro;
```

然后把连接串指过去：

```json
{
  "Assistant": {
    "ConnectionString": "Host=postgres;Port=5432;Database=qiaomes;Username=qiaomes_ai_ro;Password=强密码"
  }
}
```

留空则复用 `ConnectionStrings:DefaultDb`——功能完全正常（靠 `SET TRANSACTION READ ONLY` 兜底），
但离「数据库自己都写不进去」还差一层。页面上的状态标签会区分这两种情况：

- `独立只读连接`（绿色）= 已配置只读账号
- `复用主连接（只读事务兜底）`（黄色）= 还没配

> **为什么模型拿不到数据库连接？** 模型只收到「表结构文本」，从不接触连接串，也无法让服务端执行任意语句——所有 SQL 都先过 `SqlGuard`。

### 2.3 语义层的维护习惯（按需）

语义层的价值 = 让模型看懂 `"Status" = 3` 是「已完工」、`"CompletedSn"` 是「完工 SN 数」。
代码在 `src/Modules/Assistant/Infrastructure/Schema/SemanticCatalog.cs`，改起来是纯数据：

```csharp
["Status"] = new("状态", null, new() { [0] = "草稿", [1] = "已下达", [2] = "生产中", [3] = "已完成", [4] = "已取消" }),
```

什么时候需要动它：

- 加了新表且希望它能被问到 → 在 `Tables` 里加一条（不加则模型看不到，也就不会乱猜）
- 加了新枚举 → 补 `EnumValues`，否则模型会把数字当无意义整数
- 加了口径类字段（指标公式、班次规则）→ 补到 `Glossary` 常量里

`GET /api/assistant/schema` 可以看到当前真正喂给模型的完整文本，排查「模型为什么不懂这个字段」非常直观。

> 表结构与语义层的合并是自动的：**列名/类型永远以数据库为准**，语义层只加业务注解。
> 所以即使你忘了更新语义层，也不会出现「按不存在的列名生成 SQL」。

### 2.4 成本与额度意识（按需）

- 每次提问 ≈ 一次 `chat/completions` 调用（含语义层文本，约 4~8k tokens 输入）；失败修复会再多 1~2 次
- 语义层文本已缓存（默认 10 分钟），同一批提问不会重复拉取
- 想省钱/防滥用：
  - 只给需要的角色 `assistant:ask` 权限（默认只有 `admin` 与 `supervisor`）
  - `Assistant:Enabled = false` 可一键关停（接口返回明确提示，不会静默失败）
  - `Assistant:MaxRows` / `QueryTimeoutSeconds` 可收紧

---

## 3. 安全设计（三道防线）

```
用户问题
   │  （模型只拿到「语义层文本」，拿不到连接串）
   ▼
[1] SqlGuard 白名单/黑名单/强制 LIMIT       ← 应用层，纯逻辑 + 单元测试
   ▼
[2] SET TRANSACTION READ ONLY + statement_timeout   ← 数据库层，写操作直接报错
   ▼
[3] 只读账号（生产建议）                    ← 权限层，连表都没有写权限
   ▼
结果集（≤ MaxRows 行，超出标记「已截断」）
```

`SET TRANSACTION READ ONLY` 的含义是：**即便有人绕过前一道护栏，PostgreSQL 自己会拒绝写入并报错**。
所以「AI 把生产数据删了」这一类风险，在这里是被结构性地消除的，而不是靠提示词祈祷。

具体被拦下的东西（都有单元测试，见 `tests/QiaoMES.Domain.Tests/SqlGuardTests.cs`）：

- `INSERT / UPDATE / DELETE / DROP / TRUNCATE / ALTER / GRANT …`
- 多语句（`;` 分隔）、SQL 注释（`--` 与 `/* */`）
- `identity` / `information_schema` / `pg_catalog` / `pg_toast` schema
- `pg_sleep` / `pg_read_file` / `pg_terminate_backend` / `lo_export` / `dblink` …
- `FOR UPDATE` / `FOR SHARE` 行级锁
- 单条 SQL 超过 20,000 字符

---

## 4. 试一下就知道了

```bash
# 1. 起服务（本地构建）
docker compose up -d --build

# 2. 灌一套演示数据（3 条产线 / 8 张工单 / 400+ 颗 SN / 检验单 / 设备 / Andon）
pwsh tools/seed-demo.ps1

# 3. 配好 Assistant:Llm 的 ApiKey，登录 http://localhost:8080
#    左侧菜单 → 智能问数 → 挑一个示例问题
```

演示数据规模参考（默认参数）：

| 对象 | 数量 |
|---|---|
| 产线 / 工位 | 3 条线 + 15 个工位 |
| 产品 / 物料 / 工序 | 3 / 8 / 5 |
| 工单（草稿→已完工） | 8 张，含 40 条工序任务与报工记录 |
| SN / 过站记录 | 约 400 / 约 1800 |
| 检验单 | 8 张 IQC + 30 张 IPQC + 6 张 FQC |
| 设备 | 15 台（含停机历史与点检记录） |
| Andon 呼叫 | 6 条（覆盖待响应 / 已响应 / 已解决 / 已关闭 / 超时升级） |

### 可以拿它练手的问题

- 最近 7 天每条产线的良率是多少？
- 本月不良代码 TOP 10 及占比
- 各产品的一次合格率 FPY 对比
- 停机时长最长的 5 台设备是哪些？
- 近 30 天白班和夜班的产量趋势
- 已完工工单的达成率排名
- 当前有多少条 Andon 呼叫还没响应？
- 哪几道工序的不良数最高？

---

## 5. 常见问题

**Q：一直提示「没能给出可执行的查询」，怎么办？**
先看页面展开的报错。若提示「大模型调用失败」，检查 `BaseUrl` / `ApiKey` / 网络出口；
若提示 PostgreSQL 报错，可以点 `GET /api/assistant/schema` 确认表是否在语义层里。

**Q：SQL 明明跑通了，为什么结果是一张空表？**
最常见的是**维度口径不对**，尤其是「产线」这个维度：
`reporting.daily_shift_metrics."LineName"` 的值来自**班次定义**（`reporting.shifts."LineName"`），
班次没有绑定产线时这一列**全是空字符串** —— 所以写 `WHERE "LineName" <> ''`、`IS NOT NULL` 或
`GROUP BY "LineName"` 必然得到空表（或一行空产线）。
**要按产线拆分，走明细口径**：`production.production_reports` JOIN `production.work_orders`，
按 `"WorkCenter"`（业务上的产线）分组。语义层里「产线维度」一节已经把这套口径写给模型了，正常应该能一次问对。
其他原因：这段时间确实没有数据，或者演示数据还没灌（开发机上执行 `tools/seed-demo.ps1`）。

**Q：报 `A command is already in progress` 或「执行环境异常」是什么意思？**
这类是**执行环境**的问题（数据库连接 / 协议层），不是模型把 SQL 写错了。
页面会明确区分两者：**环境异常会直接终止「自我修复」**——因为让模型重写 SQL 毫无意义，它只会写出一样的 SQL 再失败一次，
白白多烧两次模型调用，还会把真实原因掩盖掉。
> 早先版本在这里踩过一个坑：只读事务初始化把三条 `SET` 拼成了**一个多语句命令**，
> 而 Npgsql 对多语句命令只处理第一个结果集，连接会停在"命令进行中"状态 ——
> 导致**任何一次问数都必然失败**，且报错看起来像 SQL 写错了。已修复，并补了回归测试（含把当时那条真实 SQL 原样固化）。

**Q：能不能问「这个月赚了多少钱」这类不存在的维度？**
不能。模型只认识语义层里的表和列，遇到不存在的数据会明确回答「用现有数据回答不了」，
而不是编一条看起来像真的 SQL——这比给你一个错误数字要好。

**Q：软删除数据会被算进去吗？**
不会。语义层规则里强制要求 `AND 别名."IsDeleted" = false`，生成的 SQL 会带上；
万一模型漏了，页面能直接看到 SQL，复制出来核对即可。

**Q：结果对不对怎么验证？**
生成的 SQL 完整展示、可一键复制，粘到任何 SQL 客户端跑一遍就能对账。
这是刻意设计——**不做黑盒**，问数的价值在于把「写 SQL 的时间」省掉，而不是让数字变得不可审计。

**Q：能改成直接由模型多轮对话做分析吗？**
可以，但没必要。当前是「一问一 SQL」，可控、可审计、成本可预测；
真要 Agent 化，把 `IAssistantService` 换成多步编排即可，语义层、护栏、执行器都能直接复用。
