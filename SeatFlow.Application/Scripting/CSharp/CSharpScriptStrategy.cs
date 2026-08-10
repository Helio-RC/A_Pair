using System.Reflection;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Contracts.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Scripting.CSharp
{
    /// <summary>
    /// C# 脚本策略，使用 Roslyn 脚本引擎在运行时编译并执行 C# 代码作为座位分配策略。
    /// 直接实现 <see cref="IPluginSeatingStrategy"/>，是插件系统中的一级策略类型。
    /// </summary>
    /// <remarks>
    /// <b>安全边界：</b>脚本在宿主进程内以完全信任（FullTrust）执行，引用白名单仅是功能限制而非安全边界。
    /// 超时（<see cref="CSharpScriptConfiguration.TimeoutMilliseconds"/>）无法中断正在执行的脚本（Roslyn 执行期间不响应取消），
    /// 因此脚本插件应仅从可信来源安装。建议将 C# 脚本策略的优先级保持在较低数值，避免脚本长时间占用线程。
    /// </remarks>
    public class CSharpScriptStrategy : IPluginSeatingStrategy
    {
        private readonly string _code;
        private readonly CSharpScriptConfiguration _config;
        private readonly ILogger<CSharpScriptStrategy> _logger;

        /// <summary>
        /// 初始化 C# 脚本策略。
        /// </summary>
        /// <param name="code">C# 脚本源代码。</param>
        /// <param name="strategyId">策略唯一标识（来自策略 manifest id，保证配置路由一致）。</param>
        /// <param name="version">策略版本号（来自策略 manifest）。</param>
        /// <param name="config">脚本策略配置。</param>
        /// <param name="logger">日志记录器。</param>
        public CSharpScriptStrategy (string code , string strategyId , string? version = null , CSharpScriptConfiguration? config = null , ILogger<CSharpScriptStrategy>? logger = null)
        {
            _code = code ?? throw new ArgumentNullException(nameof(code));
            _config = config ?? new CSharpScriptConfiguration();
            _logger = logger ?? NullLogger<CSharpScriptStrategy>.Instance;
            Id = strategyId;
            Version = version ?? "1.0.0";
            Name = _config.StrategyName ?? "CSharpScript";
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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.TimeoutMilliseconds);

            try
            {
                _logger.LogInformation("C# 脚本策略开始执行：{Name}" , Name);
                var options = ScriptOptions.Default
                    .WithReferences(GetAllowedReferences())
                    .WithImports(GetAllowedImports());

                var globals = new ScriptGlobals { Workspace = workspace };
                var script = CSharpScript.Create(_code , options , typeof(ScriptGlobals));

                var task = script.RunAsync(globals , cancellationToken: cts.Token);
                await task.WaitAsync(cts.Token);

                _logger.LogInformation("C# 脚本策略执行完成：{Name}" , Name);
                return new PluginStrategyResult { Success = true };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("C# 脚本执行超时：{Name}（{Timeout}ms）" , Name , _config.TimeoutMilliseconds);
                return new PluginStrategyResult { Success = false , Message = "脚本执行超时" };
            }
            catch (CompilationErrorException ex)
            {
                _logger.LogWarning(ex , "C# 脚本编译错误：{Name}" , Name);
                return new PluginStrategyResult { Success = false , Message = $"编译错误: {string.Join("\n" , ex.Diagnostics)}" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex , "C# 脚本执行失败：{Name}" , Name);
                return new PluginStrategyResult { Success = false , Message = $"执行失败: {ex.Message}" };
            }
        }

        /// <summary>
        /// 获取允许引用的程序集白名单，限制脚本可使用的 API 范围。
        /// </summary>
        /// <returns>允许引用的程序集数组。</returns>
        private static Assembly[] GetAllowedReferences ()
        {
            // 仅允许必要的程序集
            var allowed = new List<Assembly>
            {
                typeof(object).Assembly,                 // System.Private.CoreLib
                typeof(Enumerable).Assembly,             // System.Linq
                typeof(IPluginWorkspace).Assembly,       // SeatFlow.Contracts（插件工作区契约）
                typeof(SeatFlow.Core.Workspace.SeatingWorkspace).Assembly, // SeatFlow.Core（兼容旧脚本）
                typeof(ScriptGlobals).Assembly           // SeatFlow.Application
            };

            // 可根据配置添加额外引用
            return [.. allowed];
        }

        /// <summary>
        /// 获取允许导入的命名空间白名单。
        /// </summary>
        /// <returns>允许导入的命名空间数组。</returns>
        private static string[] GetAllowedImports () =>
            [
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "SeatFlow.Contracts.Models",
                "SeatFlow.Core.Workspace",
                "SeatFlow.Core.Models"
            ];
    }

    /// <summary>
    /// 传递给 C# 脚本的全局对象，脚本通过此对象访问 <see cref="IPluginWorkspace"/>。
    /// </summary>
    public class ScriptGlobals
    {
        /// <summary>
        /// 获取当前座位工作区实例（插件受限视图）。
        /// </summary>
        public IPluginWorkspace? Workspace { get; init; }
    }

    /// <summary>
    /// C# 脚本策略的配置选项。
    /// </summary>
    public class CSharpScriptConfiguration
    {
        /// <summary>
        /// 获取或设置策略显示名称。
        /// </summary>
        public string? StrategyName { get; set; }

        /// <summary>
        /// 获取或设置策略在管道中的执行优先级。
        /// </summary>
        public int Priority { get; set; } = 60;

        /// <summary>
        /// 获取或设置一个值，指示策略是否启用。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 获取或设置脚本执行超时时间（毫秒）。默认 5000 毫秒。
        /// </summary>
        public int TimeoutMilliseconds { get; set; } = 5000;
    }
}
