# 项目实现规划方案（详细版）

基于设计文档，制定具体可执行的实现路线图，涵盖技术准备、开发阶段、关键实现细节、测试与部署。

---

## 一、技术选型与工具链

### 1.1 核心框架与库

| 层次 | 技术选型 | 版本 | 用途 |
|------|----------|------|------|
| 运行时 | .NET 10 | 10.0.x | 跨平台基础 |
| UI 框架 | Avalonia UI | 12.0 | 跨平台桌面界面 |
| MVVM 框架 | CommunityToolkit.Mvvm | 8.4 | 响应式绑定、命令 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 10.0.x | DI 容器 |
| 配置管理 | Microsoft.Extensions.Configuration | 10.0.x | JSON 配置读取 |
| 日志 | Serilog | 4.x | 结构化日志 |
| Excel 处理 | EPPlus / ClosedXML | - | 导入导出 .xlsx |
| CSV 处理 | CsvHelper | - | CSV 导入导出 |
| 脚本引擎 | NLua / Microsoft.CodeAnalysis.CSharp.Scripting | - | Lua / C# 脚本插件 |
| 插件加载 | System.Runtime.Loader | - | Assembly 动态加载（可回收 ALC，卸载 + 压缩式 GC） |
| PDF 生成 | QuestPDF | - | 座位表导出 PDF |
| 序列化 | System.Text.Json | 10.0.x | JSON 处理 |
| 测试框架 | xUnit + NSubstitute + FluentAssertions | - | 单元/集成测试 |
| 性能基准 | BenchmarkDotNet | - | 性能测试 |
| 打包/更新 | Velopack | - | 安装包与自动更新 |

### 1.2 开发环境

- IDE：Visual Studio 2022 / JetBrains Rider / VS Code
- 操作系统：Windows 11 / macOS / Ubuntu（开发与测试均需覆盖）
- 版本控制：Git + GitHub / Azure DevOps
- CI/CD：GitHub Actions 或 Azure Pipelines（多平台构建）

---

## 二、项目结构详细规划

```
SeatFlow/
├── SeatFlow.slnx                          # 解决方案文件
├── SeatFlow.Core/                         # 领域核心（无外部依赖）
│   ├── Enums/                           # Gender, SeatType 等
│   ├── Models/                          # Student, ClassroomLayoutDefinition 等
│   ├── Providers/                       # 接口：IStudentProvider, IVenueRepository 等
│   ├── DomainServices/                  # ObstacleProcessor, SeatGeometryHelper 等
│   ├── Strategies/                      # ISeatingStrategy + 4 个内置实现
│   │   └── Manifests/                   # 声明式配置 JSON
│   └── Utilities/                       # AttributeBag, CircularHistory
│
├── SeatFlow.Contracts/                    # 共享契约（轻量接口）
│   └── Models/                          # IPluginSeatingStrategy, IPluginStudent 等
│
├── SeatFlow.Application/                  # 应用层
│   ├── Interfaces/                      # IApplicationFacade
│   ├── Services/                        # ApplicationFacade, ServiceCollectionExtensions
│   ├── Plugins/                         # PluginManager, PluginLoadContext
│   └── Pipelines/                       # StrategyExecutionPipeline
│
├── SeatFlow.Infrastructure/               # 基础设施层
│   ├── Providers/                       # Csv/Xlsx/JsonStudentProvider, CompositeStudentProvider
│   ├── Layouts/                         # GridLayoutBuilder, PolarLayoutBuilder, FreeformLayoutBuilder
│   ├── Exporters/                       # ExcelSeatingExporter, CsvSeatingExporter, PdfSeatingExporter, ImageSeatingExporter
│   ├── Repositories/                    # JsonVenueRepository, SeatingSnapshotRepository 等
│   ├── Writers/                         # JsonStudentWriter, CsvStudentWriter, XlsxStudentWriter
│   └── Migration/                       # FileMigrationService, IFileMigrator, file_versions.json
│
├── SeatFlow.Plugins.Sdk/                  # 插件 SDK（供外部插件引用）
├── SeatFlow.Presentation.Avalonia/        # Avalonia UI 主程序
│   ├── Views/
│   ├── ViewModels/
│   ├── Converters/
│   ├── Behaviors/
│   ├── Services/                        # INavigationService, IDialogService 等
│   ├── Lang/                            # .resx 国际化资源
│   ├── Data/                            # about.json, page_navigation.json
│   └── Assets/
│
├── SeatFlow.Core.Tests/
├── SeatFlow.Application.Tests/
├── SeatFlow.Infrastructure.Tests/
│
├── docs/                                # 设计文档、ADRs
│   └── adr/
├── plugins/                             # 外部插件目录（运行时）
└── samples/                             # 示例配置文件、数据文件
```

