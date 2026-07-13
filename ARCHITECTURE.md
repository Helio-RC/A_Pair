一、项目目标与核心原则

目标：构建一个高度模块化、可扩展、易维护的.NET10跨平台桌面座位安排与轮换系统。

核心原则：

· 面向接口编程：模块间依赖抽象而非具体实现。
· 单一职责：每个模块仅负责明确的功能领域。
· 开闭原则：对扩展开放（新增布局/策略/数据源），对修改封闭。
· 依赖注入：通过 DI 容器管理组件生命周期与依赖。
· 配置驱动：行为由外部配置文件定义，减少硬编码。

---

二、整体架构分层

采用经典三层架构 + 插件化扩展：

```
┌─────────────────────────────────────────────────┐
│               Presentation Layer                 │
│        (Avalonia UI)                        │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│            Application / Orchestration           │
│     - 编排数据加载、布局构建、策略执行流程          │
│     - IApplicationFacade (外观接口)              │
│     - 依赖注入容器 (Microsoft.Extensions.DI)      │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│                  Domain Layer                    │
│  ┌──────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │Entities  │ │Value Obj │ │Strategy Interface│ │
│  │(Student, │ │(Seat,    │ │(ISeatingStrategy)│ │
│  │Classroom)│ │Position) │ │                  │ │
│  └──────────┘ └──────────┘ └──────────────────┘ │
└─────────────────────────────────────────────────┘
                        │
┌─────────────────────────────────────────────────┐
│              Infrastructure Layer                │
│  ┌────────────────────┐  ┌─────────────────────┐│
│  │ IStudentProvider   │  │ ILayoutDefinition   ││
│  │ (Xlsx/Csv/Json实现)│  │ (Grid/Circle/Stadium)││
│  └────────────────────┘  └─────────────────────┘│
└─────────────────────────────────────────────────┘
```

项目结构规划：

```
SeatFlow.slnx
├── SeatFlow.Core              # 领域核心
├── SeatFlow.Contracts         # 共享契约（插件接口）
├── SeatFlow.Application       # 应用层（编排、策略调度）
├── SeatFlow.Infrastructure    # 基础设施（数据访问、布局实现）
├── SeatFlow.Plugins.Sdk       # 插件 SDK
├── SeatFlow.Presentation.Avalonia  # Avalonia UI 主程序
├── SeatFlow.Core.Tests          # 核心领域测试
├── SeatFlow.Application.Tests   # 应用层测试
└── SeatFlow.Infrastructure.Tests # 基础设施测试
```

---

三、核心领域模型

3.1 学生（Student）

```csharp
public class Student
{
    public string Id { get; set; }                     // 唯一标识
    public string Name { get; set; }
    public float? Height { get; set; }                 // 身高(cm)
    public Gender? Gender { get; set; }
    public bool NeedsFrontRow { get; set; }            // 是否需要前排
    
    // 轮换记录（环形缓冲区，存储最近N次座位ID）
    public CircularHistory<string> RecentSeatHistory { get; set; } = new(3);
    
    // 轮换权重（用于距离讲台轮换算法）
    public int FrontRowPreferenceScore { get; set; }
    
    // 扩展数据挂载点（插件使用）
    public AttributeBag Extensions { get; set; } = new();
}
```

3.2 座位（Seat）

```csharp
public abstract class Seat
{
    public string Id { get; set; }              // 逻辑ID
    public abstract SeatType Type { get; }
    public string LogicalGroup { get; set; }    // 逻辑分区
    public abstract object GeometryData { get; }
    
    public bool IsAvailable { get; set; } = true;
    public bool IsFixed { get; set; }
    public string OccupantId { get; set; }
    
    public AttributeBag Extensions { get; set; } = new();
}
```

具体实现：

· GridSeat：网格布局（行列）
· PolarSeat：极坐标布局（环形、扇形）

3.3 教室布局（ClassroomLayout）

支持网格、扇形、自由点等多种形式，并通过障碍物列表处理柱子、讲台等非常规结构。

```csharp
public class ClassroomLayoutDefinition
{
    public string LayoutType { get; set; }       // "Grid", "Polar", "Freeform"
    public LayoutMetadata Metadata { get; set; }
    public List<Seat> Seats { get; set; }
    public List<Obstacle> Obstacles { get; set; }
}
```

3.4 座位快照（SeatingSnapshot）

用于历史版本管理与回滚。

