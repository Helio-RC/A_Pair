# 文档索引

> 本文档是项目所有文档的导航地图。AI 辅助开发时应先阅读本索引，了解改什么代码需要联动更新哪些文档。

## 文档地图

```
README.md                     ← 项目入口，功能概览 + 开发状态
├── CHANGELOG.md               ← 版本变更记录
├── CONTRIBUTING.md            ← 开发环境、编码约定、添加功能流程
├── CLAUDE.md                  ← AI 编码助手主配置（含文件版本/迁移/JSON 约定）
│   └── docs/CLAUDE.md         ← CLAUDE.md 的人类可读副本（同步维护）
├── scripts/
│   ├── i18n.py                ← i18n 资源管理脚本
│   ├── ToolsCollection.md                ← i18n 脚本完整文档
│   ├── clean.sh / clean.ps1   ← 清理 bin/obj
│   └── publish.sh / publish.ps1 ← 多平台发布
├── ARCHITECTURE.md            ← 架构设计、分层、数据流、安全策略
├── docs/
│   ├── INDEX.md               ← 本文件
│   ├── LOGGING.md              ← 日志系统设计文档（等级规范、分模块覆盖、输出格式、最佳实践）
│   ├── Phases.md              ← 实现阶段、任务分解、工时估算
│   ├── ONBOARDING_GUIDE.md    ← 引导系统设计文档（JSON 驱动，启动引导 + 页面引导）
│   ├── SEATSETS_FORMAT.md     ← .seatsets 数据包格式规范（导出/导入/校验）
│   ├── StrategyDataResilience.md ← 策略数据持久化与容错分析
│   └── adr/
│       ├── ADR-001-avalonia-ui-framework.md        ← 选择 Avalonia UI 的决策
│       ├── ADR-002-mvvm-framework.md               ← 选择 CommunityToolkit.Mvvm 的决策
│       ├── ADR-003-layered-architecture.md ← 分层架构决策
│       ├── ADR-004-strategy-pattern-for-seating.md ← 策略模式座位安排决策
│       ├── ADR-005-command-pattern-for-undo-redo.md ← 命令模式撤销/重做决策
│       ├── ADR-006-strategy-pipeline-fill-in-order.md ← 策略管道 Fill-in-Order + 依赖策略三态
│       ├── ADR-008-onboarding-demo-data-injection.md ← 引导系统纯内存示例数据注入
│       ├── ADR-009-arrangement-counter.md ← 排座次数计数器客户端实现
│       ├── ADR-010-velopack-oss-distribution.md ← Velopack 自动更新与 OSS 分发架构
│       ├── ADR-011-keyboard-shortcuts.md ← 全局键盘快捷键系统（Behavior + Tunnel + 设置开关）
│       └── ADR-013-remove-plugin-system.md ← 移除插件系统（2026-09）
├── docs/presentation/
│   ├── Design_Spec.md        ← UI 设计规范（色板、字体、间距）
│   ├── DragDrop.md           ← Avalonia 12 拖放实现模式与踩坑记录
│   └── Fluent_Icons.md       ← 已使用的 FluentUI 图标清单
└── docs/sdk/
    └── README.md             ← 插件开发 SDK 指南
```

## 文档职责与联动规则

### README.md
- **覆盖**: 项目简介、核心功能列表、开发阶段状态表、文档导航
- **何时更新**: 新增功能、阶段推进、文档增删
- **关联文档**: 几乎所有文档（作为入口）

### CLAUDE.md（根目录）
- **覆盖**: AI 开发环境配置、构建命令、架构摘要、代码模式、文件版本管理、JSON 约定
- **何时更新**:
  - 新增/修改构建命令
  - 架构变更（分层、导航、启动流程）
  - 新增代码模式或约定
  - **新增文件类型版本迁移**（更新版本表、迁移步骤）
  - JSON 序列化约定变更
- **关联文档**: `docs/CLAUDE.md`（必须同步）、ARCHITECTURE.md、Phases.md

### docs/CLAUDE.md
- **覆盖**: 与根目录 CLAUDE.md 相同的可读副本
- **何时更新**: **每次修改根 CLAUDE.md 后必须同步**（包括新增的"文件拖放导入"小节）
- **关联文档**: CLAUDE.md（一一对应）

