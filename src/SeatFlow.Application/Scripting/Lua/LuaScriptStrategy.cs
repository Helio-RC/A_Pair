using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLua.Exceptions;

namespace SeatFlow.Application.Scripting.Lua
{
    /// <summary>
    /// Lua 脚本策略，将 Lua 脚本作为插件策略执行。
    /// 直接实现 <see cref="IPluginSeatingStrategy"/>，是插件系统中的一级策略类型。
    /// </summary>
    public class LuaScriptStrategy : IPluginSeatingStrategy
    {
        private readonly string _scriptCode;
        private readonly LuaScriptConfiguration _config;
        private readonly ILogger<LuaScriptStrategy> _logger;

        /// <summary>
        /// 初始化 Lua 脚本策略。
        /// </summary>
        /// <param name="scriptCode">Lua 脚本源代码。</param>
        /// <param name="strategyId">策略唯一标识（来自策略 manifest id，保证配置路由一致）。</param>
        /// <param name="version">策略版本号（来自策略 manifest）。</param>
        /// <param name="config">脚本策略配置。</param>
        /// <param name="logger">日志记录器。</param>
        public LuaScriptStrategy (string scriptCode , string strategyId , string? version = null , LuaScriptConfiguration? config = null , ILogger<LuaScriptStrategy>? logger = null)
        {
            _scriptCode = scriptCode ?? throw new ArgumentNullException(nameof(scriptCode));
            _config = config ?? new LuaScriptConfiguration();
            _logger = logger ?? NullLogger<LuaScriptStrategy>.Instance;
            Id = strategyId;
            Version = version ?? "1.0.0";
            Name = _config.StrategyName ?? "LuaScript";
            Priority = _config.Priority;
            IsEnabled = _config.Enabled;
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public string Version { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public int Priority { get; set; }

        /// <inheritdoc />
        public bool IsEnabled { get; set; }

        /// <inheritdoc />
        public async Task<PluginStrategyResult> ExecuteAsync (IPluginWorkspace workspace , CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            try
            {
                _logger.LogInformation("Lua 脚本策略开始执行：{Name}" , Name);

                // 注意：此处不能 using——Lua state 的释放由执行线程的 finally 负责，
                // 超时路径绝不与正在运行的 VM 并发 Dispose（避免原生层崩溃）。
                var lua = CreateRestrictedLuaState();
                var api = new LuaWorkspaceAPI(workspace);
                lua["workspace"] = api;
                lua["cancellationToken"] = cancellationToken;

                // 脚本与 Dispose 在同一线程内依次执行；脚本结束后（或进程退出时）资源自然回收
                var execTask = Task.Run(() =>
                {
                    try
                    {
                        lua.DoString(_scriptCode);
                        _logger.LogInformation("Lua 脚本策略执行完成：{Name}" , Name);
                        return new PluginStrategyResult { Success = true };
                    }
                    catch (LuaScriptException ex)
                    {
                        _logger.LogWarning(ex , "Lua 脚本错误：{Name}" , Name);
                        return new PluginStrategyResult { Success = false , Message = $"Lua 错误: {ex.Message}" };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex , "Lua 脚本执行失败：{Name}" , Name);
                        return new PluginStrategyResult { Success = false , Message = $"执行失败: {ex.Message}" };
                    }
                    finally
                    {
                        lua.Dispose();
                    }
                } , cancellationToken);

                var timeoutTask = Task.Delay(_config.TimeoutMilliseconds , cancellationToken);

                if (await Task.WhenAny(execTask , timeoutTask) == timeoutTask)
                {
                    if (!timeoutTask.IsCompletedSuccessfully)
                        throw new OperationCanceledException(cancellationToken);

                    // 超时：返回失败。受 .NET 进程内脚本宿主限制，无法强制中断 Lua VM 死循环——
                    // 脚本在后台继续运行直至自行结束（或进程退出），期间不并发释放其 Lua state。
                    // 安全边界详见 SDK 文档：脚本插件仅应从可信来源安装。
                    _logger.LogWarning(
                        "Lua 脚本执行超时：{Name}（{Timeout}ms），脚本将在后台继续运行直至自行结束" ,
                        Name , _config.TimeoutMilliseconds);
                    return new PluginStrategyResult { Success = false , Message = "脚本执行超时" };
                }

                return await execTask;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex , "Lua 脚本执行失败：{Name}" , Name);
                return new PluginStrategyResult { Success = false , Message = $"执行失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 创建受限的 Lua 状态，移除危险库以防止恶意操作。
        /// </summary>
        /// <remarks>
        /// 当前移除的全局库：<c>io</c>（文件操作）、<c>os</c>（系统操作）、
        /// <c>package</c>（模块加载）、<c>debug</c>（调试功能）、<c>require</c>（模块加载函数）。
        /// <c>import</c>（.NET 程序集加载）被覆盖为空函数，阻止脚本访问任意 .NET 类型。
        /// 内存限制尚未实现（TODO）。超时无法强制中断 Lua VM（与 Roslyn C# 脚本同理），
        /// 脚本插件应仅从可信来源安装。
        /// </remarks>
        /// <returns>受限的 Lua 状态实例。</returns>
        private static global::NLua.Lua CreateRestrictedLuaState ()
        {
            var lua = new global::NLua.Lua();
            lua.DoString(@"
        io = nil
        os = nil
        package = nil
        debug = nil
        require = nil
        import = function () end
    ");
            //TODO:GC还没有找到好的办法，暂时不限制内存，后续可以考虑通过监控 Lua 内存使用情况来实现
            return lua;
        }
    }

    /// <summary>
    /// 暴露给 Lua 脚本的受限工作区 API，提供学生查询、座位查询和座位分配功能。
    /// </summary>
    /// <remarks>
    /// Lua 脚本通过全局变量 <c>workspace</c> 访问此 API 的方法。
    /// 所有方法均设计为简单类型输入/输出，以兼容 Lua 的类型系统。
    /// </remarks>
    /// <param name="workspace">当前座位工作区（插件受限视图）。</param>
    public class LuaWorkspaceAPI (IPluginWorkspace workspace)
    {
        private readonly IPluginWorkspace _workspace = workspace;

        /// <summary>
        /// 获取所有未分配的学生 ID 列表。
        /// </summary>
        /// <returns>未分配学生的 ID 数组。</returns>
        public string[] GetUnassignedStudentIds ()
        {
            var assignedIds = _workspace.GetAssignments().Values;
            return [.. _workspace.Students
                .Select(s => s.Id)
                .Where(id => !assignedIds.Contains(id))];
        }

        /// <summary>
        /// 获取所有空座位 ID 列表。
        /// </summary>
        /// <returns>空座位的 ID 数组。</returns>
        public string[] GetEmptySeatIds ()
        {
            return [.. _workspace.GetEmptySeats().Select(s => s.Id)];
        }

        /// <summary>
        /// 将指定学生分配到指定座位。
        /// </summary>
        /// <param name="seatId">座位 ID。</param>
        /// <param name="studentId">学生 ID。</param>
        /// <returns>如果分配成功则返回 true；否则返回 false。</returns>
        public bool AssignSeat (string seatId , string studentId)
        {
            return _workspace.TryAssignSeat(seatId , studentId , out _);
        }

        /// <summary>
        /// 获取指定学生的基本信息。
        /// </summary>
        /// <param name="studentId">学生 ID。</param>
        /// <returns>学生信息对象；如果未找到则返回 <c>null</c>。</returns>
        public StudentInfo? GetStudent (string studentId)
        {
            var student = _workspace.Students.FirstOrDefault(s => s.Id == studentId);
            if (student == null) return null;
            return new StudentInfo
            {
                Id = student.Id ,
                Name = student.Name ,
                Height = student.Height ,
                NeedsFrontRow = student.NeedsFrontRow ,
                FrontRowPreferenceScore = student.FrontRowPreferenceScore
            };
        }

        /// <summary>
        /// 获取指定座位的基本信息。
        /// </summary>
        /// <param name="seatId">座位 ID。</param>
        /// <returns>座位信息对象；如果未找到则返回 <c>null</c>。</returns>
        public SeatInfo? GetSeat (string seatId)
        {
            var seat = _workspace.FindSeats(s => s.Id == seatId).FirstOrDefault();
            if (seat == null) return null;
            return new SeatInfo
            {
                Id = seat.Id ,
                IsAvailable = seat.IsAvailable ,
                IsFixed = seat.IsFixed ,
                OccupantId = seat.OccupantId
            };
        }
    }

    /// <summary>
    /// 用于 Lua 交互的学生数据传输对象。
    /// </summary>
    public class StudentInfo
    {
        /// <summary>学生 ID。</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>学生姓名。</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>学生身高（可选）。</summary>
        public float? Height { get; set; }
        /// <summary>是否需要前排座位。</summary>
        public bool NeedsFrontRow { get; set; }
        /// <summary>前排偏好分数。</summary>
        public int FrontRowPreferenceScore { get; set; }
    }

    /// <summary>
    /// 用于 Lua 交互的座位数据传输对象。
    /// </summary>
    public class SeatInfo
    {
        /// <summary>座位 ID。</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>座位是否可用。</summary>
        public bool IsAvailable { get; set; }
        /// <summary>座位是否固定。</summary>
        public bool IsFixed { get; set; }
        /// <summary>当前占用学生的 ID（可选）。</summary>
        public string? OccupantId { get; set; }
    }

    /// <summary>
    /// Lua 脚本策略的配置选项。
    /// </summary>
    public class LuaScriptConfiguration
    {
        /// <summary>策略显示名称。</summary>
        public string? StrategyName { get; set; }
        /// <summary>策略在管道中的执行优先级。</summary>
        public int Priority { get; set; } = 50;
        /// <summary>是否启用策略。</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>脚本执行超时时间（毫秒），默认 5000。</summary>
        public int TimeoutMilliseconds { get; set; } = 5000;
        /// <summary>内存限制（字节），默认 10 MB。</summary>
        public int MemoryLimitBytes { get; set; } = 10 * 1024 * 1024;
    }
}
