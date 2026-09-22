# 改进建议与 AI 迭代（宿主侧接入）

> 状态：**试验项目**。整套周期规则已在线上跑通（提建议 / 评审 / 交叉对比 / 冻结 / 审阅 / 结算 / 建任务 / 执行）。
> 本文回答两件事：**QiaoMES 在这件事里负责什么**、**`Iteration:BaseUrl` 到底填在哪里**。

---

## 0. 一句话说清分工

QiaoMES 只做 **入口 + 权限闸门 + 密钥代持**；建议、迭代计划、审阅留痕、执行审计**全部在独立的 AI 迭代服务里**
（私有仓 [`qiao33128/ai-iteration`](https://github.com/qiao33128/ai-iteration)）。

两个刻意的取舍：

| 取舍 | 为什么 |
|---|---|
| 宿主**不存**一份建议/计划 | 再存一份必然出现两份真相，对账成本远大于收益 |
| 浏览器**不碰**管理员密钥 | 迭代服务的 `Admin__ApiKey` 由 QiaoMES 服务端代持，只在服务端转发时带上 `X-Admin-Key` |

代码位置（全部在宿主这一侧，很薄）：

```
src/QiaoMES.Api/Iteration/
├── IterationOptions.cs        # 配置 POCO（appsettings 的 Iteration 节）；运行时是**单例、可被页面改写**
├── IterationClient.cs         # 转发 + 密钥代持 + 超时；失败用返回值表达，不抛异常；每次请求现算地址
├── IterationContracts.cs      # 配置快照 / 变更请求 / 探活结果 / 存储接口
└── IterationSettingsStore.cs  # 配置落库（加密存储）+ 保存即生效
src/QiaoMES.Api/Controllers/IterationController.cs   # /api/iteration/*（权限闸门 + 三个配置接口）
frontend/src/views/iteration/IterationView.vue       # 菜单「改进建议」（含「迭代服务配置」弹窗）
docs/ITERATION.md                                     # 本文
```

---

## 1. 🔴 配置填在哪里（这就是那个报错的答案）

页面报 **「迭代服务还没配置：请在「改进建议 → 迭代服务配置」里填上地址并保存」**
只有一个原因：**宿主当前生效的 `BaseUrl` 是空的** —— 也就是这个功能处于**关闭**状态（不是故障）。

### 首选：页面上填，保存即生效

登录 QiaoMES → 左侧「改进建议」→ 右上角 **「迭代服务配置」**（需要 `iteration:manage`，`admin` 默认就有）：

| 字段 | 说明 |
|---|---|
| 服务地址 | 同容器网络填服务名 `http://ai-iteration:8080`（推荐，不必对外暴露端口）；本机 Docker 填 `http://host.docker.internal:8091`；宿主直接 `dotnet run` 填 `http://localhost:8091`。**留空并保存 = 关停该功能** |
| 管理员密钥 | **只以掩码回显**（如 `aiit****2026`）；**明文既不进数据库、也不出接口** —— 它只写在服务器上的配置文件里。**留空 = 不改动现有密钥**（只改地址不会把密钥抹掉） |
| 超时（秒） | 提交建议要调大模型做评审与一致性检查，默认 180 |
| 测试连接 | 只读探活：确认地址可达 + 两端密钥是否都已配置。⚠️ 它**不校验密钥值是否一致** —— 迭代服务的那些管理员接口全是带副作用的 POST（冻结周期 / 批准条目 / 领任务），拿来做探活会真的改数据；密钥对不对要用一次真实的管理员操作来验证 |

保存后**立即生效**：不用重启、不用重新部署，点「刷新」就能看到周期与计划。

**存到哪了**：`config/iteration.json`（容器里是 `/app/config/iteration.json`，由编排把宿主目录挂进来；
`dotnet run` 时是项目目录下的 `config/`）。**密钥不进数据库** —— 它的保护级别和 `.env` 完全一样：
都在同一台机器的文件系统上，Linux 上写成 `0600`、只有属主能读；因此也不会被 `pg_dump` 备份带走。

> 🔴 **优先级会咬人，务必知道**：**只要在页面上保存过一次，就以配置文件为准** ——
> 之后再改 `.env` / 环境变量都不会生效（弹窗里的「当前来源」会显示「存在服务器的配置文件里」，
> 这就是排查"我明明改了配置却没反应"的第一现场）。
> 想把控制权交还给部署配置：删掉那个文件 —— `rm /root/qiaomes/config/iteration.json`，**立即生效，不用重启**。

### 部署配置（下面四选一：无人值守、或者没有管理员能登录页面时）

「在 QiaoMES 的配置里填」指的也就是这四种。

### 填法 A · 本机跑（`dotnet run`）→ 改 appsettings（最直观）

`src/QiaoMES.Api/appsettings.Development.json`：

```jsonc
{
  "Iteration": {
    "BaseUrl": "http://localhost:8091",   // 迭代服务的地址
    "AdminKey": "与服务端 Admin__ApiKey 一致的值",
    "TimeoutSeconds": 180                  // 可省，默认 180
  }
}
```

改完**重启后端**才生效（配置在启动时定型，与其它外部集成一致）。

### 填法 B · Docker（本地或服务器）→ 写 `.env`

仓库根目录的 `.env`（本地）或服务器 `/root/qiaomes/.env`：

```bash
ITERATION_BASE_URL=http://ai-iteration:8080
ITERATION_ADMIN_KEY=<与服务端 Admin__ApiKey 一致>
```

`docker-compose.yml` 与 `docker-compose.deploy.yml` 都会把这两个值透传成容器内的
`Iteration__BaseUrl` / `Iteration__AdminKey`。完整模板见仓库根 [`.env.example`](../.env.example)。

临时试一下也可以直接命令行给（不需要改 `.env`）：

```bash
ITERATION_BASE_URL=http://ai-iteration:8080 ITERATION_ADMIN_KEY=xxxx \
  docker compose -f docker-compose.deploy.yml up -d --force-recreate api
```

> 📌 **地址写什么**：
> - 迭代服务与 QiaoMES **在同一个容器网络里** → 用服务名，如 `http://ai-iteration:8080`（不用对外暴露端口，最推荐）
> - 迭代服务在**宿主机上**、QiaoMES 在容器里 → `http://host.docker.internal:8091`（容器里的 `localhost` 指容器自己）
> - 宿主直接 `dotnet run`、服务在宿主机 → `http://localhost:8091`

### 填法 C · 生产（CI 自动部署）→ 配到 GitHub 仓库里（推荐）

| 位置 | 键 | 值 |
|---|---|---|
| Settings → Secrets and variables → Actions → **Variables** | `ITERATION_BASE_URL` | `http://ai-iteration:8080` |
| 同页面 → **Secrets** | `ITERATION_ADMIN_KEY` | 与服务端 `Admin__ApiKey` 一致 |

配完后**下次部署自动生效**（`deploy.yml` 会把它注入 `deploy/gh_deploy_aliyun.py`，脚本**合并写入** `/root/qiaomes/.env`）。
不用跑数据库脚本、不用动 workflow。

> 也可以直接把两个键手工加进服务器 `/root/qiaomes/.env` —— 部署脚本是**合并写入**，只覆盖 CI 本次显式提供的键，
> 手工加的键会保留下来。但那样就脱离了仓库的配置来源，下一次换机器会丢，**建议走 Variables/Secrets**。

### 填法 D · 任意场景 → 环境变量（通用但最容易写错）

ASP.NET Core 的层级分隔符是双下划线 `__`：

| appsettings 键 | 环境变量 |
|---|---|
| `Iteration:BaseUrl` | `Iteration__BaseUrl` |
| `Iteration:AdminKey` | `Iteration__AdminKey` |

`docker-compose*.yml` 里用的就是这个形式（左边 `Iteration__BaseUrl`，右边读 `.env` 里的 `ITERATION_BASE_URL`）。
注意**别写成 `Iteration:BaseUrl`**（冒号在 Linux 环境变量名里非法）。

### 怎么确认配好了

**最快的方式**：在「迭代服务配置」弹窗里点 **「测试连接」** —— 它会告诉你服务是否可达、两端密钥是否都已配置。

命令行对照：

```bash
# 1. 容器里能读到（.env 生效值，密钥只显示键名）
docker compose -f docker-compose.deploy.yml config | grep -i iteration

# 2. 宿主侧接口返回不再是「还没配置」（需要 JWT + iteration:suggest 权限）
curl -s -H "Authorization: Bearer <token>" https://mes.qiaoqiaoqiao.me/api/iteration/cycle

# 3. 看配置文件（**有文件就以文件为准**）
docker exec qiaomes-api cat /app/config/iteration.json
# 或直接在宿主上看（Linux 上权限应当是 -rw-------）：
ls -l /root/qiaomes/config/iteration.json
```

📌 **备份/迁移时别忘了它**：`config/iteration.json` 和 `.env` 一样属于「换机器要带走」的东西；
它**不在数据库备份里** —— 这正是「密钥不进库」的代价，别只备份了 `pg_dump`。

📌 **多副本注意**：配置文件是**本机**的。将来若把 api 扩成多个副本，各副本会各写各的；
到那时要么换共享卷，要么把配置交回部署流程管理。当前是单机单实例，不受影响。

---

## 2. 报错对照表

| 页面/接口提示 | 含义 | 处理 |
|---|---|---|
| 迭代服务还没配置：请在「改进建议 → 迭代服务配置」里填上地址并保存 | 当前生效的 `BaseUrl` 为空 = **功能关着**（正常状态，不是故障） | 管理员在页面上填地址并保存（保存即生效）；若服务器上已有 `config/iteration.json`，先看第 1 节的优先级说明 |
| 迭代服务不可达：…（连接被拒绝 / 名字无法解析 / 超时） | `BaseUrl` 写错，或服务没起、不在同一网络 | 点「测试连接」；服务地址按第 1 节那一行逐条核对；容器里 `getent hosts ai-iteration` 能验证 DNS |
| 迭代服务返回 401 / 403 | `AdminKey` 与服务端 `Admin__ApiKey` 不一致 | 两边对齐后重启宿主 |
| 迭代服务返回 5xx：<服务端原文> | 迭代服务自己出错 | 看迭代服务日志（宿主只是转发，原文已带出来） |
| 迭代服务响应超时（>180s） | 提建议要调大模型做「评审 + 一致性交叉对比」 | 重试；必要时调大 `Iteration:TimeoutSeconds` |

> 这些提示是**有意区分**的：`IterationClient` 用返回值表达失败（不抛异常），调用方必须能分清
> 「服务没配」「服务不可达」「服务说不行」三种情况 —— 否则用户永远只看得到一句「操作失败」。

---

## 3. 权限

| 权限码 | 能做什么 | 默认持有者 |
|---|---|---|
| `iteration:suggest` | 提**修改现有功能**的建议；查看周期与本期计划 | `supervisor`（主管）等 |
| `iteration:manage` | 提**新增功能**建议 + 审阅计划（批准 / 否决 / 提意见）+ 推进周期 | 仅 `admin` |

「只能对自己可用的功能提建议」是**服务端**强制的：前端下拉框只列出你持有的 `*:read` 权限对应功能，
提交时后端还会用同一个权限码再校验一次（复用 `perm:` 动态策略）—— 前端过滤不是安全边界。

---

## 4. 周期节奏（由迭代服务执行，宿主不参与调度）

| 时间 | 发生什么 |
|---|---|
| 周一 ~ 周四 | 提建议。AI 评审 + 与已有计划交叉对比：干净才合并，**有冲突 / 歧义当场抛出** |
| **周五 00:00** | 冻结收集，留一整天给管理员审阅（批准 / 否决 / 提意见） |
| 周五 24:00 | 结算：**低风险沉默即放行；中 / 高风险沉默即顺延**（有意见、或报过冲突的条目一律顺延） |
| **周六 08:00** | 自动执行：AI 在工作区改代码 → 跑全量测试 → 判据全过就自动合并 `main` → GitHub Actions 构建并部署上线 |

管理员也能在「改进建议」页手动点 **冻结收集 / 结算 / 执行已放行** 推进当前周期（等价于到点自动触发）。

### 安全边界（刻意的取舍）

- 自动合并只做**快进式推送、绝不 `--force`** —— `main` 若这期间前进过就如实报错转人工，宁可这次改动停在分支上，也不覆盖主线
- 执行判据是硬性的：编译通过 + **全量测试全绿** + 无越界改动（超出声明影响面即失败）+ 无残留未提交文件
- 执行环境与生产隔离在资源上（容器限内存 / CPU + 独立 swap），构建再重也不会把生产拖死

### 已知限制

1. 执行跑在 2 vCPU / 896MB + swap 的机器上，**慢**（首次还要冷 NuGet 缓存，之后走持久化缓存）。
2. 规划器看不到代码仓库，`impact.files` 有时为空，会让「越界校验」对那类条目不生效。

---

## 5. 常见问题

**Q：我想先只看不改，怎么彻底关掉？**
把 `ITERATION_BASE_URL` 留空即可（或删掉 `.env` 里那一行）—— 页面显示「迭代服务还没配置」，**不会报错**。
功能默认关闭是刻意的设计，不是配置缺失。

**Q：改了 BaseUrl，为什么页面还是老样子？**
配置在**宿主启动时定型**（`Program.cs` 里 `Configure<IterationOptions>` + `HttpClient.BaseAddress`），
必须重启进程 / 重建容器：`docker compose -f docker-compose.deploy.yml up -d --force-recreate api`。

**Q：为什么前端拿不到地址和密钥？**
`GET /api/iteration/cycle` 由宿主服务端转发，前端只看得到迭代服务返回的业务结构，**看不到 `BaseUrl` 与 `AdminKey`**。
这正是「宿主代理转发」而不是「浏览器直连」的意义。

**Q：能不能让普通管理员在页面上配这一项（像智能问数那样）？**
技术上可以（照抄 `Assistant` 的「模型配置」入口即可），但**不建议**：
问数换模型是业务级开关，谁都能判断该用哪个模型；而迭代服务的地址与管理员密钥是**基础设施凭据**，
暴露到浏览器只会多一个泄露面。
