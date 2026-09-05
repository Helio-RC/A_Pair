# SeatFlow CI / 发布流水线说明

本项目发布已迁移至 GitHub Actions：自动发布由 `version.json` 驱动，手动发布通过
`workflow_dispatch` 触发；增量更新包（delta）、Worker 密钥轮换均由 CI 管理。

## 工作流一览

| 工作流 | 触发 | 职责 |
|--------|------|------|
| `release.yml` | push `version.json`（自动）/ workflow_dispatch（手动） | 4 RID（win-x64 / linux-x64 / osx-x64 / osx-arm64）并行构建 + vpk 打包（含 delta、可选签名）+ OSS 上传（仅自动）+ GitHub Release |
| `unit-tests.yml` | push/pull_request（代码变更） | 构建 + 分层单元测试（NuGet 缓存） |
| `worker-secret-sync.yml` | 每周一 03:00 UTC / 手动 | 将 OSS 密钥同步到 Cloudflare Worker（secrets-bulk） |

## 一、自动发布（latest）

1. 修改根目录 `version.json` 的 `version` 字段（如 `2.0.0` → `2.0.1`），建议使用
   `python3 scripts/version.py bump-app patch --force` 统一管理
2. 同步更新 `RELEASE.md` 发布说明（GitHub Release body 直接读取该文件）与
   `CHANGELOG.md`
3. 提交并合并到 `main` → `release.yml` 因 `version.json` 变更自动触发：
   - 预检：该 tag 不存在，且版本号必须大于当前最新 release，否则中止
   - 并行构建 4 个平台：win-x64（Setup.exe）/ linux-x64（AppImage）/
     osx-x64 + osx-arm64（.dmg），同时生成增量更新包（`-delta.nupkg`）
   - 上传 OSS（安装包 → `releases/{version}/`，更新包 → `updates/`，索引 → `releases/releases.json`）
   - 创建 GitHub Release（**latest**，非 pre-release）

## 二、手动发布（pre-release，不传 OSS）

1. 进入 GitHub 仓库 → **Actions → Release SeatFlow → Run workflow**
2. 填写参数：
   - `version`（必填）：发布版本号，**仅用于本次构建与 tag**，不写回 `version.json`
   - `suffix`（可选）：如 `beta.1`、`rc`，最终版本为 `{version}-{suffix}`
3. 手动发布固定为 **pre-release**，且**不执行 OSS 上传**（仅 GitHub Release）
4. 同样受预检约束：tag 不得已存在、版本必须大于当前最新 release

> 手动发布分支 `main` 即可触发（workflow_dispatch 需 `write` 权限，仓库默认允许）。

## 三、增量更新包（delta）

`vpk pack` 前，构建 job 会调用 `scripts/ci/fetch_previous.sh`
（封装 `vpk download http`）从更新源拉取上一版本产物：

- 成功 → vpk 自动生成 `SeatFlow-{version}-{rid}-delta.nupkg`，客户端增量更新链保持
- 失败（首次发布 / 更新源不可达）→ 容错跳过，仅生成 full 包（日志含 warn）

delta 包随 `*.nupkg` glob 一并上传 OSS `updates/`。

## 四、密钥轮换（Worker）

`worker-secret-sync.yml` 每周一 03:00 UTC 自动运行（亦可手动触发），
调用 `scripts/ci/rotate_worker_secrets.py` 将 `OSS_KEY_ID/OSS_KEY_SECRET`
同步为 Cloudflare Worker 的 secrets。失败自动创建 `ops` 标签 Issue。

## 五、所需 Secrets / Vars 配置

| 名称 | 类型 | 用途 |
|------|------|------|
| `UPDATE_FEED_URL` | var | 更新源 base URL（`https://download.seatflow.work/updates/`），delta 基础下载 |
| `OSS_KEY_ID` / `OSS_KEY_SECRET` | secret | OSS 访问密钥（上传 + Worker 同步源） |
| `OSS_ENDPOINT` / `OSS_BUCKET` | var | OSS 地址（如 `oss-cn-hangzhou.aliyuncs.com` / `seatflow-cn`） |
| `CF_ACCOUNT_ID` / `CF_API_TOKEN` | secret | Cloudflare Worker API 访问 |
| `CF_WORKER_SCRIPT` | var | 承载密钥的 Worker 脚本名 |
| `CF_API_BASE` | var（可选） | Cloudflare API 基址（默认 `https://api.cloudflare.com`） |
| `VPK_KEY_ID` / `VPK_KEY_FILE` / `VPK_KEY_PASSWORD` | secret（可选） | 代码签名（`--keyId`/`--keyFile`/`--keyPassword`），**任一为空即跳过签名环节**；Windows 用 pfx 证书，macOS 用 p12 |

仓库 **Environment** 需创建 `OSS`（release job 引用）。`release.yml` push 触发时
OSS 上传步骤需要 `OSS_*` 凭证；手动触发自动跳过该步骤，凭证缺失不影响。

### 签名环节说明

`vpk pack` 步骤仅在以下 secrets **全部非空**时附加签名参数：

```yaml
if [ -n "$VPK_KEY_ID" ]; then ARGS+=(--keyId "$VPK_KEY_ID"); fi
if [ -n "$VPK_KEY_FILE" ]; then ARGS+=(--keyFile "$VPK_KEY_FILE"); fi
if [ -n "$VPK_KEY_PASSWORD" ]; then ARGS+=(--keyPassword "$VPK_KEY_PASSWORD"); fi
```

未配置时产物为未签名包（GitHub 分发可直接安装，Windows 可能出现 SmartScreen 提示）。

### vpk 系统依赖

- **Linux runner**（win-x64 / linux-x64 打包）：`squashfs-tools` + `zstd`（workflow 已 apt 安装）
- **macOS runner**（dmg 打包）：`zstd`（workflow 已 brew 安装），vpk 工具链随 `dotnet tool install -g vpk` 就绪

### 并行构建矩阵

| matrix.rid | os | vpk 指令 | 产物 |
|-----------|-----|---------|------|
| win-x64 | ubuntu-latest | `[win]` | Setup.exe + nupkg（跨平台打包） |
| linux-x64 | ubuntu-latest | `[linux]` | AppImage + nupkg |
| osx-x64 / osx-arm64 | macos-latest | `[osx]` | .dmg + nupkg（macOS 专用托盘，需 macOS 主机执行） |

矩阵 `fail-fast: false`：单个平台失败不阻塞其余平台，
但 `release` job 仍会整体失败（需全部成功后才发版）。

## 六、缓存策略

- `unit-tests.yml` 与 `release.yml`（build job）均启用 `actions/cache`
- 缓存路径：`~/.nuget/packages`；key：`{os}-nuget-{**/*.csproj hash}`，
  restore-keys 回退 `{os}-nuget-`

## 七、脚本约定（scripts/ci/）

| 脚本 | 职责 |
|------|------|
| `upload_oss.py` | 上传产物至 OSS（凭据全部来自环境变量，无硬编码 URL/密钥） |
| `rotate_worker_secrets.py` | Cloudflare secrets-bulk 同步（CF API 基址可通过 `CF_API_BASE` 覆盖） |
| `fetch_previous.sh` | 封装 `vpk download http` 拉取上版本（delta 基础，容错） |

所有 URL / 账号 / 渠道标识均通过 `vars` / `secrets` 注入，脚本与工作流内无硬编码。
客户端应用内 `UpdateService.UpdateApiBase` 为运行时常量，如需统一参数化请另行处理。
