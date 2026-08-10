using SeatFlow.Application.Commands;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Services
{
    public class CommandHistory (ILogger<CommandHistory>? logger = null)
    {
        private readonly Stack<IUndoableCommand> _undo = new();
        private readonly Stack<IUndoableCommand> _redo = new();
        private readonly ILogger<CommandHistory> _logger = logger ?? NullLogger<CommandHistory>.Instance;

        /// <summary>
        /// 获取一个值，指示当前是否有可撤销的命令。
        /// </summary>
        public bool CanUndo => _undo.Count > 0;

        /// <summary>
        /// 获取一个值，指示当前是否有可重做的命令。
        /// </summary>
        public bool CanRedo => _redo.Count > 0;

        /// <summary>
        /// 执行指定的命令，若成功则将其压入撤销栈并清空重做栈。
        /// </summary>
        /// <param name="command">要执行的命令。</param>
        /// <param name="workspace">当前座位工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果命令执行成功则返回 true；否则返回 false。</returns>
        public async Task<bool> ExecuteAsync (IUndoableCommand command , SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            var ok = await command.ExecuteAsync(workspace , cancellationToken);
            if (ok)
            {
                _undo.Push(command);
                _redo.Clear();
                _logger.LogDebug("命令已执行：{CommandId}（撤销栈 {UndoCount}）" , command.Id , _undo.Count);
            }
            else
                _logger.LogWarning("命令执行失败：{CommandId}" , command.Id);
            return ok;
        }

        /// <summary>
        /// 撤销最近一次执行的命令。仅操作成功后才会从栈中移除命令。
        /// </summary>
        /// <param name="workspace">当前座位工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果撤销成功则返回 true；否则返回 false。</returns>
        public async Task<bool> UndoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            if (!CanUndo) return false;
            var cmd = _undo.Peek();
            var ok = await cmd.UndoAsync(workspace , cancellationToken);
            if (ok)
            {
                _undo.Pop();
                _redo.Push(cmd);
                _logger.LogDebug("命令已撤销：{CommandId}（撤销栈 {UndoCount}，重做栈 {RedoCount}）" , cmd.Id , _undo.Count , _redo.Count);
            }
            else
                _logger.LogWarning("命令撤销失败：{CommandId}" , cmd.Id);
            return ok;
        }

        /// <summary>
        /// 重做最近一次撤销的命令。仅操作成功后才会从栈中移除命令。
        /// </summary>
        /// <param name="workspace">当前座位工作区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果重做成功则返回 true；否则返回 false。</returns>
        public async Task<bool> RedoAsync (SeatingWorkspace workspace , CancellationToken cancellationToken = default)
        {
            if (!CanRedo) return false;
            var cmd = _redo.Peek();
            var ok = await cmd.ExecuteAsync(workspace , cancellationToken);
            if (ok)
            {
                _redo.Pop();
                _undo.Push(cmd);
                _logger.LogDebug("命令已重做：{CommandId}（撤销栈 {UndoCount}，重做栈 {RedoCount}）" , cmd.Id , _undo.Count , _redo.Count);
            }
            else
                _logger.LogWarning("命令重做失败：{CommandId}" , cmd.Id);
            return ok;
        }
    }
}
