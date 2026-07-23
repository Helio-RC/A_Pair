# Changelog

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [1.4.0] — 2026-07-24

### Added
- **Velopack 安装与自动更新**：引入 Velopack 安装框架，支持跨平台安装包（Windows Setup.exe、Linux AppImage，macOS 计划中暂无安装包）和增量自动更新。双源架构（API 网关 `seatflow.work` → GitHub Releases 兜底），已下载的更新包在启动时自动应用
- **遥测系统**：OpenTelemetry 遥测（页面浏览、操作事件、性能指标），opt-in 同意弹窗，支持 Gzip 压缩批量上报
- **日志系统重构**：Serilog 结构化日志替代 `Debug.WriteLine`，实例隔离文件名（`SeatFlow_{yyyyMMdd-HHmmss}.log`），分模块等级覆盖，文件尺寸/数量上限控制（`docs/LOGGING.md`）
- **.seatsets 数据打包格式**：将 AppData 全量数据打包为单个 `.seatsets` JSON 归档（分块 SHA256 校验 + 版本号系统），三种导入途径（设置页面按钮 / 双击文件 / 首次启动自动发现），Win32 命名管道多实例转发
- **文件关联**：Windows `.seatsets` 文件注册到 HKCU，双击自动启动程序导入
- **"仅更新人员数据集"**：不重新排座，仅用最新学生数据更新已有快照
- **安装时数据迁移**：Velopack `--veloapp-install` hook 自动复制安装程序目录下的 `.seatsets` 到应用根目录，首次启动自动导入
- **键盘快捷键系统**：全局快捷键支持（Ctrl+Z 撤销、Ctrl+Y 重做、Ctrl+S 保存、Ctrl+滚轮 缩放、Delete 删除、Esc 取消），采用 Static Behavior + Tunnel 路由实现，与 TextBox 内置快捷键自动避让
- **快捷键设置页面**：在设置中添加"键盘快捷键"卡片，6 个独立 ToggleSwitch 开关，保存后即时生效无需重启
- **初次引导新增快捷键环节**：启动引导（v3.4）新增 Settings 阶段（3 步），引导新用户了解快捷键开关功能
- **引导指示器按阶段呈现**：左下角步骤点从 ~24 个缩减为 9 个阶段点，通过延迟 Dispatch 覆盖 Guide Indicator 实现