---

## 三、实现阶段详细规划

### Phase 1：领域建模与基础架构搭建（2-3 周）

**目标**：建立核心领域模型，完成 DI 容器配置，实现最简单的网格布局展示。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 创建解决方案结构与项目引用关系 | 可编译的空解决方案 | .slnx / 项目引用 | 0.5d |
| 实现 Core 层基础实体 | Student, Seat, ClassroomLayout 等 | 不可变设计、值对象 | 2d |
| 实现 AttributeBag 扩展数据容器 | AttributeBag 类 | `Dictionary<string, object>` + 线程安全 | 0.5d |
| 实现 CircularHistory<T> 环形缓冲区 | 用于座位历史记录 | 泛型、索引器、容量限制 | 0.5d |
| 定义 Contracts 层接口 | ISeatingStrategy, IStudentProvider 等 | 接口抽象 | 1d |
| 配置 DI 容器 | 在 Application 和 Presentation 中集成 | `Microsoft.Extensions.DependencyInjection` | 1d |
| 实现 IApplicationFacade 空壳 | 为 UI 层提供入口 | 外观模式 | 0.5d |
| 创建 Avalonia 基础窗口与 MVVM 骨架 | 主窗口、视图定位、命令绑定 | ReactiveUI / CommunityToolkit.Mvvm | 2d |
| 实现网格布局的简单渲染 | 在 Canvas 上绘制座位网格 | Avalonia 自定义控件 | 2d |
| 编写初始单元测试 | 实体行为、环形缓冲区 | xUnit | 1d |

**关键技术细节**：

1. **CircularHistory<T> 实现**：
   ```csharp
   public class CircularHistory<T>
   {
       private readonly T[] _buffer;
       private int _head;
       private int _count;
       public CircularHistory(int capacity) { _buffer = new T[capacity]; }
       public void Add(T item) { ... }
       public bool Contains(T item) { ... }
       public IReadOnlyList<T> GetAll() { ... }
   }
   ```

2. **AttributeBag 实现**：考虑使用 `ConcurrentDictionary` 或普通 `Dictionary` + 锁，根据使用场景（UI 线程安全需求低）。

3. **Avalonia 自定义座位控件**：继承 `UserControl`，通过 `ItemsControl` 绑定座位集合，使用 `Canvas` 绝对定位。

---

### Phase 2：数据加载与导出（2 周）

**目标**：支持从 CSV、Excel 导入学生数据，支持导出座位表为 Excel/PDF。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 实现 CsvStudentProvider | 解析 CSV 并映射到 Student | CsvHelper 库 | 1d |
| 实现 XlsxStudentProvider | 读取 Excel 学生名单 | EPPlus / ClosedXML | 1.5d |
| 实现 JsonStudentProvider | 读取 Roster JSON 文件 | System.Text.Json | 0.5d |
| 添加导入数据验证器 | IDataValidator 实现，检测必填字段、格式 | FluentValidation | 1d |
| 实现座位表 Excel 导出 | 导出为带格式的 Excel 表格 | EPPlus | 1.5d |
| 实现座位表 PDF 导出 | 生成可视化座位图 PDF | QuestPDF | 2d |
| 实现座位表 CSV 导出 | 简单逗号分隔导出 | CsvHelper | 0.5d |
| UI 集成：打开/保存文件对话框 | 文件选择与进度显示 | Avalonia 对话框服务 | 1d |
| 测试数据加载与导出边界情况 | 大文件、编码、错误数据 | 集成测试 | 1d |

