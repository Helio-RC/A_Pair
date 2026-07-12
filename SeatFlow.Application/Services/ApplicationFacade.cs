using System.Text.Json;
using SeatFlow.Application.Commands;
using SeatFlow.Application.Interfaces;
using SeatFlow.Application.Plugins;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Telemetry;
using SeatFlow.Core.Exporters;
using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Services;
using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;
using SeatFlow.Infrastructure.Layouts;
using SeatFlow.Infrastructure.Providers;
using SeatFlow.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Services
{
    /// <summary>
    /// 应用程序外观（Facade）的默认实现，协调数据加载、布局生成、策略执行、
    /// 冲突解决、快照管理等核心业务流程，为 UI/CLI 层提供统一的高层 API。
    /// </summary>
    /// <remarks>
    /// <see cref="ApplicationFacade"/> 封装了完整的座位编排工作流：
    /// <list type="number">
    ///   <item>加载学生数据（通过 <see cref="IStudentProvider"/>）</item>
    ///   <item>生成或加载座位布局（Grid / Polar / Freeform）</item>
    ///   <item>创建 <see cref="SeatingWorkspace"/> 工作区</item>
    ///   <item>获取内置策略与插件策略，按优先级排序后依次执行</item>
    ///   <item>通过 <see cref="IConflictResolver"/> 检测并自动修复冲突</item>
    ///   <item>保存 <see cref="SeatingSnapshot"/> 快照以支持回滚</item>
    /// </list>
    /// </remarks>
    public class ApplicationFacade (
        IServiceProvider serviceProvider ,
        ISeatingSnapshotRepository snapshotRepository ,
        IEnumerable<ISeatingPlanExporter> exporters ,
        IPluginManager pluginManager ,
        IPluginConfigurationService pluginConfigService ,
        IAppSettingsRepository appSettingsRepo ,
        IVenueRepository venueRepo ,
        IStudentDatasetRepository datasetRepo ,
        StrategyManifestProvider manifestProvider ,
        StrategyConfigFileRepository strategyConfigRepo ,
        StrategyDatasetConfigRepository datasetConfigRepo ,
        PluginPackageConfigService pluginPackageConfigService ,
        ISeatSetsService seatSetsService ,
        ILogger<ApplicationFacade> logger) : IApplicationFacade
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        private readonly ISeatingSnapshotRepository _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        private readonly IEnumerable<ISeatingPlanExporter> _exporters = exporters ?? throw new ArgumentNullException(nameof(exporters));
        private readonly IPluginManager _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        private readonly IPluginConfigurationService _pluginConfigService = pluginConfigService ?? throw new ArgumentNullException(nameof(pluginConfigService));
        private readonly CommandHistory _history = new();
        private readonly IAppSettingsRepository _appSettingsRepo = appSettingsRepo ?? throw new ArgumentNullException(nameof(appSettingsRepo));
        private readonly IVenueRepository _venueRepo = venueRepo ?? throw new ArgumentNullException(nameof(venueRepo));
        private readonly IStudentDatasetRepository _datasetRepo = datasetRepo ?? throw new ArgumentNullException(nameof(datasetRepo));
        private readonly StrategyManifestProvider _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
        private readonly StrategyConfigFileRepository _strategyConfigRepo = strategyConfigRepo ?? throw new ArgumentNullException(nameof(strategyConfigRepo));
        private readonly StrategyDatasetConfigRepository _datasetConfigRepo = datasetConfigRepo ?? throw new ArgumentNullException(nameof(datasetConfigRepo));
        private readonly PluginPackageConfigService _pluginPackageConfigService = pluginPackageConfigService ?? throw new ArgumentNullException(nameof(pluginPackageConfigService));
        private readonly ISeatSetsService _seatSetsService = seatSetsService ?? throw new ArgumentNullException(nameof(seatSetsService));
        private SeatingWorkspace? _currentWorkspace;
        // 注意：以下字段假定单线程访问（桌面 UI 线程）。
        // 并发调用 GenerateSeatingAsync 等操作不受支持。
        private ClassroomLayoutDefinition? _currentLayout;
        // 注意：以下缓存假设 DI 容器在应用生命周期内保持稳定。
        // 若运行时热加载了新策略/插件，需调用对应刷新方法重建缓存。
        private List<ISeatingStrategy>? _cachedStrategies;
        private List<IDependentSeatingStrategy>? _cachedDependentStrategies;

        // GetStrategiesAsync 短期缓存，避免侧栏切换时频繁 I/O 和 DI 解析
        private List<StrategyDisplayInfo>? _cachedStrategyDisplayInfos;
        private DateTime _cachedStrategyDisplayInfosAt = DateTime.MinValue;
        private static readonly TimeSpan StrategyDisplayCacheDuration = TimeSpan.FromSeconds(30);

        /// <inheritdoc />
        public Task<AppConfiguration> LoadConfigurationAsync (string path , CancellationToken cancellationToken = default)
            => Task.FromResult(new AppConfiguration());

        /// <inheritdoc />
        public Task<AppSettings> LoadAppSettingsAsync (CancellationToken cancellationToken = default)
            => _appSettingsRepo.LoadAsync(cancellationToken);

        /// <inheritdoc />
        public Task SaveAppSettingsAsync (AppSettings settings , CancellationToken cancellationToken = default)
            => _appSettingsRepo.SaveAsync(settings , cancellationToken);

        /// <inheritdoc />
        public Task SaveVenueAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default)
            => _venueRepo.SaveAsync(venueId , layout , cancellationToken);

        /// <inheritdoc />
        public Task<ClassroomLayoutDefinition?> LoadVenueAsync (string venueId , CancellationToken cancellationToken = default)
            => _venueRepo.LoadAsync(venueId , cancellationToken);

        /// <inheritdoc />
        public Task<string?> GetVenueHashAsync (string venueId , CancellationToken ct = default)
            => _venueRepo.GetContentHashAsync(venueId , ct);

        /// <inheritdoc />
        public Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default)
            => _venueRepo.ListVenueIdsAsync(cancellationToken);

        /// <inheritdoc />
        public Task DeleteVenueAsync (string venueId , CancellationToken cancellationToken = default)
            => _venueRepo.DeleteAsync(venueId , cancellationToken);

        /// <inheritdoc />
        public async Task<List<Student>> LoadStudentsAsync (string source , CancellationToken cancellationToken = default)
        {
            var provider = _serviceProvider.GetService<IStudentProvider>();
            if (provider == null) return new List<Student>();
            return await provider.LoadAsync(source , cancellationToken);
        }

        /// <inheritdoc />
        public async Task ExportStudentsAsync (string path , IEnumerable<Student> students , ExportFormat format , CancellationToken cancellationToken = default)
        {
            var writers = _serviceProvider.GetServices<IStudentWriter>();
            IStudentWriter writer = format switch
            {
                ExportFormat.Excel => writers.OfType<XlsxStudentWriter>().FirstOrDefault()
                    ?? throw new InvalidOperationException("未注册 XlsxStudentWriter"),
                ExportFormat.Csv => writers.OfType<CsvStudentWriter>().FirstOrDefault()
                    ?? throw new InvalidOperationException("未注册 CsvStudentWriter"),
                ExportFormat.Json => writers.OfType<JsonStudentWriter>().FirstOrDefault()
                    ?? throw new InvalidOperationException("未注册 JsonStudentWriter"),
                _ => throw new NotSupportedException($"不支持的导出格式: {format}")
            };
            await writer.WriteAsync(path , students , cancellationToken);
        }

        /// <inheritdoc />
        public async Task<SeatingWorkspace> GenerateSeatingAsync (
            SeatingRequest request ,
            IProgress<SeatingProgress>? progress = null ,
            CancellationToken cancellationToken = default)
        {
            logger.LogInformation("开始生成座位：数据集={DataSource}，会场={VenueId}" ,
                request.StudentDataSource ?? "当前", request.LayoutId ?? "当前");

            // 1. 加载学生数据
            var studentProvider = _serviceProvider.GetService<IStudentProvider>();
            var students = studentProvider == null
                ? new List<Student>()
                : await studentProvider.LoadAsync(request.StudentDataSource ?? string.Empty , cancellationToken);

            // 2. 生成座位布局
            List<Seat> seats;
            ClassroomLayoutDefinition? venueLayout = null;
            if (!string.IsNullOrEmpty(request.LayoutId))
            {
                // 从已保存的会场加载布局
                venueLayout = await _venueRepo.LoadAsync(request.LayoutId , cancellationToken);
                _currentLayout = venueLayout;
                if (venueLayout != null)
                {
                    seats = venueLayout.Seats;
                    // 应用障碍物 (已在保存时处理，但为确保安全再次处理)
                    ObstacleProcessor.ApplyObstacles(venueLayout);
                }
                else
                {
                    seats = new List<Seat>();
                }
            }
            else
            {
                seats = BuildSeatsFromRequest(request);
            }

            // 3. 创建工作区
            var workspace = new SeatingWorkspace(students , seats ,
                _serviceProvider.GetService<ILogger<SeatingWorkspace>>());
            _currentWorkspace = workspace;

            // 3b. 清理已删除数据源的策略配置（避免无效配置残留磁盘）
            if (!string.IsNullOrEmpty(request.LayoutId))
                await CleanupOrphanedDatasetConfigsAsync(cancellationToken);

            // 3c. 从历史快照恢复前排历史，实现跨会话轮换
            if (!string.IsNullOrEmpty(request.LayoutId))
            {
                var frontRowStrategy = _serviceProvider
                    .GetServices<ISeatingStrategy>().OfType<FrontRowRotationStrategy>().FirstOrDefault();
                int windowSize = frontRowStrategy?.Config.HistoryWindowSize ?? 10;
                var historyLoader = _serviceProvider.GetRequiredService<FrontRowHistoryLoader>();
                await historyLoader.PopulateFrontRowHistoryAsync(
                    workspace , request.LayoutId , windowSize , cancellationToken);

                // 3d. 加载同桌不重复历史（过去的同桌对）
                var noRepeat = _serviceProvider.GetServices<IDependentSeatingStrategy>()
                    .OfType<NoRepeatDeskMateStrategy>().FirstOrDefault();
                if (noRepeat != null && noRepeat.IsEnabled)
                {
                    var ndLoader = _serviceProvider.GetRequiredService<NoRepeatDeskMateHistoryLoader>();
                    await ndLoader.PopulateDeskMateHistoryAsync(
                        workspace , request.LayoutId , noRepeat.Config.HistoryWindowSize , noRepeat , cancellationToken);
                }
            }

            // 4. 获取内置策略（缓存 - 内置策略，不变异）
            var builtInStrategies = _cachedStrategies ??= [.. _serviceProvider.GetServices<ISeatingStrategy>()];

            // 4b. 获取内置依赖策略（提前解析，供配置恢复和后续步骤共用）
            var builtInDependents = _cachedDependentStrategies ??= [.. _serviceProvider.GetServices<IDependentSeatingStrategy>()];

            // 4c. 恢复持久化的策略配置（Priority、IsEnabled、Parameters），覆盖重启后默认值
            await RestorePersistedStrategyConfigsAsync(
                builtInStrategies , builtInDependents , cancellationToken);

            // 5. 加载插件策略并适配（加入独立列表，不污染缓存）
            var strategies = new List<ISeatingStrategy>(builtInStrategies);
            var loadedPlugins = await _pluginManager.LoadStrategyPluginsAsync(cancellationToken);
            foreach (var pluginInfo in loadedPlugins)
            {
                if (pluginInfo.Strategy.IsEnabled)
                {
                    var adapter = new PluginStrategyAdapter(pluginInfo.Strategy);
                    strategies.Add(adapter);
                }
            }

            // 5b. 排除标记为不可见（visible=false）的策略
            var invisibleIds = _manifestProvider.GetBuiltInManifests()
                .Where(m => !m.Visible)
                .Select(m => m.Id)
                .ToHashSet();
            foreach (var pi in loadedPlugins)
            {
                if (pi.StrategyManifest is { Visible: false })
                    invisibleIds.Add(pi.StrategyManifest.Id);
            }
            strategies = [.. strategies.Where(s => !invisibleIds.Contains(s.Id))];

            // 6. 按请求过滤策略
            if (!request.UseDefaultStrategies && request.StrategyIds.Count != 0)
                strategies = [.. strategies.Where(s => request.StrategyIds.Contains(s.Id))];

            // 6b. 同步 FrontRowCount / SeatsPerDesk 到策略配置
            if (venueLayout?.Metadata is GridLayoutMetadata gridMeta)
            {
                var frontRowStrategy = strategies.OfType<FrontRowRotationStrategy>().FirstOrDefault();
                frontRowStrategy?.SetFrontRowCount(gridMeta.FrontRowCount);

                var deskMate = _serviceProvider.GetServices<IDependentSeatingStrategy>()
                    .OfType<DeskMateStrategy>().FirstOrDefault();
                deskMate?.SetSeatsPerDesk(gridMeta.SeatsPerDesk);

                var noRepeat = _serviceProvider.GetServices<IDependentSeatingStrategy>()
                    .OfType<NoRepeatDeskMateStrategy>().FirstOrDefault();
                noRepeat?.SetSeatsPerDesk(gridMeta.SeatsPerDesk);
            }
            else if (venueLayout?.Metadata is PolarLayoutMetadata polarMeta)
            {
                var frontRowStrategy = strategies.OfType<FrontRowRotationStrategy>().FirstOrDefault();
                frontRowStrategy?.SetFrontRowCount(polarMeta.FrontRowCount);
            }

            // 6c. 加载代码块配置并应用到 FixedSeat / DeskMate 策略
            await ApplyCodeBlockConfigsAsync(strategies , request , venueLayout , cancellationToken);

            // 6c-b. 收集约束学生 ID 并注入 DefragStrategy（固定座位 + DeskMate 组）
            var defragStrategy = strategies.OfType<DefragStrategy>().FirstOrDefault();
            if (defragStrategy != null && defragStrategy.IsEnabled)
            {
                var constrainedIds = new HashSet<string>();
                // 固定座位上的学生
                foreach (var seat in workspace.FindSeats(s => s.IsFixed && s.OccupantId is not null))
                    constrainedIds.Add(seat.OccupantId!);
                // DeskMate 组内学生
                var deskMate = _serviceProvider.GetServices<IDependentSeatingStrategy>()
                    .OfType<DeskMateStrategy>().FirstOrDefault();
                if (deskMate != null)
                {
                    foreach (var group in deskMate.Config.Groups)
                    {
                        foreach (var sid in group.StudentIds)
                            constrainedIds.Add(sid);
                    }
                }
                defragStrategy.SetConstrainedStudentIds(constrainedIds);
                logger.LogInformation("Defrag：已注入 {Count} 个约束学生 ID" , constrainedIds.Count);
            }

            // 6d. 收集依赖策略并注入到 RandomFill
            var dependentStrategies = new List<IDependentSeatingStrategy>();

            // 收集内置依赖策略（复用步骤 4b 中已解析的实例），排除标记为不可见的策略
            var visibleDependents = builtInDependents
                .Where(d => !invisibleIds.Contains(d.Id))
                .ToList();
            dependentStrategies.AddRange(visibleDependents);

            // 收集插件依赖策略（IsIndependent == false 的插件）
            foreach (var pi in loadedPlugins)
            {
                if (pi.StrategyManifest is { IsIndependent: false } && pi.Strategy.IsEnabled)
                {
                    // TODO: 插件依赖策略适配器 — 当前插件无法实现 EvaluateAsync，默认 Approve
                    logger.LogWarning("插件依赖策略 {PluginId} 尚不支持 EvaluateAsync，将默认批准所有分配" , pi.Strategy.Id);
                }
            }

            // 注入依赖策略到 RandomFill
            var randomFill = strategies.OfType<RandomFillStrategy>().FirstOrDefault();
            if (randomFill != null && dependentStrategies.Count > 0)
            {
                randomFill.LoadDependentStrategies(dependentStrategies);
                logger.LogInformation("已将 {Count} 个依赖策略注入 RandomFill" , dependentStrategies.Count);
            }

            // 6e. 注册策略能力到 workspace（从 manifest 读取）
            foreach (var s in strategies)
            {
                var m = _manifestProvider.GetBuiltInManifest(s.Id);
                if (m?.Capabilities is { Count: > 0 })
                    workspace.RegisterCapabilities(s.Id , m.Capabilities);
            }
            foreach (var pi in loadedPlugins)
            {
                if (pi.StrategyManifest?.Capabilities is { Count: > 0 })
                    workspace.RegisterCapabilities(pi.Strategy.Id , pi.StrategyManifest.Capabilities);
            }

            // 7. 执行策略管道
            var pipeline = new StrategyExecutionPipeline(strategies);
            var plan = await pipeline.ExecuteAsync(workspace , progress , cancellationToken);

            // 8. 解决冲突
            var conflictResolver = _serviceProvider.GetService<IConflictResolver>();
            if (conflictResolver != null)
            {
                var conflictResult = conflictResolver.Resolve(workspace);
                if (!conflictResult.Success)
                {
                    progress?.Report(new SeatingProgress
                    {
                        CurrentStep = 1 ,
                        TotalSteps = 1 ,
                        StatusMessage = $"检测到 {conflictResult.Conflicts.Count} 个冲突，已自动处理"
                    });
                }
            }

            // 9. 快照不再自动保存，由用户手动触发 SaveToSnapshot 命令
            // CreateSnapshotAsync 会在保存时生成完整的元数据（哈希、嵌入会场等）

            progress?.Report(new SeatingProgress
            {
                CurrentStep = 1 ,
                TotalSteps = 1 ,
                StatusMessage = "座位生成完成"
            });

            // 记录排座生成遥测
            try
            {
                var telemetry = _serviceProvider.GetService<ITelemetryService>();
                telemetry?.RecordSeatingGeneration(
                    success: true,
                    studentCount: students.Count,
                    venueCount: seats.Count,
                    strategyCount: strategies.Count);
            }
            catch { /* 遥测未注册时静默处理 */ }

            return workspace;
        }

        /// <inheritdoc />
        public async Task ExportSeatingPlanAsync (
    SeatingWorkspace workspace ,
    ClassroomLayoutDefinition? layout ,
    string path ,
    ExportOptions options ,
    CancellationToken cancellationToken = default)
        {
            logger.LogInformation("开始导出座位：格式={Format}，路径={Path}" , options.Format , path);
            ISeatingPlanExporter? exporter = _exporters.FirstOrDefault(e => e.Format == options.Format) ?? throw new NotSupportedException($"No exporter registered for format {options.Format}.");
            if (layout != null)
            {
                var assignments = workspace.BuildSeatingPlan().Assignments;
                var studentNames = workspace.Students.ToDictionary(s => s.Id , s => s.Name);
                var model = LayoutSeatingExportModel.FromLayout(layout , assignments , studentNames);
                // 教师视角：行前后反转（讲台移至底部）+ 列左右镜像（教师左侧对应学生右侧）
                if (options.Perspective == LayoutPerspective.TeacherView)
                {
                    model.Rows.Reverse();
                    foreach (var row in model.Rows)
                        row.Cells.Reverse();
                }
                await exporter.ExportLayoutAsync(model , path , options , cancellationToken);
            }
            else
            {
                var plan = workspace.BuildSeatingPlan();
                await exporter.ExportAsync(plan , path , options , cancellationToken);
            }
            logger.LogInformation("座位导出完成：格式={Format}，路径={Path}" , options.Format , path);

            // 记录导出遥测
            try
            {
                var telemetry = _serviceProvider.GetService<ITelemetryService>();
                telemetry?.RecordExport(options.Format.ToString() , success: true);
            }
            catch { /* 遥测未注册时静默处理 */ }
        }

        /// <inheritdoc />
        public async Task<bool> ExecuteCommandAsync (IUndoableCommand command , CancellationToken cancellationToken = default , bool recordInHistory = true)
        {
            if (_currentWorkspace == null) return false;

            if (!recordInHistory)
            {
                // 直接在工作区上执行命令，不记录到 CommandHistory，避免与 ViewModel 的快照历史双重累积
                return await command.ExecuteAsync(_currentWorkspace , cancellationToken);
            }

            return await _history.ExecuteAsync(command , _currentWorkspace , cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> UndoAsync (CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.UndoAsync(_currentWorkspace , cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RedoAsync (CancellationToken cancellationToken = default)
        {
            if (_currentWorkspace == null) return Task.FromResult(false);
            return _history.RedoAsync(_currentWorkspace , cancellationToken);
        }

        /// <inheritdoc />
        public Task<SeatingWorkspace?> GetCurrentWorkspaceAsync (CancellationToken cancellationToken = default)
            => Task.FromResult(_currentWorkspace);

        /// <inheritdoc />
        public Task<ClassroomLayoutDefinition?> GetCurrentLayoutAsync (CancellationToken cancellationToken = default)
            => Task.FromResult(_currentLayout);

        /// <inheritdoc />
        public void ClearWorkspace ()
        {
            _currentWorkspace = null;
            _currentLayout = null;
        }

        /// <inheritdoc />
        public async Task<SeatingWorkspace> CreateEmptyWorkspaceAsync (
            string layoutId , string datasetId , CancellationToken cancellationToken = default)
        {
            // 1. 加载会场布局
            var layout = await _venueRepo.LoadAsync(layoutId , cancellationToken);
            _currentLayout = layout;

            // 2. 加载学生数据
            var students = await _datasetRepo.LoadAsync(datasetId , cancellationToken) ?? [];

            // 3. 获取座位列表
            List<Seat> seats;
            if (layout != null)
            {
                seats = layout.Seats;
                ObstacleProcessor.ApplyObstacles(layout);
            }
            else
            {
                seats = [];
            }

            // 4. 创建工作区（不执行策略管道）
            var workspace = new SeatingWorkspace(students , seats ,
                _serviceProvider.GetService<ILogger<SeatingWorkspace>>());
            _currentWorkspace = workspace;

            logger.LogDebug("空白工作区已创建：{LayoutId}，{StudentCount} 学生，{SeatCount} 座位" ,
                layoutId , students.Count , seats.Count);

            return workspace;
        }

        /// <summary>轮转旧快照：超出上限删除最旧的。</summary>
        private async Task RotateSnapshotsAsync (string venueId , CancellationToken ct)
        {
            var settings = await _appSettingsRepo.LoadAsync(ct);
            int max = settings.MaxSnapshotsPerVenue;
            if (max <= 0) return;

            var snapshots = (await _snapshotRepository.ListByVenueAsync(venueId , ct))
                .OrderBy(s => s.CreatedAt).ToList();
            while (snapshots.Count > max)
            {
                try { await _snapshotRepository.DeleteAsync(snapshots[0].Id , ct); }
                catch (Exception ex) { logger.LogWarning(ex , "快照轮转删除失败：{Id}" , snapshots[0].Id); }
                snapshots.RemoveAt(0);
            }
        }

        /// <summary>检查快照关联会场的完整性。返回 (exists, hashMatch)。</summary>
        public async Task<(bool Exists , bool HashMatch)> CheckVenueIntegrityAsync (
            string venueId , string? snapshotVenueHash , CancellationToken ct = default)
        {
            var curHash = await _venueRepo.GetContentHashAsync(venueId , ct);
            if (curHash == null) return (false , false); // 会场文件不存在
            if (snapshotVenueHash == null) return (true , true); // 旧快照无哈希，默认匹配
            return (true , curHash == snapshotVenueHash);
        }

        /// <summary>将快照中嵌入的会场布局恢复/导入为会场文件。</summary>
        public async Task<string> ImportVenueFromSnapshotAsync (
            string venueLayoutJson , string? newName = null , CancellationToken ct = default)
        {
            var layout = SnapshotLayoutHelper.DeserializeVenueFromEmbeddedJson(venueLayoutJson)
                ?? throw new InvalidOperationException("无法反序列化快照中的会场布局");
            var venueId = Guid.NewGuid().ToString("N")[..8];
            layout.Id = venueId;
            if (newName != null) layout.Name = newName;
            await _venueRepo.SaveAsync(venueId , layout , ct);
            return venueId;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync (string venueId , CancellationToken cancellationToken = default)
        {
            // 从存储库中按 venueId 过滤快照
            return await _snapshotRepository.ListByVenueAsync(venueId , cancellationToken);
        }

        /// <inheritdoc />
        public async Task DeleteSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default)
            => await _snapshotRepository.DeleteAsync(snapshotId , cancellationToken);

        /// <inheritdoc />
        public bool HasActiveWorkspace => _currentWorkspace != null;

        /// <inheritdoc />
        public async Task<SeatingSnapshot?> CreateSnapshotAsync (string description , CancellationToken cancellationToken = default)
        {
            logger.LogInformation("开始创建快照：描述={Description}" , description);
            if (_currentWorkspace == null) return null;

            var plan = _currentWorkspace.BuildSeatingPlan();
            var studentNames = _currentWorkspace.Students
                .Where(s => plan.Assignments.ContainsValue(s.Id))
                .ToDictionary(s => s.Id , s => s.Name);
            var studentHash = SeatFlow.Infrastructure.Utils.ContentHashHelper.ComputeSha256(
                string.Concat(_currentWorkspace.Students.Where(s => plan.Assignments.ContainsValue(s.Id)).OrderBy(s => s.Id).Select(s => $"{s.Id}|{s.Name}")));
            var snapshotMeta = new Dictionary<string , object> { ["studentNames"] = studentNames , ["studentHash"] = studentHash };
            var venueId = _currentLayout?.Id;
            if (!string.IsNullOrEmpty(venueId))
            {
                var vh = await _venueRepo.GetContentHashAsync(venueId , cancellationToken);
                if (vh != null) snapshotMeta["venueHash"] = vh;
                var rawVenueJson = await _venueRepo.GetRawVenueFileAsync(venueId , cancellationToken);
                if (rawVenueJson != null)
                    snapshotMeta["venueFile"] = System.Text.Json.Nodes.JsonNode.Parse(rawVenueJson)!;
            }
            var snapshot = new SeatingSnapshot
            {
                Description = description ,
                LayoutId = venueId ?? (plan.Assignments.Count > 0 ? "current" : "empty") ,
                SeatAssignments = plan.Assignments ,
                Metadata = snapshotMeta
            };
            await _snapshotRepository.SaveAsync(snapshot , cancellationToken);
            if (!string.IsNullOrEmpty(venueId))
                await RotateSnapshotsAsync(venueId , cancellationToken);
            logger.LogInformation("快照已创建：{SnapshotId}，{Count} 条分配" , snapshot.Id , snapshot.SeatAssignments.Count);
            return snapshot;
        }

        /// <inheritdoc />
        public async Task RollbackToSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default)
        {
            logger.LogInformation("开始回滚快照：{SnapshotId}" , snapshotId);
            var snapshot = await _snapshotRepository.LoadAsync(snapshotId , cancellationToken) ?? throw new InvalidOperationException($"Snapshot {snapshotId} not found");

            // 回滚前自动保存当前状态为备份快照，确保可撤销
            if (_currentWorkspace != null)
            {
                try { await CreateSnapshotAsync($"回滚前的自动备份 - {DateTime.Now:yyyy-MM-dd HH:mm}" , cancellationToken); } catch { }
            }

            // 优先使用快照中嵌入的会场布局（自包含，不依赖外部会场文件）
            ClassroomLayoutDefinition? layout = null;
            var venueFileJson = SnapshotLayoutHelper.GetMetaStringFromMetadata(snapshot.Metadata , "venueFile")
                ?? SnapshotLayoutHelper.GetMetaStringFromMetadata(snapshot.Metadata , "venueLayout");
            if (!string.IsNullOrEmpty(venueFileJson))
                layout = SnapshotLayoutHelper.DeserializeVenueFromEmbeddedJson(venueFileJson);

            // 无嵌入时回退到加载会场文件
            if (layout == null
                && !string.IsNullOrEmpty(snapshot.LayoutId)
                && snapshot.LayoutId != "unknown"
                && snapshot.LayoutId != "empty"
                && snapshot.LayoutId != "current")
            {
                try { layout = await venueRepo.LoadAsync(snapshot.LayoutId , cancellationToken); } catch { }
            }

            _currentLayout = layout;

            var seats = layout?.Seats ?? new List<Seat>();
            var studentIds = snapshot.SeatAssignments.Values
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();
            var students = await BuildStudentsForSnapshotAsync(studentIds , cancellationToken);

            _currentWorkspace = new SeatingWorkspace(students , seats);
            _currentWorkspace.ApplySnapshotAssignments(snapshot.SeatAssignments);
            logger.LogInformation("快照回滚完成：{SnapshotId}，{StudentCount} 学生，{SeatCount} 座位" ,
                snapshotId , students.Count , seats.Count);
        }

        /// <summary>
        /// 从已保存的学生数据集中尽可能匹配真实学生信息，无法匹配的用 ID 作为名称的存根学生。
        /// </summary>
        private async Task<List<Student>> BuildStudentsForSnapshotAsync (List<string> studentIds , CancellationToken ct)
        {
            if (studentIds.Count == 0)
                return [];

            var idSet = new HashSet<string>(studentIds);
            var studentMap = new Dictionary<string , Student>();

            // 尝试从所有已保存的学生数据集加载真实数据
            try
            {
                var datasets = await _datasetRepo.ListAsync(ct);
                foreach (var ds in datasets)
                {
                    var loaded = await _datasetRepo.LoadAsync(ds.Id , ct);
                    if (loaded != null)
                    {
                        foreach (var s in loaded)
                        {
                            if (idSet.Contains(s.Id) && !studentMap.ContainsKey(s.Id))
                                studentMap[s.Id] = s;
                        }
                    }
                }
            }
            catch { /* 数据集读取失败不影响回滚 */ }

            return [.. studentIds.Select(id =>
                studentMap.TryGetValue(id , out var real) ? real : new Student { Id = id , Name = id } )];
        }

        public async Task<string> SaveStudentDatasetAsync (string name , List<Student> students , string? originalFileName = null , CancellationToken ct = default)
        {
            var id = Guid.NewGuid().ToString("N");
            await _datasetRepo.SaveAsync(id , name , students , originalFileName , ct);
            return id;
        }

        public Task UpdateStudentDatasetAsync (string id , string name , List<Student> students , string? originalFileName = null , CancellationToken ct = default)
        {
            // 与 SaveStudentDatasetAsync 的关键区别：不生成新 GUID，直接使用传入的 id 覆写已有文件
            return _datasetRepo.SaveAsync(id , name , students , originalFileName , ct);
        }

        public Task<List<Student>?> LoadStudentDatasetAsync (string id , CancellationToken ct = default)
            => _datasetRepo.LoadAsync(id , ct);

        public Task<IReadOnlyList<StudentDatasetInfo>> ListStudentDatasetsAsync (CancellationToken ct = default)
            => _datasetRepo.ListAsync(ct);

        public Task DeleteStudentDatasetAsync (string id , CancellationToken ct = default)
            => _datasetRepo.DeleteAsync(id , ct);

        /// <inheritdoc />
        public Task RenameStudentDatasetAsync (string id , string newName , CancellationToken ct = default)
            => _datasetRepo.RenameAsync(id , newName , ct);

        /// <inheritdoc />
        public async Task<List<StrategyDisplayInfo>> GetStrategiesAsync (CancellationToken ct = default)
        {
            // 短期缓存：侧栏频繁切换时避免重复 I/O 和 DI 解析
            if (_cachedStrategyDisplayInfos is not null
                && DateTime.Now - _cachedStrategyDisplayInfosAt < StrategyDisplayCacheDuration)
            {
                logger.LogDebug("GetStrategiesAsync：返回缓存结果（{Age:F0}s 前）" ,
                    (DateTime.Now - _cachedStrategyDisplayInfosAt).TotalSeconds);
                return _cachedStrategyDisplayInfos;
            }

            var persisted = await _strategyConfigRepo.LoadAllAsync(ct);
            var result = new List<StrategyDisplayInfo>();

            // 收集内置策略（Manifest + 运行时实例配置）
            var builtInManifests = _manifestProvider.GetBuiltInManifests();
            var builtInInstances = _cachedStrategies ??= [.. _serviceProvider.GetServices<ISeatingStrategy>()];
            var builtInDependents = _serviceProvider.GetServices<IDependentSeatingStrategy>().ToList();

            foreach (var manifest in builtInManifests)
            {
                // 优先从独立策略查找，其次从依赖策略查找
                var runtimeStrategy = builtInInstances.FirstOrDefault(s => s.Id == manifest.Id);
                var depStrategy = builtInDependents.FirstOrDefault(d => d.Id == manifest.Id);

                var info = BuildDisplayInfo(manifest , "builtin" , persisted , runtimeStrategy , depStrategy);
                result.Add(info);
            }

            // 收集插件策略
            var loadedPlugins = await _pluginManager.LoadStrategyPluginsAsync(ct);
            foreach (var pi in loadedPlugins)
            {
                var (pkg , _) = _pluginManager.FindStrategy(pi.Strategy.Id);
                var category = pkg?.PackageManifest?.Type ?? "strategy";
                var sm = pi.StrategyManifest;

                var pluginManifest = new StrategyManifest
                {
                    Id = pi.Strategy.Id ,
                    Name = pi.Strategy.Name ,
                    DisplayName = pi.Strategy.Name ,
                    Version = pkg?.PackageManifest?.Version ?? "1.0.0" ,
                    Description = pkg?.PackageManifest?.Description ?? string.Empty ,
                    Author = pkg?.PackageManifest?.Author ?? string.Empty ,
                    Category = category ,
                    DefaultPriority = sm?.DefaultPriority ?? pi.Strategy.Priority ,
                    DefaultEnabled = pi.Strategy.IsEnabled ,
                    Parameters = sm?.Parameters ,
                    CodeBlocks = sm?.CodeBlocks ,
                    Messages = sm?.Messages ,
                    Visible = sm?.Visible ?? true ,
                    IsIndependent = sm?.IsIndependent ?? true ,
                    ManifestVersion = sm?.ManifestVersion ?? "1.0"
                };

                var source = $"plugin:{pi.Strategy.Id}";
                var info = BuildDisplayInfo(pluginManifest , source , persisted , null , null);
                result.Add(info);
            }

            // 独立策略和依赖策略按各自的 Priority 分组排序，不交叉比较
            var independents = result.Where(d => d.IsIndependent).OrderByDescending(d => d.Priority).ToList();
            var dependents = result.Where(d => !d.IsIndependent).OrderByDescending(d => d.Priority).ToList();
            var indCount = independents.Count;
            var depCount = dependents.Count;
            // 独立策略在前（外部管道），依赖策略在后（将被 ViewModel 嵌套到宿主下）
            independents.AddRange(dependents);
            logger.LogInformation("加载策略列表：内置 {BuiltIn} 个，插件 {Plugin} 个，独立 {Ind} 个，依赖 {Dep} 个" ,
                builtInManifests.Count , loadedPlugins.Count() , indCount , depCount);
            _cachedStrategyDisplayInfos = independents;
            _cachedStrategyDisplayInfosAt = DateTime.Now;
            return independents;
        }

        /// <inheritdoc />
        public async Task SaveStrategyConfigAsync (string strategyId , StrategyConfig config , CancellationToken ct = default)
        {
            _cachedStrategyDisplayInfos = null; // 使 GetStrategiesAsync 缓存失效
            // 如果 Parameters 为 null（仅保存优先级/开关），保留已有参数
            if (config.Parameters == null)
            {
                var existing = await _strategyConfigRepo.LoadAsync(strategyId , ct);
                config.Parameters = existing?.Parameters ?? [];
            }

            logger.LogInformation("保存策略配置：{Id}，优先级 {Priority}，启用 {Enabled}" ,
                strategyId , config.Priority , config.IsEnabled);

            // 配置路由：插件策略 → PluginPackageConfigService，内置策略 → StrategyConfigFileRepository
            var (pkg , _) = _pluginManager.FindStrategy(strategyId);
            if (pkg != null)
            {
                await _pluginPackageConfigService.SaveConfigAsync(strategyId , config , ct);
            }
            else
            {
                await _strategyConfigRepo.SaveAsync(strategyId , config , ct);
            }

            var builtInInstances = _cachedStrategies ??= [.. _serviceProvider.GetServices<ISeatingStrategy>()];
            var strategy = builtInInstances.FirstOrDefault(s => s.Id == strategyId);
            if (strategy is not null)
            {
                ApplyPersistedConfigToInstance(config , strategy , null);
                return;
            }

            // 尝试从依赖策略查找（缓存，与独立策略对称）
            var cachedDeps = _cachedDependentStrategies ??= [.. _serviceProvider.GetServices<IDependentSeatingStrategy>()];
            var depStrategy = cachedDeps.FirstOrDefault(d => d.Id == strategyId);
            if (depStrategy is not null)
            {
                ApplyPersistedConfigToInstance(config , null , depStrategy);
            }

            // 尝试从插件策略更新运行时状态
            if (pkg != null)
            {
                var pluginInfo = _pluginManager.GetLoadedPlugin(strategyId);
                if (pluginInfo is not null)
                {
                    pluginInfo.Strategy.Priority = config.Priority;
                    pluginInfo.Strategy.IsEnabled = config.IsEnabled;
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<StrategyDatasetConfig>> LoadStrategyDatasetConfigsAsync (string strategyId , CancellationToken ct = default)
        {
            var (pkg , _) = _pluginManager.FindStrategy(strategyId);
            if (pkg != null)
                return await _pluginPackageConfigService.LoadDatasetConfigsAsync(strategyId , ct);
            return await _datasetConfigRepo.LoadAllAsync(strategyId , ct);
        }

        private static readonly JsonSerializerOptions StudentHashOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase ,
            WriteIndented = true
        };

        /// <inheritdoc />
        public async Task SaveStrategyDatasetConfigAsync (StrategyDatasetConfig config , CancellationToken ct = default)
        {
            string? studentHash = null;

            if (!string.IsNullOrEmpty(config.DatasetId))
            {
                var students = await _datasetRepo.LoadAsync(config.DatasetId , ct);
                if (students is { Count: > 0 })
                {
                    var sorted = students.OrderBy(s => s.Id).ToList();
                    var json = JsonSerializer.Serialize(sorted , StudentHashOptions);
                    studentHash = Infrastructure.Utils.ContentHashHelper.ComputeSha256(json);
                }
            }

            string? venueHash = null;
            if (!string.IsNullOrEmpty(config.VenueId))
                venueHash = await _venueRepo.GetContentHashAsync(config.VenueId , ct);

            // 配置路由
            var (pkg , _) = _pluginManager.FindStrategy(config.StrategyId);
            if (pkg != null)
                await _pluginPackageConfigService.SaveDatasetConfigAsync(config , studentHash , venueHash , ct);
            else
                await _datasetConfigRepo.SaveAsync(config , studentHash , venueHash , ct);
        }

        /// <inheritdoc />
        public async Task DeleteStrategyDatasetConfigAsync (string strategyId , string datasetId , string? venueId , CancellationToken ct = default)
        {
            var (pkg , _) = _pluginManager.FindStrategy(strategyId);
            if (pkg != null)
                await _pluginPackageConfigService.DeleteDatasetConfigAsync(strategyId , datasetId , venueId , ct);
            else
                await _datasetConfigRepo.DeleteAsync(strategyId , datasetId , venueId , ct);
        }

        /// <inheritdoc />
        public async Task<(bool studentOk , bool venueOk)> CheckDatasetIntegrityAsync (StrategyDatasetConfig config , CancellationToken ct = default)
        {
            bool studentOk = true;
            bool venueOk = true;

            if (!string.IsNullOrEmpty(config.DatasetId) && !string.IsNullOrEmpty(config.StudentsHash))
            {
                var students = await _datasetRepo.LoadAsync(config.DatasetId , ct);
                string? currentHash = null;
                if (students is { Count: > 0 })
                {
                    var sorted = students.OrderBy(s => s.Id).ToList();
                    var json = JsonSerializer.Serialize(sorted , StudentHashOptions);
                    currentHash = Infrastructure.Utils.ContentHashHelper.ComputeSha256(json);
                }
                studentOk = currentHash == config.StudentsHash;
            }

            if (!string.IsNullOrEmpty(config.VenueId) && !string.IsNullOrEmpty(config.ContentHash))
            {
                var currentHash = await _venueRepo.GetContentHashAsync(config.VenueId , ct);
                venueOk = currentHash == config.ContentHash;
            }

            return (studentOk , venueOk);
        }

        #region Strategy Helpers

        /// <summary>
        /// 加载代码块配置（FixedSeat/DeskMate 等）并应用到策略实例。
        /// </summary>
        private async Task ApplyCodeBlockConfigsAsync (
            List<ISeatingStrategy> strategies ,
            SeatingRequest request ,
            ClassroomLayoutDefinition? venueLayout ,
            CancellationToken ct)
        {
            string? datasetId = request.DatasetId;
            string? venueId = request.LayoutId;
            if (string.IsNullOrEmpty(datasetId) && string.IsNullOrEmpty(venueId)) return;

            var validStudents = _currentWorkspace?.Students
                .Select(s => s.Id).ToHashSet() ?? [];

            // 处理独立策略的代码块配置（FixedSeat）
            foreach (var strategy in strategies)
            {
                // 配置读写路径：插件策略 → PluginPackageConfigService，内置策略 → StrategyDatasetConfigRepository
                var (pkg , _) = _pluginManager.FindStrategy(strategy.Id);
                var config = pkg != null
                    ? await _pluginPackageConfigService.LoadDatasetConfigAsync(strategy.Id , datasetId ?? string.Empty , venueId , ct)
                    : await _datasetConfigRepo.LoadAsync(strategy.Id , datasetId ?? string.Empty , venueId , ct);
                if (config?.Rows is not { Count: > 0 }) continue;

                switch (strategy)
                {
                    case FixedSeatStrategy fs:
                        bool fsCleaned = CleanInvalidSeatRows(config , venueLayout);
                        fsCleaned |= CleanFixedSeatDeletedStudents(config , validStudents);
                        if (fsCleaned)
                            await SaveDatasetConfigAsync(config , pkg , ct);
                        ApplyFixedSeatConfig(fs , config , venueLayout);
                        break;
                }
            }

            // 处理依赖策略的代码块配置（DeskMate）
            var dependentStrategies = _serviceProvider.GetServices<IDependentSeatingStrategy>().ToList();
            foreach (var dep in dependentStrategies)
            {
                var config = await _datasetConfigRepo.LoadAsync(dep.Id , datasetId ?? string.Empty , venueId , ct);
                if (config?.Rows is not { Count: > 0 }) continue;

                if (dep is DeskMateStrategy ds)
                {
                    if (CleanDeskMateDeletedStudents(config , validStudents))
                        await _datasetConfigRepo.SaveAsync(config , config.StudentsHash , config.ContentHash , ct);
                    ApplyDeskMateConfig(ds , config);
                }
                else if (dep is GenderRestrictedSeatStrategy grs)
                {
                    if (CleanInvalidSeatRows(config , venueLayout))
                        await _datasetConfigRepo.SaveAsync(config , config.StudentsHash , config.ContentHash , ct);
                    ApplyGenderRestrictionConfig(grs , config , venueLayout);
                }
            }
        }

        /// <summary>
        /// 将 StrategyDatasetConfig 的 Rows 转换为 FixedSeatConfiguration.FixedAssignments。
        /// ConfigRow 中的 SeatRow/SeatColumn/SeatRing/SeatAngle/SeatX/SeatY → 查找实际座位 ID。
        /// </summary>
        private static void ApplyFixedSeatConfig (
            FixedSeatStrategy strategy ,
            StrategyDatasetConfig config ,
            ClassroomLayoutDefinition? venueLayout)
        {
            var assignments = new Dictionary<string , string>();
            foreach (var row in config.Rows)
            {
                if (string.IsNullOrEmpty(row.StudentId)) continue;
                var seat = FindSeatByPosition(venueLayout , row);
                if (seat is not null)
                    assignments[seat.Id] = row.StudentId;
            }
            if (assignments.Count > 0)
                strategy.Config.FixedAssignments = assignments;
        }

        /// <summary>
        /// 将 StrategyDatasetConfig 的 Rows 转换为 DeskMateConfiguration.Groups。
        /// 每行一个 DeskMateGroup，StudentId + Values["student1"/"student2"/...] → 组内学生 ID 列表。
        /// </summary>
        private static void ApplyDeskMateConfig (DeskMateStrategy strategy , StrategyDatasetConfig config)
        {
            strategy.Config.Groups.Clear();
            foreach (var row in config.Rows)
            {
                var group = new DeskMateGroup();
                if (!string.IsNullOrEmpty(row.StudentId))
                    group.StudentIds.Add(row.StudentId);
                // 额外的同桌学生
                for (int i = 1; i <= 10; i++)
                {
                    var key = $"student{i}";
                    if (row.Values?.TryGetValue(key , out var sid) == true && sid?.ToString() is string s && !string.IsNullOrEmpty(s))
                        group.StudentIds.Add(s);
                }
                if (group.StudentIds.Count >= 2)
                    strategy.Config.Groups.Add(group);
            }
        }

        /// <summary>
        /// 将 StrategyDatasetConfig 的 Rows 转换为 GenderRestrictedSeatConfiguration.SeatGenderRestrictions。
        /// 每行通过座位位置查找实际 Seat.Id，Gender 字段值映射为 Gender 枚举，构建限制字典。
        /// </summary>
        private static void ApplyGenderRestrictionConfig (
            GenderRestrictedSeatStrategy strategy ,
            StrategyDatasetConfig config ,
            ClassroomLayoutDefinition? venueLayout)
        {
            var restrictions = new Dictionary<string , Gender>();
            foreach (var row in config.Rows)
            {
                var seat = FindSeatByPosition(venueLayout , row);
                if (seat is null) continue;

                if (row.Values?.TryGetValue("Gender" , out var genderObj) != true || genderObj is null)
                    continue;

                var genderStr = genderObj.ToString();
                Gender? parsed = null;
                if (string.Equals(genderStr , "Male" , StringComparison.OrdinalIgnoreCase))
                    parsed = Gender.Male;
                else if (string.Equals(genderStr , "Female" , StringComparison.OrdinalIgnoreCase))
                    parsed = Gender.Female;

                if (parsed is null) continue;
                restrictions[seat.Id] = parsed.Value;
            }

            strategy.SetRestrictions(restrictions);
        }

        /// <summary>
        /// 根据 ConfigRow 中的座位位置信息在会场布局中查找对应的 Seat 对象。
        /// </summary>
        private static Seat? FindSeatByPosition (ClassroomLayoutDefinition? layout , StrategyConfigRow row)
        {
            if (layout is null) return null;

            if (layout.LayoutType == LayoutType.Grid)
            {
                if (row.SeatRow.HasValue && row.SeatColumn.HasValue)
                    return layout.Seats.OfType<GridSeat>()
                        .FirstOrDefault(s => s.Row == row.SeatRow.Value && s.Column == row.SeatColumn.Value);
            }
            else if (layout.LayoutType == LayoutType.Polar)
            {
                if (row.SeatRing.HasValue && row.SeatAngle.HasValue)
                    return layout.Seats.OfType<PolarSeat>()
                        .FirstOrDefault(s => s.Ring == row.SeatRing.Value
                            && Math.Abs(s.AngleDegrees - row.SeatAngle.Value) < 0.01);
            }
            else if (layout.LayoutType == LayoutType.Freeform)
            {
                if (row.SeatX.HasValue && row.SeatY.HasValue)
                    return layout.Seats.OfType<FreeformSeat>()
                        .FirstOrDefault(s => Math.Abs(s.X - row.SeatX.Value) < 0.01
                            && Math.Abs(s.Y - row.SeatY.Value) < 0.01);
            }

            return null;
        }

        /// <summary>
        /// 移除配置行中座位位置在当前会场布局中不存在的行（会场缩小导致旧位置越界）。
        /// </summary>
        /// <returns>是否有行被移除。</returns>
        internal static bool CleanInvalidSeatRows (
            StrategyDatasetConfig config ,
            ClassroomLayoutDefinition? venueLayout)
        {
            if (config.Rows.Count == 0)
                return false;

            if (venueLayout == null)
            {
                // 会场已删除 → 所有依赖座位位置的行均失效
                int removed = config.Rows.RemoveAll(r =>
                    r.SeatRow.HasValue || r.SeatColumn.HasValue
                    || r.SeatRing.HasValue || r.SeatAngle.HasValue
                    || r.SeatX.HasValue || r.SeatY.HasValue);
                return removed > 0;
            }

            var validRows = config.Rows
                .Where(row => FindSeatByPosition(venueLayout , row) != null)
                .ToList();

            if (validRows.Count == config.Rows.Count)
                return false;

            config.Rows = validRows;
            return true;
        }

        /// <summary>
        /// 移除 FixedSeat 配置中引用已删除学生的行。
        /// </summary>
        /// <returns>是否有行被移除。</returns>
        internal static bool CleanFixedSeatDeletedStudents (
            StrategyDatasetConfig config ,
            HashSet<string> validStudentIds)
        {
            if (config.Rows.Count == 0)
                return false;

            int before = config.Rows.Count;
            if (validStudentIds.Count == 0)
                config.Rows.RemoveAll(r => !string.IsNullOrEmpty(r.StudentId));
            else
                config.Rows.RemoveAll(r =>
                    !string.IsNullOrEmpty(r.StudentId) && !validStudentIds.Contains(r.StudentId));
            return config.Rows.Count < before;
        }

        /// <summary>
        /// 清理 DeskMate 配置中引用已删除学生的行。
        /// 过滤掉不在 <paramref name="validStudentIds"/> 中的学生，
        /// 剩余小于 2 人则删除整行，否则重建该行仅保留有效学生。
        /// </summary>
        /// <returns>是否有行被移除或修改。</returns>
        internal static bool CleanDeskMateDeletedStudents (
            StrategyDatasetConfig config ,
            HashSet<string> validStudentIds)
        {
            if (config.Rows.Count == 0)
                return false;

            int before = config.Rows.Count;
            bool anyModified = false;
            var cleanedRows = new List<StrategyConfigRow>();
            foreach (var row in config.Rows)
            {
                // 收集本行中所有存在于当前工作区的学生 ID
                var validIds = new List<string>();

                if (!string.IsNullOrEmpty(row.StudentId)
                    && validStudentIds.Contains(row.StudentId))
                    validIds.Add(row.StudentId);

                // 计算原始学生总数（StudentId + 所有 studentN 值）
                int originalStudentCount = string.IsNullOrEmpty(row.StudentId) ? 0 : 1;
                for (int i = 1; i <= 10; i++)
                {
                    var key = $"student{i}";
                    if (row.Values?.TryGetValue(key , out var sid) == true
                        && sid?.ToString() is string s
                        && !string.IsNullOrEmpty(s))
                    {
                        originalStudentCount++;
                        if (validStudentIds.Contains(s))
                            validIds.Add(s);
                    }
                }

                // 同桌组至少需要 2 人
                if (validIds.Count < 2)
                    continue;

                // 有效学生数减少 → 内容已修改
                if (validIds.Count != originalStudentCount)
                    anyModified = true;

                // 重建行，仅保留有效学生（重新编号 student1/student2/...）
                var newRow = new StrategyConfigRow
                {
                    Index = row.Index ,
                    StudentId = validIds[0] ,
                    SeatRow = row.SeatRow ,
                    SeatColumn = row.SeatColumn ,
                    SeatRing = row.SeatRing ,
                    SeatAngle = row.SeatAngle ,
                    SeatX = row.SeatX ,
                    SeatY = row.SeatY ,
                    Values = new Dictionary<string , object?>()
                };
                for (int i = 1; i < validIds.Count; i++)
                    newRow.Values[$"student{i}"] = validIds[i];

                cleanedRows.Add(newRow);
            }

            if (cleanedRows.Count == before && !anyModified)
                return false;

            config.Rows = cleanedRows;
            return true;
        }

        /// <summary>
        /// 保存清理后的数据集配置，自动路由插件策略到 PluginPackageConfigService。
        /// 保留现有的哈希值（数据本身未变，只是移除了失效行）。
        /// </summary>
        private async Task SaveDatasetConfigAsync (
            StrategyDatasetConfig config ,
            LoadedPackageInfo? pkg ,
            CancellationToken ct)
        {
            if (pkg != null)
                await _pluginPackageConfigService.SaveDatasetConfigAsync(
                    config , config.StudentsHash , config.ContentHash , ct);
            else
                await _datasetConfigRepo.SaveAsync(
                    config , config.StudentsHash , config.ContentHash , ct);
        }

        private static StrategyDisplayInfo BuildDisplayInfo (
            StrategyManifest manifest ,
            string source ,
            Dictionary<string , StrategyConfig> persisted ,
            ISeatingStrategy? runtimeStrategy ,
            IDependentSeatingStrategy? depStrategy = null)
        {
            var info = new StrategyDisplayInfo
            {
                Id = manifest.Id ,
                DisplayName = manifest.DisplayName ,
                Description = manifest.Description ,
                Author = manifest.Author ,
                Category = manifest.Category ,
                Source = source ,
                DefaultPriority = manifest.DefaultPriority ,
                DefaultEnabled = manifest.DefaultEnabled ,
                Priority = manifest.DefaultPriority ,
                IsEnabled = manifest.DefaultEnabled ,
                ParameterDefinitions = manifest.Parameters ,
                CodeBlocks = manifest.CodeBlocks ,
                Messages = manifest.Messages ,
                Visible = manifest.Visible ,
                IsIndependent = manifest.IsIndependent
            };

            // 用持久化的配置覆盖默认值
            if (persisted.TryGetValue(manifest.Id , out var savedConfig))
            {
                info.Priority = savedConfig.Priority;
                info.IsEnabled = savedConfig.IsEnabled;
                info.Parameters = savedConfig.Parameters;
            }
            // 否则从运行时策略实例读取当前参数
            else if (runtimeStrategy is not null)
            {
                info.Parameters = ExtractParameters(runtimeStrategy);
            }
            else if (depStrategy is not null)
            {
                info.Parameters = ExtractDependentParameters(depStrategy);
            }

            return info;
        }

        private static Dictionary<string , object?> ExtractParameters (ISeatingStrategy strategy)
        {
            return strategy switch
            {
                FrontRowRotationStrategy fr => new Dictionary<string , object?>
                {
                    ["HistoryWeight"] = fr.Config.HistoryWeight ,
                    ["NeedsFrontRowBonus"] = fr.Config.NeedsFrontRowBonus ,
                    ["FrontRowCount"] = fr.Config.FrontRowCount
                },
                DefragStrategy => [],
                _ => []
            };
        }

        private static Dictionary<string , object?> ExtractDependentParameters (IDependentSeatingStrategy strategy)
        {
            return strategy switch
            {
                NoRepeatDeskMateStrategy nd => new Dictionary<string , object?>
                {
                    ["HistoryWindowSize"] = nd.Config.HistoryWindowSize
                },
                GenderRestrictedSeatStrategy => [],
                _ => []
            };
        }

        private static void ApplyConfiguration (ISeatingStrategy strategy , Dictionary<string , object?> parameters)
        {
            if (parameters.Count == 0) return;

            switch (strategy)
            {
                case FrontRowRotationStrategy fr:
                    fr.Config.HistoryWeight = GetParamInt(parameters , "HistoryWeight");
                    fr.Config.NeedsFrontRowBonus = GetParamInt(parameters , "NeedsFrontRowBonus");
                    fr.Config.FrontRowCount = GetParamInt(parameters , "FrontRowCount");
                    break;
                case DefragStrategy:
                    break; // 零参数策略
            }
        }

        private static void ApplyDependentConfiguration (IDependentSeatingStrategy strategy , Dictionary<string , object?> parameters)
        {
            if (parameters.Count == 0) return;

            switch (strategy)
            {
                case NoRepeatDeskMateStrategy nd:
                    nd.Config.HistoryWindowSize = GetParamInt(parameters , "HistoryWindowSize");
                    break;
                case GenderRestrictedSeatStrategy:
                    // 无策略级参数
                    break;
            }
        }

        private static int GetParamInt (Dictionary<string , object?> p , string key)
        {
            if (!p.TryGetValue(key , out var v) || v is null) return 0;
            if (v is int i) return i;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
            return 0;
        }

        private static bool GetParamBool (Dictionary<string , object?> p , string key)
        {
            if (!p.TryGetValue(key , out var v) || v is null) return false;
            if (v is bool b) return b;
            if (v is JsonElement je)
                return je.ValueKind == JsonValueKind.True;
            return false;
        }

        /// <summary>
        /// 将持久化的 StrategyConfig（Priority、IsEnabled、Parameters）应用到策略实例。
        /// 供 <see cref="SaveStrategyConfigAsync"/> 和 <see cref="RestorePersistedStrategyConfigsAsync"/> 共用。
        /// </summary>
        private static void ApplyPersistedConfigToInstance (
            StrategyConfig config ,
            ISeatingStrategy? strategy ,
            IDependentSeatingStrategy? depStrategy)
        {
            if (strategy is not null)
            {
                strategy.Priority = config.Priority;
                strategy.IsEnabled = config.IsEnabled;
                ApplyConfiguration(strategy , config.Parameters);
            }
            else if (depStrategy is not null)
            {
                depStrategy.Priority = config.Priority;
                depStrategy.IsEnabled = config.IsEnabled;
                ApplyDependentConfiguration(depStrategy , config.Parameters);
            }
        }

        /// <summary>
        /// 从持久化存储恢复策略配置（Priority、IsEnabled、Parameters），
        /// 覆盖 DI 单例的默认值。解决重启后策略参数丢失问题。
        /// </summary>
        private async Task RestorePersistedStrategyConfigsAsync (
            List<ISeatingStrategy> strategies ,
            List<IDependentSeatingStrategy> dependentStrategies ,
            CancellationToken ct)
        {
            var persisted = await _strategyConfigRepo.LoadAllAsync(ct);
            if (persisted.Count == 0)
                return;

            foreach (var strategy in strategies)
            {
                if (persisted.TryGetValue(strategy.Id , out var config))
                    ApplyPersistedConfigToInstance(config , strategy , null);
            }

            foreach (var dep in dependentStrategies)
            {
                if (persisted.TryGetValue(dep.Id , out var config))
                    ApplyPersistedConfigToInstance(config , null , dep);
            }

            logger.LogDebug("已从持久化恢复 {Count} 个策略配置" , persisted.Count);
        }

        /// <summary>
        /// 遍历所有策略的数据集配置，删除引用了已删除数据集或会场的孤立配置文件。
        /// 在每次 GenerateSeatingAsync 时执行，防止磁盘残留无效配置。
        /// </summary>
        private async Task CleanupOrphanedDatasetConfigsAsync (CancellationToken ct)
        {
            // 1. 收集有效数据集 ID 和会场 ID
            var validDatasetIds = (await _datasetRepo.ListAsync(ct))
                .Select(d => d.Id).ToHashSet();
            var validVenueIds = (await _venueRepo.ListVenueIdsAsync(ct))
                .ToHashSet();

            // 2. 收集所有策略 ID（内置 + 插件）
            var strategyIds = new HashSet<string>();
            foreach (var s in _serviceProvider.GetServices<ISeatingStrategy>())
                strategyIds.Add(s.Id);
            foreach (var d in _serviceProvider.GetServices<IDependentSeatingStrategy>())
                strategyIds.Add(d.Id);
            var plugins = await _pluginManager.LoadStrategyPluginsAsync(ct);
            foreach (var p in plugins)
                strategyIds.Add(p.Strategy.Id);

            // 3. 检查每个策略的所有数据集配置
            foreach (var sid in strategyIds)
            {
                var (pkg , _) = _pluginManager.FindStrategy(sid);

                var configs = pkg != null
                    ? await _pluginPackageConfigService.LoadDatasetConfigsAsync(sid , ct)
                    : await _datasetConfigRepo.LoadAllAsync(sid , ct);

                foreach (var config in configs)
                {
                    bool datasetGone = !string.IsNullOrEmpty(config.DatasetId)
                        && !validDatasetIds.Contains(config.DatasetId);
                    bool venueGone = !string.IsNullOrEmpty(config.VenueId)
                        && !validVenueIds.Contains(config.VenueId);

                    if (!datasetGone && !venueGone)
                        continue;

                    logger.LogWarning(
                        "清理孤立策略配置：{StrategyId} / Dataset={DatasetId} / Venue={VenueId}（数据集存在={DsOk}，会场存在={VOk}）" ,
                        sid , config.DatasetId , config.VenueId , !datasetGone , !venueGone);

                    if (pkg != null)
                        await _pluginPackageConfigService.DeleteDatasetConfigAsync(
                            sid , config.DatasetId ?? string.Empty , config.VenueId , ct);
                    else
                        await _datasetConfigRepo.DeleteAsync(
                            sid , config.DatasetId ?? string.Empty , config.VenueId , ct);
                }
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// 根据 <see cref="SeatingRequest"/> 中的布局类型和参数构建座位列表。
        /// </summary>
        /// <param name="request">座位生成请求。</param>
        /// <returns>生成的座位列表。</returns>
        private static List<Seat> BuildSeatsFromRequest (SeatingRequest request)
        {
            ClassroomLayoutDefinition layout = request.LayoutType switch
            {
                LayoutType.Grid => BuildGridLayout(request.LayoutParameters),
                LayoutType.Polar => BuildPolarLayout(request.LayoutParameters),
                LayoutType.Freeform => BuildFreeformLayout(request.LayoutParameters),
                _ => BuildGridLayout(new Dictionary<string , object> { ["Rows"] = 3 , ["Columns"] = 3 })
            };

            ObstacleProcessor.ApplyObstacles(layout);
            return layout.Seats;
        }

        /// <summary>
        /// 从参数字典构建网格布局。
        /// </summary>
        /// <param name="parameters">布局参数字典，支持 "Rows" 和 "Columns" 键。</param>
        /// <returns>网格布局定义。</returns>
        private static ClassroomLayoutDefinition BuildGridLayout (Dictionary<string , object> parameters)
        {
            int rows = GetParameter(parameters , "Rows" , 3);
            int columns = GetParameter(parameters , "Columns" , 3);
            var meta = new GridLayoutMetadata
            {
                Rows = rows ,
                Columns = columns ,
                SeatsPerDesk = GetParameter(parameters , "SeatsPerDesk" , 1) ,
                HorizontalSpacing = GetParameter(parameters , "HorizontalSpacing" , 1.0) ,
                VerticalSpacing = GetParameter(parameters , "VerticalSpacing" , 1.0) ,
                IntraDeskSpacing = GetParameter(parameters , "IntraDeskSpacing" , 0.0) ,
                InterDeskSpacing = GetParameter(parameters , "InterDeskSpacing" , 10.0) ,
                OriginX = GetParameter(parameters , "OriginX" , 0.0) ,
                OriginY = GetParameter(parameters , "OriginY" , 0.0) ,
                ColumnRowCounts = GetParameter<List<int>>(parameters , "ColumnRowCounts" , []) ,
                AisleAfterColumns = GetParameter<List<int>>(parameters , "AisleAfterColumns" , []) ,
                AisleAfterRows = GetParameter<List<int>>(parameters , "AisleAfterRows" , []) ,
                AisleWidth = GetParameter(parameters , "AisleWidth" , 0.0) ,
                FrontRowCount = GetParameter(parameters , "FrontRowCount" , 1) ,
                HasPodium = GetParameter(parameters , "HasPodium" , false) ,
                PodiumWidth = GetParameter(parameters , "PodiumWidth" , 0.0) ,
                PodiumHeight = GetParameter(parameters , "PodiumHeight" , 0.0) ,
                EmptyPositions = GetParameter<List<GridPosition>>(parameters , "EmptyPositions" , [])
            };
            return GridLayoutBuilder.BuildGrid(meta);
        }

        /// <summary>
        /// 从参数字典构建极坐标（环形）布局。
        /// </summary>
        /// <param name="parameters">布局参数字典，支持 "RadiusStep"、"Rings" 和 "SeatsPerRing" 键。</param>
        /// <returns>极坐标布局定义。</returns>
        private static ClassroomLayoutDefinition BuildPolarLayout (Dictionary<string , object> parameters)
        {
            var meta = new PolarLayoutMetadata
            {
                RadiusStep = GetParameter(parameters , "RadiusStep" , 1.0) ,
                Rings = GetParameter(parameters , "Rings" , 2) ,
                SeatsPerRing = GetParameter(parameters , "SeatsPerRing" , 8) ,
                RingSeatCounts = GetParameter<List<int>>(parameters , "RingSeatCounts" , []) ,
                StartAngleDegrees = GetParameter(parameters , "StartAngleDegrees" , 0.0) ,
                EndAngleDegrees = GetParameter(parameters , "EndAngleDegrees" , 180.0) ,
                EmptyPositions = GetParameter<List<PolarRingAngle>>(parameters , "EmptyPositions" , [])
            };
            return PolarLayoutBuilder.BuildPolar(meta);
        }

        /// <summary>
        /// 从参数字典构建自由形式布局。
        /// </summary>
        /// <param name="parameters">布局参数字典，支持 "Points" 键（坐标点列表）。</param>
        /// <returns>自由形式布局定义。</returns>
        private static ClassroomLayoutDefinition BuildFreeformLayout (Dictionary<string , object> parameters)
        {
            var points = new List<(double X , double Y , int? Row , int? Column , int? GroupId)>();
            if (parameters.TryGetValue("Points" , out var rawPoints) && rawPoints is System.Collections.IList list)
            {
                foreach (var item in list)
                {
                    if (item is Dictionary<string , object> dict)
                    {
                        double x = GetParameter(dict , "X" , 0.0);
                        double y = GetParameter(dict , "Y" , 0.0);
                        int? row = dict.ContainsKey("Row") ? global::SeatFlow.Application.Services.ApplicationFacade.GetParameter<int>(dict , "Row" , 0) : null;
                        int? col = dict.ContainsKey("Column") ? global::SeatFlow.Application.Services.ApplicationFacade.GetParameter<int>(dict , "Column" , 0) : null;
                        int? groupId = dict.ContainsKey("GroupId") ? global::SeatFlow.Application.Services.ApplicationFacade.GetParameter<int>(dict , "GroupId" , 0) : null;
                        points.Add((x , y , row , col , groupId));
                    }
                }
            }
            return FreeformLayoutBuilder.BuildFreeform(points);
        }

        /// <summary>
        /// 从字典中安全地获取指定键的值，若不存在或类型转换失败则返回默认值。
        /// </summary>
        /// <typeparam name="T">期望的值类型。</typeparam>
        /// <param name="parameters">参数字典。</param>
        /// <param name="key">键名。</param>
        /// <param name="defaultValue">默认值。</param>
        /// <returns>转换后的值或默认值。</returns>
        private static T GetParameter<T> (Dictionary<string , object> parameters , string key , T defaultValue)
        {
            if (parameters.TryGetValue(key , out var value))
            {
                try
                {
                    if (value is T typedValue) return typedValue;
                    return (T)Convert.ChangeType(value , typeof(T));
                }
                catch { return defaultValue; }
            }
            return defaultValue;
        }

        #endregion

        #region Plugin Management

        // ── 数据包 (SeatSets) ──

        /// <inheritdoc />
        public Task<int> ExportSeatSetsAsync(string outputPath, SeatSetsExportSelection selection, CancellationToken ct = default)
            => _seatSetsService.ExportAsync(outputPath, selection, ct);

        /// <inheritdoc />
        public Task<SeatSetsImportResult> ImportSeatSetsAsync(string filePath, SeatSetsExportSelection selection,
            IProgress<double>? progress = null, CancellationToken ct = default)
            => _seatSetsService.ImportAsync(filePath, selection, progress, ct);

        /// <inheritdoc />
        public Task<SeatSetsValidationResult> ValidateSeatSetsAsync(string filePath, CancellationToken ct = default)
            => _seatSetsService.ValidateAsync(filePath, ct);

        /// <inheritdoc />
        public Task<string?> DiscoverSeatSetsFileAsync(CancellationToken ct = default)
            => _seatSetsService.DiscoverAsync(ct);

        /// <inheritdoc />
        public Task<SeatSetsExportSelection> ProbeSeatSetsCategoriesAsync(string filePath, CancellationToken ct = default)
            => _seatSetsService.ProbeCategoriesAsync(filePath, ct);

        /// <inheritdoc />
        public async Task<List<PluginDisplayInfo>> GetPluginsAsync (CancellationToken ct = default)
        {
            // 从包级 API 展平所有策略
            var packages = await GetPluginPackagesAsync(ct);
            var result = new List<PluginDisplayInfo>();
            foreach (var pkg in packages)
            {
                result.AddRange(pkg.Strategies);
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<List<PluginPackageDisplayInfo>> GetPluginPackagesAsync (CancellationToken ct = default)
        {
            var result = new List<PluginPackageDisplayInfo>();

            foreach (var (packageId , pkgInfo) in _pluginManager.LoadedPackages)
            {
                var iconPath = Path.Combine(pkgInfo.PackagePath , "icon.png");
                var strategies = new List<PluginDisplayInfo>();

                foreach (var (strategyId , pluginInfo) in pkgInfo.Strategies)
                {
                    var loadKind = GetLoadKindFromEntry(pluginInfo.Entry);
                    var scriptType = pluginInfo.Entry?.ScriptType?.ToLowerInvariant();

                    strategies.Add(new PluginDisplayInfo
                    {
                        Id = strategyId ,
                        Name = pluginInfo.Strategy.Name ,
                        Version = pkgInfo.PackageManifest.Version ,
                        Category = pkgInfo.PackageManifest.Type ,
                        LoadKind = loadKind ,
                        IsEnabled = pluginInfo.Strategy.IsEnabled ,
                        Description = pkgInfo.PackageManifest.Description ,
                        Author = pkgInfo.PackageManifest.Author ,
                        Priority = pluginInfo.Strategy.Priority ,
                        ScriptType = scriptType ,
                        PluginPath = pkgInfo.PackagePath ,
                        IconPath = File.Exists(iconPath) ? iconPath : null
                    });
                }

                result.Add(new PluginPackageDisplayInfo
                {
                    PackageId = packageId ,
                    PackageName = pkgInfo.PackageManifest.Name ,
                    Version = pkgInfo.PackageManifest.Version ,
                    Author = pkgInfo.PackageManifest.Author ,
                    Description = pkgInfo.PackageManifest.Description ,
                    IsEnabled = pkgInfo.Enables?.Enabled ?? true ,
                    IconPath = File.Exists(iconPath) ? iconPath : null ,
                    PackagePath = pkgInfo.PackagePath ,
                    Strategies = strategies
                });
            }
            return result;
        }

        /// <inheritdoc />
        public async Task SetPluginPackageEnabledAsync (string packageId , bool enabled , CancellationToken ct = default)
        {
            _cachedStrategyDisplayInfos = null;
            await _pluginManager.SetPackageEnabledAsync(packageId , enabled , ct);
        }

        /// <inheritdoc />
        public async Task<string> InstallPluginPackageAsync (string packagePath , CancellationToken ct = default)
        {
            _cachedStrategyDisplayInfos = null;
            return await _pluginManager.InstallFromPackageAsync(packagePath , ct);
        }

        /// <inheritdoc />
        public async Task UninstallPluginPackageAsync (string packageId , CancellationToken ct = default)
        {
            _cachedStrategyDisplayInfos = null;
            await _pluginManager.UnloadPackageAsync(packageId);
        }

        /// <inheritdoc />
        public async Task RefreshPluginPackageAsync (string packageId , CancellationToken ct = default)
        {
            _cachedStrategyDisplayInfos = null;
            await _pluginManager.RefreshPackageAsync(packageId , ct);
        }

        /// <inheritdoc />
        public async Task<string> GetPluginScriptAsync (string pluginId , CancellationToken ct = default)
        {
            var (pkg , plugin) = _pluginManager.FindStrategy(pluginId);
            if (pkg == null || plugin == null)
                throw new InvalidOperationException($"插件 {pluginId} 未加载");

            var entry = plugin.Entry;
            var scriptFile = entry?.ScriptFile;
            if (string.IsNullOrEmpty(scriptFile))
                throw new InvalidOperationException($"插件 {pluginId} 不是脚本插件");

            // 优先从策略子目录查找（与 LoadStrategyFromEntry 的查找顺序一致）
            var scriptPath = (string?)null;
            if (entry != null && !string.IsNullOrEmpty(entry.Path))
                scriptPath = Path.Combine(pkg.PackagePath , entry.Path , scriptFile);
            // 回退到包根目录
            if (scriptPath == null || !File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(pkg.PackagePath , scriptFile);
            }

            return await File.ReadAllTextAsync(scriptPath , ct);
        }

        /// <inheritdoc />
        public async Task SavePluginScriptAsync (string pluginId , string script , CancellationToken ct = default)
        {
            var (pkg , plugin) = _pluginManager.FindStrategy(pluginId);
            if (pkg == null || plugin == null)
                throw new InvalidOperationException($"插件 {pluginId} 未加载");

            var scriptFile = plugin.Entry?.ScriptFile;
            if (string.IsNullOrEmpty(scriptFile))
                throw new InvalidOperationException($"插件 {pluginId} 不是脚本插件");

            // 优先从策略子目录查找（与 LoadStrategyFromEntry 的查找顺序一致）
            var scriptPath = (string?)null;
            var entry = plugin.Entry;
            if (entry != null && !string.IsNullOrEmpty(entry.Path))
                scriptPath = Path.Combine(pkg.PackagePath , entry.Path , scriptFile);
            // 回退到包根目录
            if (scriptPath == null || !File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(pkg.PackagePath , scriptFile);
            }

            await File.WriteAllTextAsync(scriptPath , script , ct);
        }

        /// <inheritdoc />
        public async Task<string> GetPluginConfigJsonAsync (string pluginId , CancellationToken ct = default)
        {
            var config = await _pluginConfigService.LoadConfigurationAsync<object>(pluginId , ct);
            return System.Text.Json.JsonSerializer.Serialize(config , JsonOptions.WriteIndented);
        }

        /// <inheritdoc />
        public async Task SavePluginConfigJsonAsync (string pluginId , string json , CancellationToken ct = default)
        {
            // 验证 JSON 格式
            var obj = System.Text.Json.JsonSerializer.Deserialize<object>(json)
                ?? throw new ArgumentException("JSON 格式无效");
            await _pluginConfigService.SaveConfigurationAsync(pluginId , obj , ct);
        }

        /// <inheritdoc />
        public async Task SetPluginEnabledAsync (string pluginId , bool enabled , CancellationToken ct = default)
        {
            _cachedStrategyDisplayInfos = null;
            await _pluginManager.SetStrategyEnabledAsync(pluginId , enabled , ct);
        }

        private static string GetLoadKindFromEntry (PluginStrategyEntry? entry)
        {
            if (entry == null) return "unknown";
            if (!string.IsNullOrEmpty(entry.ScriptFile) && !string.IsNullOrEmpty(entry.ScriptType))
                return entry.ScriptType.ToLowerInvariant() switch
                {
                    "lua" => "lua",
                    "csharp" => "csharp",
                    _ => "script"
                };
            if (!string.IsNullOrEmpty(entry.Assembly) && !string.IsNullOrEmpty(entry.EntryType))
                return "assembly";
            return "unknown";
        }

        #endregion
    }
}