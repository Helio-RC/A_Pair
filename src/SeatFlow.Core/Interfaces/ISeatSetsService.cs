using SeatFlow.Core.Models.SeatSets;

namespace SeatFlow.Core.Interfaces;

/// <summary>
/// .seatsets 数据包文件的服务契约。
/// 提供导出（打包）、导入（恢复）、校验、自动发现和探测类别等操作。
/// </summary>
public interface ISeatSetsService
{
    /// <summary>
    /// 将选定的应用数据类别导出为 .seatsets 文件。
    /// </summary>
    /// <param name="outputPath">输出文件路径。</param>
    /// <param name="selection">用户选择的数据类别。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导出的文件总数。</returns>
    Task<int> ExportAsync (string outputPath , SeatSetsExportSelection selection ,
        CancellationToken ct = default);

    /// <summary>
    /// 从 .seatsets 文件导入数据，恢复文件夹结构和文件。
    /// 采用"尽力而为"策略：单个文件失败不会中断整个导入。
    /// </summary>
    /// <param name="filePath">.seatsets 文件路径。</param>
    /// <param name="selection">用户选择要导入的数据类别。</param>
    /// <param name="progress">进度报告（0.0 ~ 1.0），可选。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>导入结果，含成功/跳过/失败计数和错误详情。</returns>
    Task<SeatSetsImportResult> ImportAsync (string filePath , SeatSetsExportSelection selection ,
        IProgress<double>? progress = null , CancellationToken ct = default);

    /// <summary>
    /// 校验 .seatsets 文件的完整性和有效性（大小、JSON 结构、哈希）。
    /// 不执行实际导入操作。
    /// </summary>
    /// <param name="filePath">.seatsets 文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>校验结果。</returns>
    Task<SeatSetsValidationResult> ValidateAsync (string filePath ,
        CancellationToken ct = default);

    /// <summary>
    /// 在可执行文件目录中自动发现 .seatsets 文件。
    /// 用于首次启动且 AppData 不存在时的自动导入场景。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发现的 .seatsets 文件路径，未找到则返回 null。</returns>
    Task<string?> DiscoverAsync (CancellationToken ct = default);

    /// <summary>
    /// 探测 .seatsets 文件中包含哪些数据类别。
    /// 用于在导入对话框中预填可用的复选框。
    /// </summary>
    /// <param name="filePath">.seatsets 文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>文件中实际包含的类别选择（仅含有的类别为 true）。</returns>
    Task<SeatSetsExportSelection> ProbeCategoriesAsync (string filePath ,
        CancellationToken ct = default);
}
