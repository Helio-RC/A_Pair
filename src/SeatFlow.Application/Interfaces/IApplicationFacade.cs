using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Interfaces
{
    /// <summary>
    /// 应用程序外观接口，是 UI/CLI 与业务逻辑层的统一入口。
    /// 封装了数据加载、座位生成、导出、快照管理等所有核心业务操作。
    /// 采用外观模式（Facade Pattern）简化调用方的使用复杂度。
    /// </summary>
    public interface IApplicationFacade
    {
        /// <summary>加载配置（预留，当前返回空配置）。</summary>
        Task<AppConfiguration> LoadConfigurationAsync (string path , CancellationToken cancellationToken = default);

        /// <summary>从指定数据源加载学生列表。</summary>
        Task<List<Student>> LoadStudentsAsync (string source , CancellationToken cancellationToken = default);

        /// <summary>从指定数据源加载学生列表，可限制最大扫描行列（0 = 不限制）。</summary>
        Task<List<Student>> LoadStudentsAsync (string source , int maxRows , int maxCols , CancellationToken ct = default);

        /// <summary>获取数据源文件的维度（行数 × 列数），用于导入前的范围判断。</summary>
        Task<(int Rows , int Cols)> GetDataSourceDimensionsAsync (string source , CancellationToken ct = default);

        /// <summary>
        /// 生成座位安排。
        /// 流程：加载学生 → 构建布局 → 执行策略管道 → 解决冲突 → 保存快照。
        /// </summary>
        /// <param name="request">座位生成请求参数。</param>
        /// <param name="progress">进度报告回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>包含最终分配结果的工作区。</returns>
        Task<SeatingWorkspace> GenerateSeatingAsync (SeatingRequest request , IProgress<SeatingProgress>? progress = null , CancellationToken cancellationToken = default);

        /// <summary>创建空白工作区：加载会场布局和学生，但不执行策略管道。所有座位为空，学生进入未分配列表。</summary>
        Task<SeatingWorkspace> CreateEmptyWorkspaceAsync (string layoutId , string datasetId , CancellationToken cancellationToken = default);

        /// <summary>导出座位安排计划到文件。</summary>
        Task ExportSeatingPlanAsync (SeatingWorkspace workspace , ClassroomLayoutDefinition? layout , string path , ExportOptions options , CancellationToken cancellationToken = default);

        /// <summary>导出学生列表到文件。</summary>
        Task ExportStudentsAsync (string path , IEnumerable<Student> students , ExportFormat format , CancellationToken cancellationToken = default);

        /// <summary>执行可撤销的命令（如手动调座）。</summary>
        /// <param name="recordInHistory">是否记录到 Facade 的命令历史栈。ViewModel 使用快照历史时传 false 避免重复累积。</param>
        Task<bool> ExecuteCommandAsync (SeatFlow.Application.Commands.IUndoableCommand command , CancellationToken cancellationToken = default , bool recordInHistory = true);

        /// <summary>撤销上一条命令。</summary>
        Task<bool> UndoAsync (CancellationToken cancellationToken = default);

        /// <summary>重做已撤销的命令。</summary>
        Task<bool> RedoAsync (CancellationToken cancellationToken = default);

        /// <summary>获取当前工作区。</summary>
        Task<SeatingWorkspace?> GetCurrentWorkspaceAsync (CancellationToken cancellationToken = default);

        /// <summary>加载应用程序设置。</summary>
        Task<AppSettings> LoadAppSettingsAsync (CancellationToken cancellationToken = default);

        /// <summary>保存应用程序设置。</summary>
        Task SaveAppSettingsAsync (AppSettings settings , CancellationToken cancellationToken = default);

        /// <summary>保存会场布局。</summary>
        Task SaveVenueAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default);

        /// <summary>加载会场布局。</summary>
        Task<ClassroomLayoutDefinition?> LoadVenueAsync (string venueId , CancellationToken cancellationToken = default);

        /// <summary>获取会场的 ContentHash（不反序列化全量布局）。</summary>
        Task<string?> GetVenueHashAsync (string venueId , CancellationToken ct = default);

        /// <summary>获取所有会场 ID 列表。</summary>
        Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default);

        /// <summary>删除指定会场。</summary>
        Task DeleteVenueAsync (string venueId , CancellationToken cancellationToken = default);

        /// <summary>获取指定会场的快照列表。</summary>
        Task<IReadOnlyList<SeatingSnapshot>> GetSnapshotsAsync (string venueId , CancellationToken cancellationToken = default);

        /// <summary>回滚到指定快照。</summary>
        Task RollbackToSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default);

        /// <summary>删除指定快照。</summary>
        Task DeleteSnapshotAsync (string snapshotId , CancellationToken cancellationToken = default);

        /// <summary>从当前工作区手动创建快照。返回 null 表示无活跃工作区。</summary>
        Task<SeatingSnapshot?> CreateSnapshotAsync (string description , CancellationToken cancellationToken = default);

        /// <summary>当前是否有活跃的工作区（可用于创建快照或回滚）。</summary>
        bool HasActiveWorkspace { get; }

        /// <summary>获取当前活跃工作区关联的会场布局，用于 UI 恢复显示。无活跃工作区时返回 null。</summary>
        Task<ClassroomLayoutDefinition?> GetCurrentLayoutAsync (CancellationToken cancellationToken = default);

        /// <summary>保存学生数据集到托管存储。</summary>
        Task<string> SaveStudentDatasetAsync (string name , List<Student> students , string? originalFileName = null , CancellationToken ct = default);

        /// <summary>更新已有学生数据集，保持原有 ID 不变（直接覆写对应文件）。</summary>
        Task UpdateStudentDatasetAsync (string id , string name , List<Student> students , string? originalFileName = null , CancellationToken ct = default);

        /// <summary>从托管存储加载学生数据集。</summary>
        Task<List<Student>?> LoadStudentDatasetAsync (string id , CancellationToken ct = default);

        /// <summary>列出所有已保存的学生数据集。</summary>
        Task<IReadOnlyList<StudentDatasetInfo>> ListStudentDatasetsAsync (CancellationToken ct = default);

        /// <summary>删除指定学生数据集。</summary>
        Task DeleteStudentDatasetAsync (string id , CancellationToken ct = default);

        /// <summary>原地重命名数据集，保持 ID 不变。</summary>
        Task RenameStudentDatasetAsync (string id , string newName , CancellationToken ct = default);

        /// <summary>获取所有策略（内置 + 插件）的完整展示信息，合并 Manifest 和运行时 Config。</summary>
        Task<List<StrategyDisplayInfo>> GetStrategiesAsync (CancellationToken ct = default);

        /// <summary>保存单个策略的运行时配置（优先级、启用状态、参数），并更新运行时策略实例。</summary>
        Task SaveStrategyConfigAsync (string strategyId , StrategyConfig config , CancellationToken ct = default);

        // ── 策略数据集配置 ──

        /// <summary>加载指定策略的所有数据集配置。</summary>
        Task<List<StrategyDatasetConfig>> LoadStrategyDatasetConfigsAsync (string strategyId , CancellationToken ct = default);

        /// <summary>保存一份数据集配置，自动计算学生/会场哈希。</summary>
        Task SaveStrategyDatasetConfigAsync (StrategyDatasetConfig config , CancellationToken ct = default);

        /// <summary>删除一份数据集配置。</summary>
        Task DeleteStrategyDatasetConfigAsync (string strategyId , string datasetId , string? venueId , CancellationToken ct = default);

        /// <summary>检测数据集和会场的哈希是否与配置中记录的一致。返回 (studentOk, venueOk)。</summary>
        Task<(bool studentOk , bool venueOk)> CheckDatasetIntegrityAsync (StrategyDatasetConfig config , CancellationToken ct = default);

        /// <summary>清除当前工作区和布局状态，用于页面离开时重置。</summary>
        void ClearWorkspace ();

        /// <summary>检查快照关联会场的完整性。返回 (文件是否存在, 哈希是否匹配)。</summary>
        Task<(bool Exists , bool HashMatch)> CheckVenueIntegrityAsync (string venueId , string? snapshotVenueHash , CancellationToken ct = default);

        /// <summary>将快照中嵌入的会场布局 JSON 导入为新的会场文件，返回新会场 ID。</summary>
        Task<string> ImportVenueFromSnapshotAsync (string venueLayoutJson , string? newName = null , CancellationToken ct = default);

        // ── 数据包 (SeatSets) ──

        /// <summary>将选定的应用数据类别导出为 .seatsets 文件。</summary>
        Task<int> ExportSeatSetsAsync (string outputPath , SeatSetsExportSelection selection , CancellationToken ct = default);

        /// <summary>从 .seatsets 文件导入数据，恢复文件夹结构和文件。</summary>
        Task<SeatSetsImportResult> ImportSeatSetsAsync (string filePath , SeatSetsExportSelection selection , IProgress<double>? progress = null , CancellationToken ct = default);

        /// <summary>校验 .seatsets 文件的完整性和有效性。</summary>
        Task<SeatSetsValidationResult> ValidateSeatSetsAsync (string filePath , CancellationToken ct = default);

        /// <summary>在可执行文件目录中自动发现 .seatsets 文件。</summary>
        Task<string?> DiscoverSeatSetsFileAsync (CancellationToken ct = default);

        /// <summary>探测 .seatsets 文件中包含哪些数据类别。</summary>
        Task<SeatSetsExportSelection> ProbeSeatSetsCategoriesAsync (string filePath , CancellationToken ct = default);
    }

    /// <summary>
    /// 应用程序配置（预留，当前为空类型）。
    /// </summary>
    public class AppConfiguration { }

    /// <summary>
    /// 座位生成请求参数，包含布局选择、策略配置和数据源等信息。
    /// </summary>
    public class SeatingRequest
    {
        /// <summary>已保存的会场布局 ID（优先使用）。</summary>
        public string? LayoutId { get; set; }

        /// <summary>布局类型（当未指定 LayoutId 时使用）。</summary>
        public LayoutType LayoutType { get; set; }

        /// <summary>布局参数，根据布局类型不同包含不同键值对。</summary>
        public Dictionary<string , object> LayoutParameters { get; set; } = [];

        /// <summary>要使用的策略 ID 列表（当 UseDefaultStrategies=false 时生效）。</summary>
        public List<string> StrategyIds { get; set; } = [];

        /// <summary>是否使用默认策略列表。</summary>
        public bool UseDefaultStrategies { get; set; } = true;

        /// <summary>学生数据源路径。</summary>
        public string? StudentDataSource { get; set; }

        /// <summary>人员数据集 ID（用于加载对应策略代码块配置）。</summary>
        public string? DatasetId { get; set; }

        /// <summary>座位安排描述（用于快照记录）。</summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// 座位生成进度信息，用于向 UI 报告长时间操作的状态。
    /// </summary>
    public class SeatingProgress
    {
        /// <summary>总步骤数。</summary>
        public int TotalSteps { get; set; }

        /// <summary>当前步骤索引。</summary>
        public int CurrentStep { get; set; }

        /// <summary>状态描述消息。</summary>
        public string StatusMessage { get; set; } = string.Empty;
    }
}