```csharp
public class SeatingSnapshot
{
    public string Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; }
    public string LayoutId { get; set; }
    public Dictionary<string, string> SeatAssignments { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

---

四、策略引擎设计

4.1 策略接口

```csharp
public interface ISeatingStrategy
{
    string Id { get; }
    string Name { get; }
    int Priority { get; set; }      // 数值越大越先执行
    bool IsEnabled { get; set; }
    
    Task<StrategyExecutionResult> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken);
    ValidationResult ValidateConfiguration();
}
```

4.1b 依赖策略接口（v2）

```csharp
public interface IDependentSeatingStrategy
{
    string Id { get; }
    string Name { get; }
    string DisplayName { get; }
    int Priority { get; set; }      // 上下文内部优先级，独立于外部管道
    bool IsEnabled { get; set; }

    Task<DependentEvaluationResult> EvaluateAsync(
        SeatingWorkspace workspace, Student student, Seat targetSeat,
        IRandomFillContext context, CancellationToken ct);
    ValidationResult ValidateConfiguration();
}
```

依赖策略不直接加入外部执行管道，而是在 RandomFill 的分配循环中运行。每次 RandomFill 随机选出 (student, seat) 对后，
依次调用依赖策略的 `EvaluateAsync`。返回三种结果：
- **Approve**：批准分配
- **Reject**：请求重掷（尝试其他座位）
- **Handled**：已自行完成分配（含连携修改相邻座位），RandomFill 跳过 TryAssignSeat

`IRandomFillContext` 提供 `RerollCount`、`MaxRerolls` 以及 `LogWarning`/`LogError` 方法。

4.2 工作区（Workspace）

提供可修改的座位视图，隔离原始数据，记录修改历史。

```csharp
public class SeatingWorkspace
{
    public IReadOnlyList<Student> Students { get; }
    
    public bool TryAssignSeat(string seatId, string studentId, out string error);
    public IEnumerable<Seat> GetEmptySeats();
    public IEnumerable<Seat> FindSeats(Func<Seat, bool> predicate);
    public SeatingPlan BuildSeatingPlan();
    public IReadOnlyList<StrategyMessage> Messages { get; }
    public void LogWarning(string strategyId, string displayName, string messageKey, params object?[] args);
    public void LogError(string strategyId, string displayName, string messageKey, params object?[] args);
}
```

4.3 执行管道

管道采用 **"按优先级填空"（Fill-in-Order）** 模型。所有独立策略操作同一个
`SeatingWorkspace` 实例，按 Priority 降序依次执行。每个策略在空座中操作，
后执行的策略在剩余空座中择优。不存在"覆盖"语义。

依赖策略不在外部管道中执行，而是在 RandomFill 的分配循环中按上下文内部优先级
依次评估（DeskMate 50 → GenderRestrictedSeat 45 → NoRepeatDeskMate 40）。
Handled 后仍继续运行后续依赖策略以供检查/警告。

```
独立策略 Priority 降序 →
  FixedSeat(100)         ← 最先执行：锁定固定座位（IsFixed=true，通过 Capability.MarkFixedSeat 能力）
  FrontRowRotation(50)  ← 第二执行：在非固定空座中填前排
  RandomFill(1)       ← 兜底填充：
    └─ 内部上下文 (依赖策略按 Priority 降序) →
       DeskMate(50)               ← 检查同桌关系，协调同行分配（连携修改/腾挪）
       GenderRestrictedSeat(45)   ← 检查性别限制，不匹配时重定向
       NoRepeatDeskMate(40)       ← 检查相邻已占座是否与历史同桌重复，重复则重掷
  Defrag(0)            ← 最后执行："扫地僧"碎片整理，后排无约束学生前移

能力系统（Capability.cs）：
  Manifest capabilities 声明 → Facade 注册 → Workspace 校验 → 接口调用
  · MarkFixedSeat — IFixedSeatCapability.TryMarkFixed()
  · 日后在 Capability.cs 追加 const + interface 即可拓展
