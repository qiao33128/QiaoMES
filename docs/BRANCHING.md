# 分支 = 环境：dev（开发环境）→ main（生产）

> 这份文档回答一件事：**一个改动从「AI 改完」到「上生产」，中途经过谁、在哪停下、谁能放行。**
> 部署链路本身的细节在 [CICD.md](CICD.md)；自迭代服务的配置在 [ITERATION.md](ITERATION.md)。

## 1. 对照表（先看这张）

| 分支 | 环境 | 地址 | 容器 / 编排 | 谁能改 | 部署触发 |
|---|---|---|---|---|---|
| `dev` | **开发环境**（候选版本） | `http://139.196.195.44:8092` | `qiaomes-dev-*` / `docker-compose.dev.yml`（独立库、独立网络、独立 JWT 密钥） | **AI 自迭代自动推**（也可以人工推） | push `dev` → `.github/workflows/deploy-dev.yml` |
| `main` | **生产** | `https://mes.qiaoqiaoqiao.me`（:8090） | `qiaomes-*` / `docker-compose.deploy.yml` | **只由人工合入** | push `main` → `.github/workflows/deploy.yml` |

两个环境跑在**同一台服务器**上（2 vCPU / 896MB），但完全分家：不同 compose 项目、不同容器名、
不同数据库卷、不同 docker 网络、不同 JWT 密钥。开发环境的三个服务都有内存硬限制 ——
它自己撑爆只会杀它自己，生产不受影响。

## 2. 完整流程

```
① 需求方在 QiaoMES 里提「改进建议」
        ↓
② AI 评审 + 周五冻结审阅 + 结算（低风险沉默即放行 / 中高风险沉默顺延）
        ↓
③ 自动执行（在能跑构建的机器上——开发机的 AiIteration.Worker，不是服务器）
        ↓  判据全过：编译 + 全量测试全绿 + 无越界改动 + 无残留文件
④ 快进推送 iteration/xxx → **dev**        ← 到此为止，绝不碰 main
        ↓
⑤ dev 的 CI：全量测试 → 构建 :dev 镜像 → 部署开发环境（:8092）
        ↓
⑥ 需求方在开发环境看真实效果、与提建议的人对意见
        ↓  确认没问题
⑦ 人工把 dev 同步到 main（见第 4 节）→ 生产 CI → 生产更新
```

**为什么③要放在服务器外**：执行要装 .NET SDK + Node + git 并真的跑构建，这台 896MB 的机器
在服务器上跑构建会 OOM（QiaoMES.Api 自己就占约 150MB）。所以线上服务
`Scheduler__ExecutorEnabled=false`，只做"计划 + 审阅 + 审计"，执行交给开发机上的
`AiIteration.Worker`（它通过 `POST /api/tasks/lease` 租任务）。

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

- **只允许快进**（`--ff-only`）：`dev` 分叉过说明中间有人直接改过 main，这种情况要先弄清差异再合，
  不要用一个普通的 merge 把不清楚的东西带进生产。
- 合并前确认 `dev` 那条流水线是**绿的**（测试 + 开发环境部署都过）。
- 回滚：生产工作流支持 `workflow_dispatch` + `image_tag` 输入（填上一次的 git SHA），
  服务器上 `.last_api_image` / `.last_web_image` 记着上一版镜像地址。详见 [CICD.md](CICD.md)。

## 5. 排错对照

| 现象 | 原因 / 处理 |
|---|---|
| 开发环境 502 / 打不开 | `docker ps --filter name=qiaomes-dev-` 看容器是否在；`docker logs qiaomes-dev-api`。它可能被内存限制 OOM 掉（`docker inspect qiaomes-dev-api --format '{{.State.OOMKilled}}'`）；不用时 `docker compose -p qiaomes-dev stop` 腾内存 |
| 开发环境「改进建议」显示「迭代服务还没配置」 | **正常**：开发环境**刻意**不接自迭代服务（数据隔离 —— 免得有人在开发站点上批准/否决**真实**的迭代计划）。页面提示后面会跟一句本环境的说明（配置键 `Iteration:UnconfiguredHint`，只在开发环境那份编排里设）；生产站点上同样的提示**不会**出现。真要连通：给 `docker-compose.dev.yml` 的 api 加 `Iteration__BaseUrl`（并注意那份地址指向的是真实数据） |
| AI 推 `dev` 报 non-fast-forward | 工作区的 `defaultBranch` 还是 `main`（见第 3 节），或 `dev` 被人工改过。先把它改成 `dev` 再重跑任务 |
| AI 报「拒绝自动合并到生产分支 main」 | 闸门生效了：`MergeTargetBranch` / `ITERATION_MERGE_TARGET` 被设成了 `main`，改回 `dev` |
| 开发环境的登录/密码与生产不同 | 应该是不同的 —— 两个环境刻意用不同 JWT 密钥。开发环境的初始账号由库初始化逻辑创建，与生产一致 |
| 开发环境数据库想重来 | `docker compose -p qiaomes-dev down -v`（会清空开发库，**只动开发卷** `qiaomes_dev_pgdata`） |