### Changed
- **取消单文件发布**：移除 `PublishSingleFile`，改为标准 `dotnet publish` 文件夹输出 + zip/tar.gz 打包。Velopack 安装包作为主要分发形式，便携包保留
- **数据存储路径标准化**：从 `{exeDir}/AppData/` 迁移到 OS 标准路径（Windows `%APPDATA%\SeatFlow\`、Linux `~/.local/share/SeatFlow/`、macOS `~/Library/Application Support/SeatFlow/`）
- **发布文件名简化**：zip/tar.gz 发布文件名从 `SeatFlow-{ver}-{label}-{rid}.{ext}` 简化为 `SeatFlow-{ver}-{platform}.{ext}`
- **日志系统**：`Debug.WriteLine` → Serilog + `Microsoft.Extensions.Logging.ILogger<T>`，`docs/LOGGING.md` 完整文档
- **辅助脚本**：`version.py` 统一管理 App/文件格式/策略清单/引导配置版本；`i18n.py` .resx 资源 CRUD + Designer.cs 自动生成
- **ZoomOnScroll**：滚轮缩放改为 Ctrl+滚轮（不再任意滚轮缩放），普通滚轮恢复 ScrollViewer 正常滚动

### Removed
- **启动时目录清洁检查**（`CheckCleanDirectory`）：不再要求 exe 居于空目录，适配安装包目录结构
- **单文件发布标志**：`PublishSingleFile`、`IncludeNativeLibrariesForSelfExtract`、`IncludeAllContentForSelfExtract`

### Fixed
- **单文件发布数据目录 BUG**：修复 `AppContext.BaseDirectory` 在单文件发布时指向临时解压目录（`%TEMP%\.net\...`），改用 `Environment.ProcessPath`，防止 AppData 在更新/重启后丢失
- **引导系统**：修复引导不显示、死锁、竞态、窗口状态不同步、初次启动配置文件缺失等多项问题
- **UI**：弹窗内容重叠修复、颜色随主题、导入后自动刷新、教师视角导出列镜像修正、修复导出按钮无反应
- **遥测与引导冲突**：修复引导在初次启动时覆盖遥测同意弹窗
- **文件拖放导入**：支持从 OS 文件管理器直接拖入文件到应用窗口完成导入，拖入时显示遮罩覆盖层（支持/不支持两种视觉反馈）。人员管理页支持 `.csv`/`.xlsx`/`.json`，自由点管理支持 `.csv`/`.json`，首页和设置页支持 `.seatsets`，其他页面显示不支持提示

### Added
- **应用数据打包（.seatsets）**：将 AppData 全部数据（设置、会场、名单、快照、策略配置）打包为单个 `.seatsets` JSON 归档文件，支持分块 SHA256 完整性校验和版本号系统
- **三种导入途径**：(1) 设置页面按钮导入/导出，含类别选择对话框；(2) 双击 `.seatsets` 文件自动启动程序导入（需 OS 文件关联）；(3) 首次启动时自动发现 exe 目录下的 `.seatsets` 文件并静默全量导入
- **导入/导出选择对话框**（`SeatSetsSelectionWindow`）：含五个数据类别的复选框、全选/取消全选，区分导出/导入模式
- **文件校验**：导入前校验文件大小（上限 200 MB）、JSON 格式、chunk 哈希和归档哈希，篡改检测
- **尽力而为导入**：单文件/单 chunk 失败不中断整体，返回详细成功/跳过/失败计数
- **i18n**：新增 25 个 `SeatSets_*` 资源键（中/英）
- **单元测试**：11 个 `SeatSetsServiceTests`（往返、篡改检测、部分选择、空目录、超大文件、探测类别等）
- `docs/SEATSETS_FORMAT.md` — .seatsets 文件格式规范文档

### Changed
- `CheckCleanDirectory()` 允许 `.seatsets` 文件存在于 exe 目录（用于自动发现）
- `CheckSeatSetsAutoImportAsync()` 在 AppData 创建前执行，确保数据在引导系统之前可用
- `SettingsView.axaml` 新增"数据管理"区域（导出/导入按钮）
- `IApplicationFacade` 扩展：5 个 SeatSets 方法（Export/Import/Validate/Discover/ProbeCategories）
- `file_versions.json` 新增 `"seatsets": "1.0"` 条目
- `ServiceCollectionExtensions` 注册 `ISeatSetsService`、`SeatSetsSelectionViewModel`、`SeatSetsMigrator`

## [1.2.0] — 2026-06-19

### Added
- **交互式引导系统**：19 步启动引导 + 2 个页面引导（FreeformManagement、PluginManagement），JSON 驱动配置（`onboarding_config.json` v3.0），Popup 弹窗 + ControlHighlight 边框 + Placement 自适应定位。窗口失焦自动收起避免孤立窗口，恢复后自动定位到当前步骤。纯内存示例数据注入（ADR-008），引导期间不产生磁盘文件
- **拖拽换座**：座位排布画布支持拖放交换（`DoDragDropAsync` + `PointerPressed` 模式），拖放期间 CanvasZoomPan 通过 NaN 哨兵机制自动忽略平移。详见 `docs/DragDrop.md`
- **教师/学生视角导出**：`LayoutPerspective` 枚举（StudentView / TeacherView），Excel/CSV/PDF/PNG 导出支持选择视角，座位位置标签按视角翻转
- **`RemoveStudentCommand`**：可撤销的学生移除命令（`IUndoableCommand`），集成到 CommandHistory 支持撤销/重做
- **i18n 管理脚本**（`scripts/i18n.py`）：Python 3，支持 .resx 三文件同步 CRUD + `Resources.Designer.cs` 自动生成。自动备份到 `Lang/.backup/`（已 gitignore）。45 个单元测试。完整文档 `scripts/ToolsCollection.md`
- **版本号管理脚本**（`scripts/version.py`）：Python 3，统一管理 App 版本 / 文件格式版本 / 策略清单版本 / 引导配置版本。`bump-file` 自动同步 JSON + Model 类 + JsonStudentWriter。26 个单元测试。文档 `scripts/ToolsCollection.md`
- **策略配置清理**（`ConfigCleanupService`）：自动检测并清理无效策略配置（已删除的数据集/会场），集成 `NoRepeatDeskMateHistoryLoader` 历史清理
- **引导重启**：`Settings_RestartGuide` 设置入口，用户可手动重新运行启动引导
- **Guide 样式系统**（`Guide.axaml`）：Popup 主体、箭头、ControlHighlight 边框的完整样式定义
- ADR-008 — 引导系统纯内存示例数据注入决策记录
- `docs/DragDrop.md` — Avalonia 12 拖放实现模式与 CanvasZoomPan 交互记录
- `docs/ONBOARDING_GUIDE.md` — 引导系统设计文档
- `docs/StrategyDataResilience.md` — 策略数据韧性文档

### Changed
- **引导系统重构**：从硬编码 7 步改为 JSON 驱动 19 步启动引导 + 页面引导。步骤定义声明式（titleKey/descKey/target/placement），零 C# 硬编码。引导完成状态持久化到 `AppSettings.CompletedPageGuides`
- **页面切换动画优化**：从 CrossFade（闪烁）改为 Slide 动画（200ms），引导导航跳过动画直接切换
- **MainWindow**：侧栏整合到 `MainShellViewModel`，新增 `Activated`/`Deactivated` 事件转发给 `OnboardingService`
- **SeatingArrangement**：座位画布重构支持拖放 + 创建空布局 + 手动快照 + 策略消息面板折叠。新增 ~230 行交互逻辑
- **VenueConfiguration**：`NewVenue()` 取消飞行中的 `SelectVenueAsync` 消除竞态条件
- **Settings**：外观/行为分组重构，最大快照数可配置，新增引导重启入口
- `IApplicationFacade` 扩展：`DeleteStudentFromDatasetAsync`、`CleanupInvalidStrategyConfigsAsync`、`LoadCompletedGuidePagesAsync` / `MarkGuidePageCompletedAsync`
- `CanvasZoomPan` 重构：NaN 哨兵机制兼容拖放，缩放/平移/拖放三模式平滑切换
- `App.axaml.cs` 重构：启动流程 7 步标准化（语言→XAML→DI→TopLevel→Dialog→Watchdog→InputNormalizer→Settings）
- 所有 `.axaml` 视图添加 `x:Name` 以支持引导系统的目标解析
- 发布脚本增强：多平台打包改进

### Fixed
- 修复侧栏拖动范围越界
- 修复引导 Popup 失焦后仍可见的孤立窗口问题（窗口状态同步机制）
- 修复引导页面切换时 target 解析失败（先导航后解析 x:Name 顺序修正）
- 修复策略配置页引导中 ToggleSwitch 未被框中的问题
- 修复初次启动时配置文件未创建导致引导不显示
- 修复参数缺失导致的策略配置加载失败
- 修复快照轮转删除逻辑
- 修复控件边框渲染不显示的问题
- 修复无法拖动座位的问题
- 修复 `Guide_Seating_Select_Title` / `Guide_Seating_Select_Desc` 在 Designer.cs 中存在但 .resx 缺失的问题
- 修复 .resx 文件 XML 注释影响解析器的问题
- 修复 `JsonStudentWriter.cs` 硬编码 `Version = "1.0"` 与 roster `1.1` 不一致

## [1.1.0] — 2026-06-14

### Added
- **依赖策略系统**：`IDependentSeatingStrategy` + `IRandomFillContext` 接口，策略在 RandomFill 分配循环内评估，支持 Approve / Reject / Handled 三态响应
- **3 个新策略**：
  - `GenderRestrictedSeatStrategy` — 座位级性别限制，不匹配时自动重定向到匹配性别的受限空座
  - `NoRepeatDeskMateStrategy` — 基于历史快照防止过去同桌再次相邻
  - `DefragStrategy` — 后置碎片整理，将后排无约束学生前移填空隙（默认关闭）
- **能力声明系统**（`Capability.cs` + `IFixedSeatCapability`）：策略在 manifest.json 中声明能力，运行时校验。插件可通过 `TryMarkFixed` 保护座位不被后续策略移动
- **历史感知加载器**：
  - `FrontRowHistoryLoader` — 从快照恢复学生前排座位历史，跨会话轮换惩罚生效
  - `NoRepeatDeskMateHistoryLoader` — 从快照提取过去同桌对，供同桌不重复策略使用
- **`SeatAdjacencyHelper`** — 共享座位邻接判定（Grid/Polar/Freeform 三种布局统一判定 + 桌边界感知）
- `PluginPackageConfigService` — 插件策略配置存储路由（与内置策略物理分离）
- `PluginEnables` — 运行时启用状态管理（`data/enables.json`）
- `PluginManager` 支持单个包热重载（`RefreshPackageAsync`）和策略级启用/禁用（`SetStrategyEnabledAsync`）
- ADR-007 — 多策略插件包架构决策记录
- ADR-006 补充 — 依赖策略三态模型、能力声明系统
- 策略执行消息系统：策略可在执行期间通过 `LogWarning`/`LogError` 报告警告/错误，UI 侧栏展示

### Changed
- **DeskMate 同桌策略重写**：从独立策略改为依赖策略，三级协调分配（充足→挺挪→部分+警告），成功率大幅提升
- **策略管道模型**：从 `后可覆盖（override）` 改为 `按优先级填空（Fill-in-Order）`，Priority 降序（高→先执行）
- **Priority 语义反转**：数值越大越先执行（旧版：越小越先），FixedSeat=100 → RandomFill=1 → Defrag=0
- **插件系统重构**：从单策略插件改为多策略插件包架构（`plugins-manifest.json` + 策略 `manifest.json` 双层清单），支持一个包承载多个策略和热插拔。新增 `.ap-plugin` 打包格式
- `PluginManifest` 类型已删除，由 `PluginPackageManifest`（包级）+ 策略 `manifest.json`（策略级）替代
- 插件清单格式从 `plugin.manifest.json` 改为 `plugins-manifest.json`
- 插件包扩展名从 `.apairplugin` 改为 `.ap-plugin`
- `IPluginSeatingStrategy` 新增默认接口实现（`Category` / `Version`）
- `IsFixed` 从只读改为可设置（`IPluginSeat.IsFixed { get; set; }`）
- `CircularHistory` 容量从 3 增至 10，新增 `Resize()` 方法和 `Add()` 去重
- ZIP 安全校验增强（条目数 / 压缩比 / 总大小 / 路径遍历防护）
- 策略配置页重构：独立 / 依赖策略分组展示，竖线层级指示器
- 座位安排页新增加策略消息面板（可折叠）+ 修改历史时间线
- 侧栏移除折叠功能
- 配置路由：内置策略 → `AppData/StrategyConfig/`，插件策略 → `Plugins/{pkgId}/{strategyPath}/`
- `JsonSerializerOptions` 统一为 `JsonOptions` 静态池（消除每次序列化的重复分配）
- `GetStrategiesAsync` 新增 30 秒短期缓存
- `[ObservableProperty]` 字段全面转换为 C# 13 partial 属性语法

### Fixed
- 配置读写路径不一致：`SaveStrategyConfigAsync` 正确路由插件策略到 `PluginPackageConfigService`，`ApplyCodeBlockConfigsAsync` 同步路由
- `CircularHistory.Add` 无去重导致快照回滚时历史膨胀
- DeskMate 腾挪操作污染 `RecentSeatHistory`（新增 `TryAssignSeat(updateHistory: false)` 重载）
- `SaveDirtyBlockEditors` 发后即忘 → await 化
- `IdentifyFrontRowSeats` 逻辑在 `FrontRowHistoryLoader` 和 `FrontRowRotationStrategy` 中重复 → 提取到 `SeatGeometryHelper`
- `ValidateZipSafety` 在 `PluginManager` 和 `PluginPackage` 中重复 → 各自独立维护（架构约束）
- 所有 `Dispose()` 补充 `GC.SuppressFinalize(this)`（8 处）
- `CancellationToken` 未转发（6 处）
- 未使用的局部变量 / 参数清理（10+ 处）
- `ConfigBlockRowViewModel` MVVMTK0042 转换后 CodeBlock 属性丢失（CS0103）→ 手动修复为 partial 属性

### Obsoleted
- 旧 `plugin.manifest.json` 单策略格式不再被识别

## [1.0.0] — 2026-06-07

### Added
- 导航区页面可导航性管理（`Data/page_navigation.json`），支持禁用页面并提示原因
- 确定性构建（`<Deterministic>true` + `<PathMap>`），相同源码产生相同 DLL
- 构建时自动注入 git commit hash 到版本号（MSBuild target `GenerateGitCommit`）
- `Data/page_navigation.json` 嵌入资源，控制各页面启用/禁用
- `Nav_PluginDisabled` 资源键（zh-CN: 插件系统尚未就绪 / en-US: Plugin system not ready）

### Changed
- 关于页面版本号改为 `about.json` + git commit hash（格式 `1.0.0+af52bf7`）
- 人员管理数据集交互重构：点击侧栏即加载、保存直接写入、切换数据集检测未保存修改
- 人员管理表格新增删除行按钮、底部空行（写完一行加一行）、保存时验证空行
- 保存前检查空行是否有未完成数据，弹窗确认

### Fixed
- 新建会场时配置区数据未清空的并发问题（取消旧 CTS + 检查取消令牌）
- 人员管理新增行布局错误（DockPanel 子元素顺序）
- 策略配置数据块 UI 硬编码中文字符串修正为 i18n（`ConfigBlock_Dataset`、`ConfigBlock_Venue`）
- 快照回滚失败异常消息去硬编码中文
- 页面禁用按钮 ToolTip 不显示（`IsEnabled=False` 改用 `Opacity`）
- `page_navigation.json` 资源加载（`AssetLoader` → `Assembly.GetManifestResourceStream`）
