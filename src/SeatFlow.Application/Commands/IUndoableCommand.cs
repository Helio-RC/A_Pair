using SeatFlow.Core.Workspace;

namespace SeatFlow.Application.Commands
{
    /// <summary>
    /// 可撤销命令接口，实现命令模式（Command Pattern）。
    /// 支持对工作区的操作进行撤销和重做，用于手动调座等交互场景。
    /// </summary>
    public interface IUndoableCommand
    {
        /// <summary>命令唯一标识符。</summary>
        string Id { get; }

        /// <summary>执行命令。</summary>
        /// <param name="workspace">当前工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否执行成功。</returns>
        Task<bool> ExecuteAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default);

        /// <summary>撤销命令。</summary>
        /// <param name="workspace">当前工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否撤销成功。</returns>
        Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default);
    }
}