```

> **关键设计决策**：高 Priority = 先执行 = 优先挑选座位。冲突解决 = Priority 数值（先到先得）。
> 该模型是妥协方案——"后可覆盖"模型因 Workspace API 限制不可行，详见 docs/adr/ADR-006.md。

```csharp
public class StrategyExecutionPipeline
{
    public async Task<SeatingPlan> ExecuteAsync(SeatingWorkspace workspace, CancellationToken cancellationToken)
    {
        foreach (var strategy in _strategies.OrderByDescending(s => s.Priority).Where(s => s.IsEnabled))
        {
            var result = await strategy.ExecuteAsync(workspace, cancellationToken);
            if (!result.Success)
            {
                failedStrategies.Add($"{strategy.Name}({strategy.Id}): {result.Message}");
                workspace.LogError(strategy.Id, strategy.Name, "Pipeline_ExecFailed", result.Message);
            }
        }
        return workspace.BuildSeatingPlan();
    }
}
```

4.4 内置策略

| 策略 | Priority | 类型 | 职责 |
|------|----------|------|------|
| FixedSeatStrategy | 100 | 独立 | 最先执行，锁定固定座位（IsFixed=true），后续策略的 GetEmptySeats() 自动排除 |
| FrontRowRotationStrategy | 50 | 独立 | 在非固定空座中识别前排，按需求分数选出学生后 Fisher-Yates 洗牌，随机分布在各列 |
| DeskMateStrategy | 50 (context) | 依赖 | 在 RandomFill 中协调同桌组分配，同行+邻列+同 SeatsPerDesk 分组为同桌；可腾挪 RandomFill 已分配学生但不移动前序策略安置者 |
| GenderRestrictedSeatStrategy | 45 (context) | 依赖 | 在 RandomFill 中检查座位性别限制；不匹配时优先重定向到匹配性别的受限空座（减少无效重掷），无可用时请求重掷，耗尽则强制分配 |
| NoRepeatDeskMateStrategy | 40 (context) | 依赖 | 在 RandomFill 中检查历史同桌重复，从快照提取过去的同桌对；重复时请求重掷，耗尽则强制分配 |
| RandomFillStrategy | 1 | 独立+Host | 兜底填充剩余空座，约束学生（DeskMate 组）优先分配；托管依赖策略执行 |
| DefragStrategy | 0 | 独立 | 后置碎片整理——将后排无约束学生前移填空隙（跨列允许），跳过固定座位和 DeskMate 组学生，记录有效性警告 |

4.5 声明式策略配置

策略的配置界面由 manifest JSON 声明驱动，而非 ViewModel 中硬编码。

**三层声明：**

```
visible               ← 策略可见性（默认 true，false 时策略在 UI 和执行管道中完全排除）
isIndependent         ← 是否为独立策略（默认 true）。false 时策略以依赖策略形式在 RandomFill 上下文中执行
manifestVersion       ← Manifest 文件格式版本号（用于运行时版本兼容性校验，Manifest 不走 FileMigrationService）
parameters[]          ← 策略级全局参数（NumberInput/TextInput/ToggleSwitch/Dropdown）
codeBlocks[]          ← 按数据集/会场的配置块（Table/ValuePair 模式）
  ├── dataType        ← Student | Venue | Both
  ├── displayMode     ← Table | ValuePair
  ├── showSeatPosition ← 是否显示座位定位器（默认 true，自动匹配策略设为 false）
  ├── preventDuplicateInRow      ← 是否禁止同行学生选择器值重复（同桌策略设为 true）
  ├── preventDuplicateAcrossRows ← 是否禁止跨行学生选择器值重复（FixedSeat 设为 true）
  ├── loadTrigger    ← 配置加载触发方式：Both=需两个选择器都选（默认），Any=任一选择即加载
  └── fields[]        ← 自定义字段定义
