using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 侧栏策略列表中的单个策略项。追踪自身的优先级/启用状态变更。
/// 支持嵌套子项（依赖策略始终显示在宿主下方，用左侧竖线区分）。
/// </summary>
public partial class StrategyItemViewModel (
    string id ,
    string displayName ,
    string source ,
    bool isBuiltIn ,
    int priority ,
    int defaultPriority ,
    bool isEnabled ,
    bool isIndependent = true ,
    bool isDependentChild = false) : ObservableObject
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string Source { get; } = source;
    public bool IsBuiltIn { get; } = isBuiltIn;

    /// <summary>是否为独立策略（false 表示依赖策略，在 RandomFill 上下文中执行）。</summary>
    public bool IsIndependent { get; } = isIndependent;

    /// <summary>是否为依赖策略子项（在扁平列表中位于宿主下方）。</summary>
    public bool IsDependentChild { get; init; } = isDependentChild;

    public int DefaultPriority { get; } = defaultPriority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriorityDisplay))]
    public partial int Priority { get; set; } = priority;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = isEnabled;

    [ObservableProperty]
    public partial bool HasChanges { get; set; }

    /// <summary>子策略列表（依赖策略始终显示在宿主下方）。</summary>
    [ObservableProperty]
    public partial ObservableCollection<StrategyItemViewModel>? Children { get; set; }

    /// <summary>是否有子策略（用于 UI 竖线显示判断）。</summary>
    public bool HasChildren => Children is { Count: > 0 };

    /// <summary>是否显示左侧竖线（宿主或有依赖子项）。</summary>
    public bool ShowLeftBar => HasChildren || IsDependentChild;

    partial void OnPriorityChanged (int value) => HasChanges = true;
    partial void OnIsEnabledChanged (bool value) => HasChanges = true;

    public string PriorityDisplay => IsIndependent
        ? string.Format(Lang.Resources.Strategy_PipelinePriorityFmt , Priority)
        : string.Format(Lang.Resources.Strategy_ContextPriorityFmt , Priority);
    public string EnableTooltipDisplay => IsEnabled ? Lang.Resources.Common_Enabled : Lang.Resources.Common_Disabled;

    public void MarkClean () => HasChanges = false;
}