**关键技术细节**：

1. **Excel 导入性能**：对于大型文件，使用流式读取 `ExcelReader` 而非一次性加载全部。
2. **PDF 布局计算**：根据座位坐标动态绘制矩形，使用 QuestPDF 的 `Canvas` 元素。
3. **错误处理**：导入失败时返回详细的错误列表，包括行号和字段名。

---

### Phase 3：内置策略实现（3-4 周）

**目标**：实现设计文档中列出的四个核心策略，构建策略执行管道。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 设计 SeatingWorkspace 与 SeatingContext | 提供可修改的座位视图 | 快照隔离、变更跟踪 | 2d |
| 实现 StrategyExecutionPipeline | 按优先级执行策略 | 异步管道、日志记录 | 1d |
| 实现 FixedSeatStrategy | 强制固定座位分配 | 配置读取、冲突检测 | 1d |
| 实现 DeskMateStrategy | ⚠️ 已隐藏：同桌组绑定靠近。实现存在根本性缺陷（见 ADR-006），当前默认不启用。 | 聚类算法（简单贪心） | 2d |
| 实现 FrontRowRotationStrategy | 基于历史分数轮换前排 | 分数累计算法 | 2d |
| 实现 RandomFillStrategy | 剩余学生随机填充 | Fisher-Yates 随机 | 0.5d |
| 添加策略配置模型与验证 | 每个策略的配置类与 ValidateConfiguration | FluentValidation | 1d |
| 实现座位冲突解决器 | IConflictResolver 处理固定座位冲突 | 策略模式 | 1d |
| 集成进度报告 | IProgress<SeatingProgress> 跨层传递 | `Progress<T>` | 1d |
| UI 集成：策略配置界面 | 动态生成策略配置面板 | Avalonia 数据模板 | 2d |
| 测试策略组合与边界情况 | 无座位、无人、冲突等 | 单元+集成测试 | 2d |

**关键技术细节**：

1. **SeatingWorkspace 实现**：
   - 内部维护原始座位列表的深拷贝，所有修改在拷贝上进行。
   - 记录每次修改（座位-学生映射变化）到 `_changeLog`，用于撤销/重做。
   - `BuildSeatingPlan()` 方法生成最终结果。

2. **FrontRowRotationStrategy 算法**：
   - 每次轮换时，计算每个学生对前排的“需求度”：`score = (前排累计次数 * -10) + (身高低加分) + (视力需求加分)`。
   - 按分数降序选择前排座位。

3. **DeskMateStrategy 实现**（⚠️ 已隐藏，见 ADR-006 补充）：
   - 先识别同桌组（配置中指定），将组视为一个单元。
   - 寻找相邻空座位组（网格布局中检查左右/前后相邻），将组内成员分配进去。
   - **已知缺陷**：在前序策略占座后连续块碎片化严重，组员常被拆散，无法保证有效分组。

4. **进度报告设计**：
   ```csharp
   public class SeatingProgress
   {
       public int TotalSteps { get; set; }
       public int CurrentStep { get; set; }
       public string StatusMessage { get; set; }
   }
   ```

---

### Phase 4：插件系统（2-3 周）

**目标**：实现插件管理器，支持加载外部 DLL 策略插件。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 定义插件契约 IPluginSeatingStrategy | 继承 ISeatingStrategy，添加插件元数据 | 接口设计 | 0.5d |
| 实现插件清单文件规范 | `plugin.manifest.json` 结构定义 | JSON Schema | 0.5d |
| 实现 PluginManager | 扫描 Plugins 文件夹，加载 DLL | `AssemblyLoadContext` | 2d |
| 实现插件隔离与卸载 | 使用 `AssemblyLoadContext` 实现热卸载 | 程序集加载上下文 | 1.5d |
| 实现插件配置管理 | 每个插件独立 config.json 读取 | 文件监控热重载 | 1d |
| 添加插件安全沙箱 | 限制插件代码权限（可选） | CAS 已过时，考虑 AppDomain 替代？ | 1d |
| UI 集成：插件管理界面 | 列出插件、启用/禁用、配置 | Avalonia 列表控件 | 1.5d |
| 编写示例插件 | 一个简单自定义策略插件 | 独立项目 | 1d |
| 测试插件加载/卸载/执行 | 集成测试 | 1d |

