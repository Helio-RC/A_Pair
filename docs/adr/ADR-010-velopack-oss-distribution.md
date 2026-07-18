# ADR-010：Velopack 自动更新与 OSS 分发架构

## Status
Accepted

## Date
2026-07-19

## Context

SeatFlow v1.4.0 从便携式单文件分发迁移到标准安装包 + 自动更新。需要选择一个跨平台安装/更新框架，并设计分发架构。

核心需求：
- **跨平台**：Windows（Setup.exe）、Linux（AppImage）、macOS（.pkg/.dmg，计划中）
- **自动更新**：增量更新（delta）、全量回退、启动时自动应用已下载更新
- **双源容灾**：主更新源不可用时自动降级到 GitHub Releases
- **中国用户可达**：更新文件托管在阿里云 OSS（香港），通过 Cloudflare Worker 代理

## Decision

**使用 Velopack 1.2.0 作为安装和自动更新框架。更新文件托管在阿里云 OSS，通过 `download.seatflow.work`（Cloudflare Worker 代理）分发。**

### 架构

```
客户端 (UpdateService)
  │
  ├─ ① GET /updates/metadata (UA: SeatFlow/x.x)
  │     └─ Worker 透传 → 后端 API → 返回源健康状态
  │
  ├─ ② GET /updates/releases.{channel}.json?arch=&os=&rid=&id=&localVersion=
  │     └─ Worker url.search='' → OSS → 200 (签名校验仅看路径)
  │
  └─ ③ GET /updates/*.nupkg
        └─ Worker 透传 → OSS
```

### 数据流

```
CheckForUpdatesAsync()
  → FetchMetadataAsync()              GET /updates/metadata
       └─ isFallback=false → 主源健康, isFallback=true → 跳过, 降级 GitHub
  → CreateApiManager()
       → SimpleWebSource("{base}/updates/")
            → GET releases.{channel}.json  (Velopack JSON 格式, 与 OSS 扁平结构兼容)
  → CreateGitHubManager()
       → GithubSource("github.com/SeatFlow/SeatFlow")  (兜底)
```

### OSS 文件结构

```
updates/
├── releases.{channel}.json            ← Velopack JSON 更新源
├── RELEASES-{channel}                 ← 旧版降级文件（扁平后缀, 非目录）
├── {package}-{version}-{rid}-full.nupkg
├── {package}-{version}-{rid}-delta.nupkg
└── {installer}-{version}.{ext}        ← 安装程序
```

### Worker 三项关键配置

1. **去掉查询参数**：`url.search = ''`（OSS 签名不认查询参数，透传触发 `SignatureDoesNotMatch`）
2. **关闭 Bot Fight Mode**：`/updates/*` 路径放行 `Velopack/x.x` UA
3. **旧版路径兜底**：`/updates/{channel}/RELEASES` → `/updates/RELEASES-{channel}`（通常不触发——JSON 主路径已通）

## Alternatives Considered

### 方案 A：Squirrel.Windows + 手动构建

Squirrel 是 Velopack 的前身，仅支持 Windows。

- **Pros**：成熟，社区大
- **Cons**：无跨平台支持；需自行实现 macOS/Linux 更新逻辑
- **Rejected**：跨平台是硬需求

### 方案 B：直接 GitHub Releases 分发

`UpdateManager(new GithubSource(...))`，不经过 Worker/OSS。

- **Pros**：零运维，免费
- **Cons**：未认证限制 60 req/h/IP；中国用户下载速度极慢；无更新源状态检测
- **Rejected**：无法满足中国用户的可用性要求

### 方案 C：OSS 直接暴露

不经过 Worker，客户端直连 OSS。

- **Pros**：减少一跳延迟
- **Cons**：OSS 签名 URL 有时效限制；无法做 Bot Fight Mode 控制；无法做 URL 重写
- **Rejected**：Worker 提供了必要的代理层——签名管理、UA 白名单、路径重写

### 方案 D：Worker 签名 URL 而非去参

不在 Worker 中去掉查询参数，而是重新签名 URL。

- **Pros**：保留 Velopack 查询参数用于日志
- **Cons**：Worker 需要访问 OSS AccessKey（安全风险）；复杂度增加
- **Rejected**：查询参数仅用于 Velopack 端日志，无业务价值，丢弃无损失

## Consequences

- **正面**：
  - 跨平台自动更新，用户无需手动下载安装包
  - 双源容灾——API 网关不可用时自动降级到 GitHub
  - Worker 层提供灵活的 UA 白名单、路径重写、缓存控制
  - OSS 分发对中国用户友好（香港 region）
- **负面**：
  - Worker 是额外的故障点（但目前 OSS 直连不可行，所以是必要的）
  - Velopack SDK 的默认 UA 需要 Worker 侧白名单（升级 SDK 版本时需确认 UA 变化）
  - 旧版 RELEASES 降级路径与 OSS 扁平结构不兼容（但不影响功能——JSON 主路径已通）
- **中性**：
  - 客户端通过 `LoggingFileDownloader` 捕获 Velopack 内部 HTTP 请求详情，方便排查
  - Metadata 端点 `recommendedSource` 字段当前未用于动态切换更新源 URL（预留扩展点）