messages[]             ← 策略执行消息 i18n 模板（{ "zh-CN": "{0}...", "en-US": "{0}..." }）
```

**i18n 方案：** 所有用户可见文字在 manifest 中以内嵌词典存储：
`{ "zh-CN": "历史惩罚权重", "en-US": "History Penalty Weight" }`。
UI 层通过 `LocalizeHelper.Resolve(dict)` 按 `CurrentUICulture` 解析。

**DeskMate（依赖策略，在 RandomFill 上下文中执行）特有：** `isIndependent: false`, `dataType: "Both"`, `showSeatPosition: false`, `preventDuplicateInRow: true`。
每行 StudentPicker 数量由会场 `GridLayoutMetadata.SeatsPerDesk` 动态决定，
会场变更导致 `SeatsPerDesk` 不匹配时自动清除旧配置行。
同行内多个学生选择器互相排除已选学生（同对防重复）。
作为依赖策略，DeskMate 在 RandomFill 随机分配时评估每个 (student, seat) 对：
检查同桌关系，寻找相邻空座进行连携分配，必要时请求重掷。

**FixedSeat 特有：** `preventDuplicateAcrossRows: true`。
跨所有行的学生选择器互相排除已选学生（全局防重复），确保一个学生只能固定在一个座位。

**FrontRowRotation：** 无 codeBlock——`NeedsFrontRow` 是 Student 模型字段，由 CSV/XLSX 导入。

**配置加载行为：** 持久化配置行的匹配采用"已选定则匹配，未选定则跳过"策略：
`(SelectedDataset is null || c.DatasetId == SelectedDataset.Id) && (SelectedVenue is null || c.VenueId == SelectedVenue.Id)`。
对于 `dataType: "Both"`，仅选数据集时场馆作为通配符，立即加载配置；后续选择场馆后重新精确匹配。
学生选择器通过 `_pendingSelections` 字典延迟到学生列表加载完成后再应用，避免 `SelectById` 在学生列表为空时被调用导致选中信息丢失。

**策略执行消息：** Manifest 中的 `messages` 字段为策略执行时可能产生的警告/错误提供 i18n 模板。
模板中用 `{0} {1}` 占位，策略通过 `workspace.LogWarning(id, displayName, key, args)` 写入，
消息（含 `StrategyDisplayName`、`MessageKey`、`Args`）收集在 `SeatingWorkspace.Messages` 中供 UI 展示。
日志同时记录原始 ID，UI 层将学生 ID 解析为姓名后显示。

插件 manifest 同样支持 `parameters`、`codeBlocks` 和 `messages`——声明后即可自动获得配置 UI 和消息支持。

4.6 插件化策略

· Assembly 插件：编译为 DLL，实现 IPluginSeatingStrategy。
· Script 插件：支持 Lua / C# Script，逻辑完全由脚本文件描述。
· 插件清单：双层架构 — `plugins-manifest.json`（包级，定义加载指令）+ 策略 `manifest.json`（定义 ID、parameters、codeBlocks 等，格式与内置策略一致）。

---

五、配置文件与存储结构

5.1 配置文件分类

类别 文件位置 作用域
程序设置 AppSettings.json 全局
会场定义 Venues/*.venue.json 单个物理空间
人员名单 Rosters/*.roster.json 单次活动
座位安排结果 Assignments/{venue}/{date}/snapshot.json 特定场次
插件配置 Plugins/{plugin-id}/config.json 单个插件

5.2 数据存储位置

所有用户数据存储在 OS 标准应用数据目录（`AppEnvironment.DefaultDataDirectory`）：
- Windows: `%APPDATA%\SeatFlow\`
- Linux: `~/.local/share/SeatFlow/`
- macOS: `~/Library/Application Support/SeatFlow/`

用户可通过设置页面的 `DataDirectory` 选项自定义路径。

5.3 文件夹布局

```
{DataDirectory}/
├── AppSettings.json
├── Venues/
├── Rosters/
├── Assignments/
├── StrategyConfig/
├── Logs/
└── Temp/
```

插件目录：Velopack 安装时位于 `RootAppDir/Plugins/`，开发/便携模式位于 `{exeDir}/Plugins/`。

5.4 版本管理与升级

· 配置文件包含 `version` 字段（`VenueFile` v1.1、`RosterFile` v1.0、`SeatingSnapshot` v1.0、`AppSettings` v1.0、`StrategyConfig` v1.0、`VenueSnapshotInfo` v1.0）
· 各文件类型当前版本号记录于 `SeatFlow.Infrastructure/Migration/file_versions.json`（嵌入资源，随程序编译）
· 加载时 `FileMigrationService` 读取文件版本号，链式执行注册的 `IFileMigrator` 实现完成向前迁移（不支持回退）
· 迁移器按文件类型组织：`Migration/Migrators/{FileType}Migrators.cs`，每个版本步进为一个嵌套类（如 `VenueMigrators.Step_1_0_to_1_1`），DI 中以 `IFileMigrator` 注册
· 反序列化前通过 `JsonNode` 操作完成迁移，避免目标类型 Schema 不一致
· JSON 序列化约定：camelCase 命名字段；`layoutType` 为数字枚举、`layoutTypeString` 为字符串；Seat 多态通过 `SeatJsonConverter` 的 `Type` 鉴别器
· 座位快照支持父子关系，便于追溯与回滚

5.5 敏感数据保护

· 字段级 AES-256 加密（标记 [SensitiveData] 的属性）。
· 文件级加密（用户密码保护整个 Roster 文件）。
· 密钥使用平台安全存储（DPAPI / libsecret）。

---

六、跨平台表示层设计

6.1 UI 框架选择

Avalonia UI，支持 Windows / macOS / Linux，原生 MVVM 支持。

6.2 表示层与应用层通信

采用 外观模式 + 依赖注入：

```csharp
public interface IApplicationFacade
{
    // 数据管理
    Task<IReadOnlyList<StudentDatasetInfo>> ListStudentDatasetsAsync(CancellationToken ct = default);
    Task ImportStudentsAsync(string filePath, CancellationToken ct = default);
    // 排座
    Task<SeatingWorkspace> GenerateSeatingAsync(SeatingRequest request,
        IProgress<SeatingProgress>? progress = null, CancellationToken ct = default);
    // 导出
    Task ExportSeatingPlanAsync(ExportOptions options, SeatingWorkspace workspace,
        ClassroomLayoutDefinition layout, CancellationToken ct = default);
    // 快照
    Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync(string venueId, CancellationToken ct = default);
    Task RollbackToSnapshotAsync(string snapshotId, CancellationToken ct = default);
    // 命令历史
    Task<bool> ExecuteCommandAsync(IUndoableCommand command, CancellationToken ct = default);
    Task<bool> UndoAsync(CancellationToken ct = default);
    Task<bool> RedoAsync(CancellationToken ct = default);
    // 策略配置
    Task<IReadOnlyList<StrategyDisplayInfo>> GetStrategiesAsync(CancellationToken ct = default);
    Task SaveStrategyConfigAsync(string id, StrategyConfig config, CancellationToken ct = default);
    // … 共 40+ 方法
}
```

ViewModel 通过构造函数注入 IApplicationFacade，调用业务逻辑。

6.3 进度报告与异步

· 使用 IProgress<SeatingProgress> 报告长时间操作状态。
· 命令模式（ICommand）绑定 UI 操作。

---

七、补充考量与边缘情况

7.1 非功能性需求

类别 处理方案
性能 高效数据结构、并行执行无冲突策略、进度刷新限频
并发 文件锁机制，可选 SQLite 支持多人编辑
日志 Serilog 分级输出，审计关键操作
国际化 .resx 资源文件（Lang/Resources.resx + 卫星资源），CultureInfo 切换
内存管理 快照文件化，内存仅保留索引，实现 IDisposable

7.2 业务流程闭环

· 手动微调：支持拖拽换位，记录手动覆盖标记。
· 撤销/重做：命令模式实现操作栈。
· 数据验证：导入后调用 IDataValidator 返回错误列表。
· 打印/PDF 导出：已集成 QuestPDF（`PdfSeatingExporter`）。

7.3 异常处理

场景 策略
座位不足 提前容量检查，抛出明确异常
固定座位冲突 配置验证阶段检测，Fill-in-Order 模型下 FixedSeat 最先执行锁定座位
插件加载失败 提供“安全模式”跳过插件
配置文件损坏 自动加载最近有效备份
策略执行超时 CancellationToken + 超时设置

7.4 部署与更新

· 打包格式：Windows (.msi / .zip)、macOS (.app / .pkg)、Linux (AppImage / Flatpak)。
· 自动更新：可选集成 Velopack 或 Squirrel。

7.5 测试策略

· 单元测试：核心算法。
· 集成测试：完整管道。
· 快照测试：验证输出一致性。
· 性能基准：BenchmarkDotNet。
· 沙箱测试：脚本死循环隔离。

7.6 安全与隐私

· 脚本沙箱：禁用 IO/网络，限制执行时间与内存。
· 导出匿名化：支持仅显示学号。
· 日志脱敏：避免打印完整姓名。

---

八、后续开发里程碑

阶段 内容 产出
Phase 1 领域建模、基础架构搭建 核心实体、DI 配置、网格布局
Phase 2 数据加载与导出 Xlsx/Csv 读取、Excel 导出
Phase 3 内置策略实现 7 策略（FixedSeat, FrontRowRotation, DeskMate, RandomFill, GenderRestrictedSeat, NoRepeatDeskMate, Defrag）
Phase 4 插件系统 插件管理器、Assembly 加载
Phase 5 脚本支持 Lua/C# 引擎集成、受限 API
Phase 6 高级布局 + 拖放 圆形/扇形/自由点、拖拽换座、CanvasZoomPan
Phase 7 配置管理与版本迁移 文件版本管理、迁移管线、快照回滚、完整性检测
Phase 8 测试与文档 单元测试覆盖、用户手册
（CLI 工具 命令行为后续规划中功能）

---

九、附录：关键接口速查

接口 所在层 用途
IApplicationFacade Application UI 与业务逻辑通信外观
ISeatingStrategy Domain 座位安排策略
IPluginSeatingStrategy Contracts 插件策略契约
IClassroomLayout Domain 教室布局抽象
IStudentProvider Infrastructure 学生数据加载
ISeatingPlanExporter Infrastructure 结果导出
IConflictResolver Application 座位冲突解决
IPluginManager Application 插件发现与加载

---

文档版本：1.2
最后更新：2026-05-24
适用项目：座位安排/轮换系统（跨平台桌面版 .NET 10 + Avalonia 12）