**关键技术细节**：

1. **AssemblyLoadContext 使用**：
   ```csharp
   public class PluginLoadContext : AssemblyLoadContext
   {
       private readonly AssemblyDependencyResolver _resolver;
       public PluginLoadContext(string pluginPath) { ... }
       protected override Assembly Load(AssemblyName assemblyName) { ... }
   }
   ```
   每个插件使用独立的 `AssemblyLoadContext` 实例，卸载时调用 `Unload()`。

2. **插件发现**：
   - 扫描 `Plugins/*/plugin.manifest.json`。
   - 验证清单中的入口类型是否实现了 `IPluginSeatingStrategy`。

3. **安全性考虑**：
   - 在 .NET Core 中代码访问安全性 (CAS) 已过时，采用进程级隔离较为复杂。初期可信任插件来源，或通过代码审查保障。后续可考虑将脚本插件限制 API（见 Phase 5）。

---

### Phase 5：脚本支持（2 周）

**目标**：支持 Lua 脚本作为策略插件。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 集成 NLua 库 | Lua 解释器嵌入 | NLua | 0.5d |
| 定义 Lua API 接口 | 暴露给脚本的工作区操作方法 | 注册 C# 函数 | 1.5d |
| 实现 LuaScriptStrategy | 封装 Lua 脚本执行 | 执行上下文、超时控制 | 1d |
| 实现脚本沙箱限制 | 禁用 IO/OS 库，限制内存/指令数 | Lua 环境裁剪 | 1d |
| 支持 C# 脚本（Roslyn） | 可选，使用 `CSharpScript` | Microsoft.CodeAnalysis.CSharp.Scripting | 1d |
| UI 集成：脚本编辑器 | 简单的文本编辑器与语法高亮 | Avalonia 文本框 | 1d |
| 测试脚本执行与沙箱 | 死循环、内存泄漏测试 | 单元测试 | 1d |

**关键技术细节**：

1. **NLua 环境裁剪**：
   - 创建 Lua 状态时不加载 `io`、`os`、`package` 等危险库。
   - 使用 `lua.State.Environment` 限制全局变量。

2. **脚本超时控制**：
   - 使用 `CancellationTokenSource` 设置超时。
   - 在 Lua 中定期调用 C# 检查钩子（`debug.sethook`）可能较复杂，初期可通过 `Task.WhenAny` 在 C# 侧超时取消。

3. **C# 脚本限制**：
   - 通过 `ScriptOptions` 限制引用的程序集和命名空间。
   - 禁用 `System.IO`、`System.Net` 等。

---

### Phase 6：高级布局与可视化（3-4 周）

**目标**：实现圆形、扇形、自由点布局，完善 UI 可视化与交互。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 设计布局定义文件格式 | `.venue.json` 结构 | JSON Schema | 1d |
| 实现 GridLayoutBuilder | 根据行列数生成网格座位 | 坐标计算 | 1d |
| 实现 PolarLayoutBuilder | 环形/扇形座位生成 | 极坐标转换 | 2d |
| 实现 FreeformLayoutBuilder | 从点列表生成座位 | 自定义坐标 | 1d |
| 添加障碍物处理 | 讲台、柱子占位 | 座位剔除逻辑 | 1d |
| UI 可视化：座位图渲染 | 根据布局类型绘制座位 | Avalonia 绘图 API | 3d |
| UI 交互：拖拽换位 | 手动微调座位 | 拖拽事件、命中测试 | 2d |
| UI 交互：缩放平移 | 视图导航 | `ScrollViewer` + 变换 | 1d |
| UI 交互：座位信息提示 | Tooltip 显示学生详情 | 自定义控件模板 | 1d |
| 实现命令模式的撤销/重做 | 支持拖拽操作的撤销 | `ICommand` 模式 | 1.5d |
| 测试布局生成与交互 | 集成测试 | 1d |