### ARCHITECTURE.md
- **覆盖**: 项目目标、分层架构、领域模型、策略引擎、数据存储、安全设计、跨平台设计
- **何时更新**:
  - 分层职责变更
  - 新增策略或修改策略管道
  - 数据模型结构变更
  - 存储/快照/加密方案变更
  - **文件版本管理机制变更**（更新 5.3 节）
- **关联文档**: Phases.md、docs/adr/

### docs/Phases.md
- **覆盖**: 实现阶段规划、具体任务清单、技术栈版本、风险分析
- **何时更新**:
  - 阶段任务完成或新增
  - 技术栈升级
  - **项目结构变化**（更新目录树）
- **关联文档**: ARCHITECTURE.md

### CONTRIBUTING.md
- **覆盖**: 环境搭建、项目结构、编码约定、提交约定、**文件版本管理开发流程**
- **何时更新**:
  - 开发流程变更
  - 编码约定变更
  - **添加新文件类型或迁移步骤的示例代码**
- **关联文档**: CLAUDE.md、ARCHITECTURE.md

### docs/adr/ADR-*.md
- **覆盖**: 特定技术决策的上下文、方案对比、最终选择
- **何时更新**: 推翻或重大修改既有决策时
- **关联文档**: ARCHITECTURE.md（引用 ADR）

### scripts/ToolsCollection.md
- **覆盖**: i18n 管理脚本的完整参考，包括子命令、安全机制、命名规范、常见工作流
- **何时更新**: 脚本新增子命令、修改校验规则、修改工作流
- **关联文档**: CLAUDE.md（i18n 节）

### docs/presentation/Design_Spec.md
- **覆盖**: 色板、排版层级、间距系统、圆角、布局模式
- **何时更新**: 视觉规范变更、新增 Token
- **关联文档**: Fluent_Icons.md

### docs/presentation/Fluent_Icons.md
- **覆盖**: 项目中已使用的所有 FluentUI 图标名称
- **何时更新**: 新增或替换图标时
- **关联文档**: Design_Spec.md

### docs/presentation/DragDrop.md
- **覆盖**: Avalonia 12 拖放 API 正确用法、`PointerPressed` + `DoDragDropAsync` 模式、数据格式创建/读写、CanvasZoomPan 冲突处理、常见坑及修复
- **何时更新**: 新增拖放交互、Avalonia 版本升级后 API 变更、发现新的拖放坑
- **关联文档**: CLAUDE.md（Behaviors 节）、SeatingArrangementView.axaml.cs

### docs/sdk/README.md
- **覆盖**: 插件开发指南、接口参考、打包格式
- **何时更新**: 插件 API 变更、打包格式变更
- **关联文档**: ARCHITECTURE.md（插件化架构）

## 常见变更场景的文档联动清单

| 变更场景 | 需更新的文档 |
|---|---|
| 新增/修改/删除 .resx 资源 key | scripts/ToolsCollection.md（如新增工作流）、CLAUDE.md（i18n 节工具链） |
| 新增文件类型版本迁移 | CLAUDE.md、docs/CLAUDE.md、ARCHITECTURE.md（5.3 节）、CONTRIBUTING.md（示例代码）、Phases.md（Phase 7 状态） |
| 新增策略实现 | ARCHITECTURE.md（4.4/4.5 节）、Phases.md、CLAUDE.md（架构摘要） |
| 新增/修改策略配置 | 更新 manifest JSON（`Manifests/{Id}.json`）中的 `parameters`/`codeBlocks`；CONTRIBUTING.md（声明式配置） |
| 修改策略配置 UI | 更新 manifest JSON，不修改 C#（声明式驱动） |
| 新增页面 | CLAUDE.md（导航枚举值 + Patterns）、Design_Spec.md（如有新图标） |
| 修改数据模型 | ARCHITECTURE.md（3.x 节）、Phases.md |
| 修改 JSON 序列化格式 | CLAUDE.md（JSON 约定）、docs/CLAUDE.md、CONTRIBUTING.md（字段约定） |
| 修改快照/完整性检测逻辑 | CLAUDE.md（快照完整性检测/轮转/嵌入）、docs/CLAUDE.md、ARCHITECTURE.md（5.3 节） |
| 修改构建/测试流程 | CLAUDE.md、CONTRIBUTING.md、README.md |
| 推进开发阶段 | README.md（状态表）、Phases.md |
