namespace SeatFlow.Core.Models;

/// <summary>
/// 策略的完整展示信息，合并了不可变的 Manifest 和用户可修改的 Config。
/// 在 UI 层作为策略详情的数据源。
/// </summary>
public sealed class StrategyDisplayInfo
{
    // ── 来自 Manifest（不可变） ──

    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Source { get; init; } = "builtin";
    public int DefaultPriority { get; init; }
    public bool DefaultEnabled { get; init; } = true;

    // ── 来自 Config（用户可修改） ──

    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public Dictionary<string , object?> Parameters { get; set; } = [];

    // ── 来自 Manifest 的声明式配置 ──

    /// <summary>策略级全局参数声明。</summary>
    public List<StrategyParameterDefinition>? ParameterDefinitions { get; init; }

    /// <summary>按数据集/会场的配置块声明。</summary>
    public List<StrategyCodeBlock>? CodeBlocks { get; init; }

    /// <summary>是否在策略配置页可见（来自 manifest visible）。默认 true。</summary>
    public bool Visible { get; init; } = true;

    /// <summary>是否为独立策略（来自 manifest isIndependent）。默认 true。false 时在 RandomFill 上下文中执行。</summary>
    public bool IsIndependent { get; init; } = true;

    /// <summary>依赖策略的子策略列表（仅当此策略是宿主时使用，如 RandomFill）。</summary>
    public List<StrategyDisplayInfo> DependentChildren { get; set; } = [];

    /// <summary>是否有子依赖策略。</summary>
    public bool HasDependentChildren => DependentChildren is { Count: > 0 };

    /// <summary>策略执行消息的多语言模板（来自 manifest messages）。key→语言词典。</summary>
    public Dictionary<string , Dictionary<string , string>>? Messages { get; init; }

    // ── 便捷判断 ──

    public bool IsBuiltIn => Source == "builtin";
    public bool IsModified => Priority != DefaultPriority || IsEnabled != DefaultEnabled;
}