**关键技术细节**：

1. **PolarLayoutBuilder 算法**：
   - 输入：半径、起始角度、结束角度、圈数、每圈座位数。
   - 输出：每个座位的 `(X, Y)` 坐标。

2. **拖拽换位实现**：
   - 在 `PointerPressed` 中记录源座位。
   - 在 `PointerMoved` 中更新拖拽视觉效果。
   - 在 `PointerReleased` 中执行座位交换命令，压入命令栈。

3. **命令模式**：
   - 定义 `IUndoableCommand` 接口（`Execute` / `Unexecute`）。
   - 维护 `CommandHistory` 栈。

---

### Phase 7：配置管理、存储与版本迁移（2 周）

**目标**：完善配置文件体系，实现快照管理与版本迁移。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 实现 AppSettings 加载与监视 | 热重载配置 | `IOptionsMonitor` | 1d |
| 实现 Venue/Roster 文件仓储 | 读取/保存 JSON 文件 | 文件 I/O | 1d（已完成，含版本号字段）|
| 实现 SnapshotRepository | 保存/加载座位快照 | JSON 序列化 | 1d |
| 实现 ConfigMigrationService | 配置版本升级 | 迁移管线模式 | 1.5d（基础实现已完成：IFileMigrator + FileMigrationService）|
| 实现文件锁机制 | 防止多人同时编辑同一文件 | `FileStream` 锁 | 0.5d |
| 实现备份与恢复 | 自动备份损坏文件 | 文件复制 | 0.5d |
| 实现敏感数据加密 | 字段级/文件级加密 | AES-256, DPAPI | 1.5d |
| 测试配置迁移与加密 | 单元测试 | 1d |

**关键技术细节**：

1. **配置迁移管线**（基础实现已完成）：
   ```csharp
   public interface IFileMigrator
   {
       string FileType { get; }
       string FromVersion { get; }
       string ToVersion { get; }
       JsonNode Migrate(JsonNode root);
   }
   ```
   注册到 `FileMigrationService`，按版本号链式执行向前迁移。迁移器按文件类型组织在 `Migration/Migrators/{Type}Migrators.cs`。

2. **字段级加密**：
   - 使用 `[SensitiveData]` 特性标记属性。
   - 序列化前通过反射加密，反序列化后解密。

3. **密钥存储**：
   - Windows: `ProtectedData` (DPAPI)
   - Linux: `libsecret` 通过 `SecretService` 封装
   - macOS: Keychain

---

### Phase 8：CLI 工具与自动化（1-2 周）🔜 规划中

> [!NOTE]
> 此阶段尚未实现。当前 `SeatFlow.Cli` 项目不存在，命令行工具为后续规划功能。
> 实际开发路线图中 Phase 8 已调整为「测试覆盖、文档完善、打包发布」（详见 README.md）。

**目标**：提供命令行接口，支持无 UI 运行。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 创建 SeatFlow.Cli 控制台项目 | 独立可执行程序 | .NET 控制台应用 | 0.5d |
| 实现命令行参数解析 | 支持生成、导出、验证等命令 | `System.CommandLine` | 1d |
| 复用 Application 层逻辑 | 调用 IApplicationFacade | DI 配置 | 0.5d |
| 实现交互式向导 | 通过问答生成配置文件 | 控制台交互 | 1d |
| 编写 CLI 文档 | 帮助文本 | - | 0.5d |

---

### Phase 9：测试与文档（持续进行，最后集中 2 周）

**目标**：完善测试覆盖，编写用户手册与开发者文档。

**具体任务**：

