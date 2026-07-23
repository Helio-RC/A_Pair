using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 页面 ViewModel 实现此接口以支持文件拖放导入。
/// 由 FileDropHandler 行为在 DragOver/Drop 时检查并调用。
/// </summary>
public interface IFileDropHandler
{
    /// <summary>当前页面接受的文件扩展名列表（含前导点号，小写）。</summary>
    IReadOnlyList<string> AcceptedFileExtensions { get; }

    /// <summary>处理拖放的文件。返回 true 表示处理成功。</summary>
    /// <param name="filePaths">拖放文件路径列表（至少一个）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<bool> HandleFileDropAsync(IReadOnlyList<string> filePaths, CancellationToken ct);
}
