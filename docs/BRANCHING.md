# 分支 = 环境：dev（本机开发环境）→ main（生产）

> 这份文档回答一件事：**一个改动从「AI 改完」到「上生产」，中途经过谁、在哪停下、谁能放行。**
> 部署链路本身的细节在 [CICD.md](CICD.md)；自迭代服务的配置在 [ITERATION.md](ITERATION.md)。

## 1. 对照表（先看这张）

| 分支 | 环境 | 在哪 / 地址 | 编排 / 容器 | 怎么起 | 部署触发 |
|---|---|---|---|---|---|
| `dev` | **开发环境**（候选版本） | **你的开发机**，`http://localhost:8092` | `docker-compose.dev.yml` / `qiaomes-dev-*`（独立库、独立网络、独立密钥） | `pwsh tools/start-dev.ps1` | push `dev` → CI 只做**测试 + 构建 `:dev` 镜像** |
| `main` | **生产** | 阿里云服务器，`https://mes.qiaoqiaoqiao.me`（:8090） | `docker-compose.deploy.yml` / `qiaomes-*` | 由 CI 自动部署 | push `main` → `.github/workflows/deploy.yml` |

**为什么开发环境在开发机、不在服务器**：那台机器只有 2 vCPU / 896MB。2026-09-23 曾把开发环境也放上去，
结果一次 dev 部署（拉镜像 + 容器重建 + 镜像清理）把**磁盘 I/O 拖死** —— `load average` 冲到 38，
而 `ps` 里没有任何进程在吃 CPU（**高负载 + 无人占 CPU = 等 I/O**，不是 OOM），HTTP 全超时、生产一起不可达，
约 17 分钟后才自愈。结论：**这台机器给不了第二套环境**，候选版本就该跑在你自己机器上。

两个环境完全分家：不同 compose 项目、不同容器名、不同数据卷、不同网络、不同端口、不同 JWT 密钥。
所以本机可以同时跑着"生产镜像那套"（`tools/start-local.ps1`，8080）和"候选版本那套"（`tools/start-dev.ps1`，8092）。

## 2. 完整流程

```
① 需求方在 QiaoMES 里提「改进建议」
        ↓
② AI 评审 + 周五冻结审阅 + 结算（低风险沉默即放行 / 中高风险沉默顺延）
        ↓
③ 自动执行（在能跑构建的机器上——开发机的 AiIteration.Worker，不是服务器）
        ↓  判据全过：编译 + 全量测试全绿 + 无越界改动 + 无残留文件
④ 快进推送 iteration/xxx → **dev**            ← 到此为止，绝不碰 main
        ↓
⑤ dev 的 CI：全量测试 + 构建推送 `:dev` 镜像（**不做部署** —— Runner 在云上，到不了你的机器）
        ↓
⑥ 你在本机跑 `pwsh tools/start-dev.ps1` → 开发环境更新到这一版 → 需求方在 http://localhost:8092 看真实效果、对意见
        ↓  确认没问题
⑦ 人工把 dev 同步到 main（见第 4 节）→ 生产 CI → 生产更新
```

**为什么③要放在服务器外**：执行要装 .NET SDK + Node + git 并真的跑构建，服务器扛不住（见第 1 节）。
线上服务 `Scheduler__ExecutorEnabled=false`，只做"计划 + 审阅 + 审计"，执行交给 `AiIteration.Worker`
（它通过 `POST /api/tasks/lease` 租任务）。

## 3. AI 侧的硬约束（三处，缺一处就会漏到生产）

| 位置 | 约束 |
|---|---|
| AI迭代 仓 `WorkspaceProvisioner.PromoteAsync` | **代码级闸门**：目标分支是 `main` / `master` 时直接抛错。配错环境变量也推不上去 —— 只靠"配置里别写 main"是不够的 |
| AI迭代 仓 `Scheduler:MergeTargetBranch` / Worker 的 `ITERATION_MERGE_TARGET` | 默认值都是 `dev` |
| 工作区的 `defaultBranch` | 必须是 **`dev`** —— 它是 `iteration/*` 分支的基线与 reset 目标。留在 `main` 上，等 `dev` 领先 `main` 之后推送必然 non-fast-forward（判据再全也合不进去） |

## 4. 把 dev 同步到生产（人工，唯一动生产的入口）

```bash
# 在本地仓库（干净的工作区）：
git fetch origin
git checkout main && git pull --ff-only
git merge --ff-only origin/dev        # 只允许快进：dev 若分叉过就老老实实去看 diff，别硬合
git push origin main                  # → 触发生产部署（测试 → 构建 → 服务器 pull & up）
```

- **只允许快进**（`--ff-only`）：`dev` 分叉过说明中间有人直接改过 main，这种情况要先弄清差异再合。
- 合并前确认 `dev` 那条流水线是**绿的**，并且你已经在 8092 上看过实际效果。
- 回滚：生产工作流支持 `workflow_dispatch` + `image_tag`（填上一次的 git SHA）；服务器上
  `.last_api_image` / `.last_web_image` 记着上一版镜像。详见 [CICD.md](CICD.md)。

## 5. 排错对照

| 现象 | 原因 / 处理 |
|---|---|
| `start-dev.ps1` 报找不到 Docker / 引擎没就绪 | 先启动 Docker Desktop；脚本会等最多 5 分钟 |
| 8092 打不开 / 502 | `docker ps --filter name=qiaomes-dev-`；`docker logs qiaomes-dev-api`。首次起来要跑全量迁移，慢是正常的 |
| 开发环境「改进建议」显示「迭代服务还没配置」 | **正常**：开发环境**刻意**不接自迭代服务（数据隔离 —— 免得有人在开发站点上批准/否决**真实**的迭代计划）。提示后面会跟一句本环境的说明（配置键 `Iteration:UnconfiguredHint`，只在开发环境那份编排里设）。生产站点上同样的提示**不会**出现 |
| 想让开发环境也能看到自迭代数据 | 不推荐（会连上真实数据）。真需要：在 `.env.dev` 里加 `ITERATION_BASE_URL=http://<服务器>:8091` 与 `ITERATION_ADMIN_KEY=<与服务器一致>` |
| `git merge --ff-only` 失败 | `dev` 与 `main` 分叉过了：先看清差异（`git log --oneline main..dev` / `git diff main..dev`），别用普通 merge 把不清楚的东西带进生产 |
| AI 推 `dev` 报 non-fast-forward | 工作区的 `DefaultBranch` 还是 `main`（见第 3 节），先改成 `dev` 再重跑任务 |
| AI 报「拒绝自动合并到生产分支 main」 | 闸门生效了：`MergeTargetBranch` / `ITERATION_MERGE_TARGET` 被设成了 `main`，改回 `dev` |
| 服务器上还留着旧的 dev 栈（2026-09-23 迁移前的残留） | 撤除：`cd /root/qiaomes-dev && docker compose -f docker-compose.dev.yml down`（数据卷 `qiaomes_dev_pgdata` 默认保留，确认不要了再加 `-v`） |