| 任务 | 产出 | 技术点 | 预估工时 |
|------|------|--------|----------|
| 单元测试覆盖 Core/Application | 覆盖率 > 80% | xUnit, NSubstitute | 持续 |
| 集成测试关键流程 | 导入→生成→导出完整流程 | 测试夹具 | 3d |
| 快照测试 | 验证输出一致性 | Verify 库 | 1d |
| 性能基准测试 | 大型数据集策略执行时间 | BenchmarkDotNet | 1d |
| 编写用户手册 | 操作指南、配置说明 | Markdown | 2d |
| 编写开发者文档 | 插件开发指南 | Markdown | 2d |
| 本地化资源整理 | 中英文 .resx | - | 1d |

---

## 四、关键技术难点与解决方案

| 难点 | 解决方案 |
|------|----------|
| 跨平台 UI 一致性 | Avalonia 提供一致的渲染，在三个平台充分测试 |
| 插件热卸载 | 使用 `AssemblyLoadContext`，确保没有引用泄露，定期 GC |
| 大型布局渲染性能 | 虚拟化画布，只渲染可见区域座位（类似 `VirtualizingStackPanel`） |
| 复杂策略的性能 | 使用高效数据结构（如 `HashSet` 索引），并行执行无冲突策略 |
| 脚本安全 | Lua 裁剪环境 + 超时控制；C# 脚本限制引用 |
| 配置文件兼容性 | 版本迁移管线确保旧配置可升级 |
| 撤销/重做内存占用 | 命令栈记录增量变化，而非完整快照，最大深度限制 |

---

## 五、时间线与里程碑

| 阶段 | 内容 | 预计时间 | 里程碑产物 |
|------|------|----------|------------|
| Phase 1 | 领域建模与基础架构 | 2-3 周 | 可显示网格布局的原型 |
| Phase 2 | 数据加载与导出 | 2 周 | 支持导入导出 Excel/CSV |
| Phase 3 | 内置策略实现 | 3-4 周 | 完整自动排座功能 |
| Phase 4 | 插件系统 | 2-3 周 | 可加载外部 DLL 策略 |
| Phase 5 | 脚本支持 | 2 周 | Lua 脚本策略可用 |
| Phase 6 | 高级布局与可视化 | 3-4 周 | 圆形/扇形布局，拖拽交互 |
| Phase 7 | 配置管理与存储 | 2 周 | 快照、版本迁移、加密 |
| Phase 8 | CLI 工具 | 1-2 周 | 命令行版本可用 |
| Phase 9 | 测试与文档 | 2 周 | 发布就绪版本 |

**总预计时间**：约 **18-24 周**（4-6 个月），视团队规模与投入程度可调整。

---

## 六、风险与应对

| 风险 | 概率 | 影响 | 应对措施 |
|------|------|------|----------|
| Avalonia 在 Linux 下的稳定性问题 | 中 | 高 | 提前在目标发行版测试，参与社区修复 |
| 插件系统内存泄漏 | 中 | 中 | 使用 `WeakReference` 和内存分析工具监控 |
| 复杂布局算法有误 | 低 | 中 | 充分单元测试，提供手动微调补救 |
| 第三方库版本冲突 | 低 | 中 | 版本号直接在各 `.csproj` 中管理（项目未使用 `Directory.Packages.props`） |
| 性能不达标（大型数据集） | 中 | 高 | 早期引入性能基准测试，持续优化 |

---

## 七、交付物清单

1. **可执行程序**：
   - Windows (.msi / .zip)
   - macOS (.app / .pkg)
   - Linux (AppImage / .deb / .rpm)

2. **文档**：
   - 用户操作手册
   - 插件开发指南
   - 配置文件规范说明

3. **示例文件**：
   - 示例会场定义（网格、圆形、阶梯教室）
   - 示例学生名单
   - 示例插件源码

4. **测试报告**：
   - 单元测试覆盖率报告
   - 性能基准测试结果

---

以上即为详细的项目实现规划方案，可根据实际开发进度动态调整。