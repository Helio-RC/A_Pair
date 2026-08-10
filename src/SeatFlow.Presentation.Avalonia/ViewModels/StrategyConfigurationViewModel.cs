using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Strategies;
using SeatFlow.Presentation.Avalonia.Lang;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class StrategyConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly ILogger<StrategyConfigurationViewModel> _logger;

    // ═══════════════ 侧栏列表 ═══════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ObservableCollection<StrategyItemViewModel> Strategies { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(PriorityDisplay))]
    [NotifyPropertyChangedFor(nameof(EnableTooltipDisplay))]
    public partial StrategyItemViewModel? SelectedStrategy { get; set; }

    public bool HasSelection => SelectedStrategy is not null;

    // ═══════════════ 详情区域 ═══════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyPropertyChangedFor(nameof(DetailSourceDisplay))]
    [NotifyPropertyChangedFor(nameof(DetailAuthorDisplay))]
    [NotifyPropertyChangedFor(nameof(DetailCategoryDisplay))]
    [NotifyPropertyChangedFor(nameof(DetailDefaultPriorityDisplay))]
    public partial StrategyDisplayInfo? SelectedDetail { get; set; }

    public bool HasDetail => SelectedDetail is not null && !string.IsNullOrEmpty(SelectedDetail.Id);

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    public partial bool IsLoading { get; set; }

    public bool IsNotLoading => !IsLoading;

    // ═══════════════ 全局变更：任意策略有变更即为 true ═══════════════

    public bool HasChanges
    {
        get
        {
            if (_hasDetailChanges) return true;
            return Strategies.Any(s => s.HasChanges);
        }
    }

    private bool _hasDetailChanges;
    private bool _suppressChangeTracking;

    // ═══════════════ 详情编辑属性 ═══════════════

    [ObservableProperty]
    public partial int EditPriority { get; set; }

    [ObservableProperty]
    public partial bool EditIsEnabled { get; set; }

    // ═══════════════ 声明式配置编辑器 ═══════════════

    /// <summary>策略级参数编辑器（manifest parameters[] 驱动）。</summary>
    [ObservableProperty]
    public partial ParameterEditorViewModel? ParameterEditor { get; set; }

    /// <summary>配置块编辑器列表（manifest codeBlocks[] 驱动），每项对应一个 codeBlock。</summary>
    [ObservableProperty]
    public partial ObservableCollection<ConfigBlockEditorViewModel> ConfigBlockEditors { get; set; } = [];

    /// <summary>是否有参数或配置块可显示。</summary>
    public bool HasParameters => ParameterEditor is not null && ParameterEditor.Parameters.Count > 0;
    public bool HasCodeBlocks => ConfigBlockEditors.Count > 0;

    // ── 详情编辑触发 _hasDetailChanges ──

    partial void OnEditPriorityChanged (int value)
    {
        if (_suppressChangeTracking) return;
        MarkDetailChanged();
    }
    partial void OnEditIsEnabledChanged (bool value)
    {
        if (_suppressChangeTracking) return;
        MarkDetailChanged();
        if (SelectedStrategy is not null)
            SelectedStrategy.IsEnabled = value;
    }

    private void MarkDetailChanged ()
    {
        _hasDetailChanges = true;
        OnPropertyChanged(nameof(HasChanges));
    }

    // ═══════════════ 构造函数 ═══════════════


    public string PriorityDisplay => SelectedStrategy != null ? string.Format(Resources.Strategy_PriorityFmt , SelectedStrategy.Priority) : "";
    public string EnableTooltipDisplay => SelectedStrategy != null ? string.Format(Resources.Strategy_EnableFmt , SelectedStrategy.DisplayName) : "";
    public string DetailSourceDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_SourceFmt , SelectedDetail.Source) : "";
    public string DetailAuthorDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_AuthorFmt , SelectedDetail.Author) : "";
    public string DetailCategoryDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_CategoryFmt , SelectedDetail.Category) : "";
    public string DetailDefaultPriorityDisplay => SelectedDetail != null ? string.Format(Resources.Strategy_DefaultPriorityFmt , SelectedDetail.DefaultPriority) : "";

    public StrategyConfigurationViewModel (IApplicationFacade facade , ILogger<StrategyConfigurationViewModel>? logger = null)
    {
        _facade = facade;
        _logger = logger ?? NullLogger<StrategyConfigurationViewModel>.Instance;
        SelectedDetail = new();
        _ = LoadAsync(CancellationToken.None);
    }

    // ═══════════════ 导航离开拦截 ═══════════════

    public override async Task<bool> CanLeaveAsync ()
    {
        var hasBlockChanges = ConfigBlockEditors.Any(ce => ce.IsDirty);
        if (!HasChanges && !hasBlockChanges) return true;

        var choice = await Dialog.ShowConfirmAsync(
            Resources.Strategy_UnsavedChanges ,
            Resources.Strategy_UnsavedChangesMsg);

        if (choice)
        {
            await SaveAllCommand.ExecuteAsync(null);
            // 保存后再次检查是否仍有未保存内容（保存可能部分失败）
            if (HasChanges)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 侧栏项属性变更时刷新全局 HasChanges。
    /// </summary>
    partial void OnSelectedStrategyChanged (StrategyItemViewModel? oldValue , StrategyItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedStrategyItemPropertyChanged;
            // 切换前自动保存旧策略的脏代码块，避免编辑内容丢失
            _ = SaveDirtyBlockEditorsAsync(oldValue);
        }

        if (newValue is null)
        {
            SelectedDetail = new();
            return;
        }
        // 订阅新项的属性变更，以同步 PriorityDisplay
        newValue.PropertyChanged += OnSelectedStrategyItemPropertyChanged;
        // 丢弃未保存的详情编辑（旧策略数据不应污染新策略配置）
        _hasDetailChanges = false;
        _ = LoadDetailAsync(newValue);
    }

    /// <summary>自动保存旧策略的脏代码块，失败时标记策略项为脏以确保离开时提示保存。</summary>
    private async Task SaveDirtyBlockEditorsAsync (StrategyItemViewModel oldItem)
    {
        var dirtyEditors = ConfigBlockEditors.Where(ce => ce.IsDirty && ce.IsLoaded).ToList();
        if (dirtyEditors.Count == 0) return;

        bool anyFailed = false;
        foreach (var editor in dirtyEditors)
        {
            try
            {
                await editor.SaveConfigCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "切换策略时自动保存代码块编辑器失败");
                anyFailed = true;
            }
        }

        if (anyFailed)
        {
            oldItem.HasChanges = true;
            StatusMessage = Resources.Strategy_SaveBlockFailed;
        }
    }

    private void OnSelectedStrategyItemPropertyChanged (object? sender , System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StrategyItemViewModel.Priority))
        {
            OnPropertyChanged(nameof(PriorityDisplay));
            OnPropertyChanged(nameof(EnableTooltipDisplay));
        }
    }

    // ═══════════════ 加载 ═══════════════

    private async Task LoadAsync (CancellationToken ct)
    {
        try
        {
            IsLoading = true;
            StatusMessage = Resources.Strategy_Loading;

            var displayInfos = await _facade.GetStrategiesAsync(ct);

            // 分类：独立策略 vs 依赖策略
            var independentInfos = displayInfos
                .Where(d => d.Visible && d.IsIndependent)
                .ToList();
            var dependentInfos = displayInfos
                .Where(d => d.Visible && !d.IsIndependent)
                .ToList();

            // 构建独立策略的 ViewModel
            var items = independentInfos
                .Select(d => new StrategyItemViewModel(
                    d.Id , d.DisplayName , d.Source , d.IsBuiltIn ,
                    d.Priority , d.DefaultPriority , d.IsEnabled ,
                    isIndependent: true))
                .ToList();

            // 将依赖策略展平插入到宿主（RandomFill）后面，避免 hover 冒泡
            var randomFill = items.FirstOrDefault(i => i.Id == RandomFillStrategy.StrategyId);
            if (randomFill != null && dependentInfos.Count > 0)
            {
                var children = dependentInfos
                    .OrderByDescending(d => d.Priority)
                    .Select(d => new StrategyItemViewModel(
                        d.Id , d.DisplayName , d.Source , d.IsBuiltIn ,
                        d.Priority , d.DefaultPriority , d.IsEnabled ,
                        isIndependent: false , isDependentChild: true))
                    .ToList();

                // 设置 Children 保留引用（SeatingArrangement 页面使用）
                randomFill.Children = new ObservableCollection<StrategyItemViewModel>(children);

                // 展平：在宿主后面插入依赖策略
                var hostIndex = items.IndexOf(randomFill);
                items.InsertRange(hostIndex + 1 , children);

                // 检测依赖策略内部优先级冲突
                var depFixed = DetectAndFixDependentPriorityConflicts(children);
                if (depFixed.Count > 0)
                {
                    var names = string.Join("\n" , depFixed.Select(n => $"• {n}"));
                    _logger.LogWarning("依赖策略优先级冲突已修复：{Names}" , names);
                }
            }

            foreach (var item in items)
                AttachChangeTracking(item);

            Strategies = new ObservableCollection<StrategyItemViewModel>(items);

            // 检测并修复初始优先级冲突（仅独立策略）
            var fixedList = DetectAndFixPriorityConflicts();
            if (fixedList.Count > 0)
            {
                var names = string.Join("\n" , fixedList.Select(n => $"• {n}"));
                await Dialog.ShowWarningAsync(
                    Resources.Strategy_PriorityConflictAutoFixed ,
                    string.Format(Resources.Strategy_PriorityConflictMsgFmt , names));
                RefreshPriorities();
                StatusMessage = string.Format(Resources.Strategy_LoadedFixedFmt , Strategies.Count , fixedList.Count);
            }
            else
                StatusMessage = string.Format(Resources.Strategy_LoadedFmt , Strategies.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_LoadFailed;
            await Dialog.ShowErrorAsync(Resources.Strategy_LoadFailed , ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>为策略项附加属性变更追踪。</summary>
    private void AttachChangeTracking (StrategyItemViewModel item)
    {
        item.PropertyChanged += (_ , e) =>
        {
            OnPropertyChanged(nameof(HasChanges));
            // 侧栏变更联动详情面板
            if (item == SelectedStrategy)
            {
                _suppressChangeTracking = true;
                if (e.PropertyName == nameof(StrategyItemViewModel.IsEnabled))
                    EditIsEnabled = item.IsEnabled;
                else if (e.PropertyName == nameof(StrategyItemViewModel.Priority))
                    EditPriority = item.Priority;
                _suppressChangeTracking = false;
            }
        };
    }

    private async Task LoadDetailAsync (StrategyItemViewModel item)
    {
        try
        {
            _suppressChangeTracking = true;

            var displayInfos = await _facade.GetStrategiesAsync(CancellationToken.None);
            var detail = displayInfos.FirstOrDefault(d => d.Id == item.Id);
            if (detail is null) return;

            SelectedDetail = detail;
            EditPriority = item.Priority;
            EditIsEnabled = item.IsEnabled;

            // 加载策略参数编辑器（manifest parameters[] 驱动）
            if (detail.ParameterDefinitions is { Count: > 0 })
            {
                var pe = new ParameterEditorViewModel();
                pe.LoadParameters(detail.ParameterDefinitions , detail.Parameters);
                pe.Parameters.CollectionChanged += (_ , _) => MarkDetailChanged();
                foreach (var p in pe.Parameters)
                    p.PropertyChanged += (_ , _) => MarkDetailChanged();
                ParameterEditor = pe;
            }
            else
            {
                ParameterEditor = null;
            }
            OnPropertyChanged(nameof(HasParameters));

            // 加载配置块编辑器（manifest codeBlocks[] 驱动）
            ConfigBlockEditors.Clear();
            if (detail.CodeBlocks is { Count: > 0 })
            {
                var datasets = await _facade.ListStudentDatasetsAsync(CancellationToken.None);
                var datasetItems = datasets.Select(d => new DatasetItem { Id = d.Id , Name = d.Name }).ToList();
                var venueIds = await _facade.ListVenueIdsAsync(CancellationToken.None);
                var venueItems = new List<DatasetItem>();
                foreach (var vid in venueIds)
                {
                    var name = vid; // 简化：用 ID 作为名称
                    venueItems.Add(new DatasetItem { Id = vid , Name = name });
                }

                foreach (var cb in detail.CodeBlocks)
                {
                    var ce = new ConfigBlockEditorViewModel(_facade);
                    ce.Initialize(cb , detail.Id , datasetItems , venueItems);
                    ce.PropertyChanged += (_ , e) =>
                    {
                        if (e.PropertyName == nameof(ConfigBlockEditorViewModel.IsDirty))
                            MarkDetailChanged();
                    };
                    ConfigBlockEditors.Add(ce);
                }
            }
            OnPropertyChanged(nameof(HasCodeBlocks));

            _suppressChangeTracking = false;
            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
        }
        catch (Exception ex)
        {
            _suppressChangeTracking = false;
            await Dialog.ShowErrorAsync(Resources.Strategy_DetailLoadFailed , ex.Message);
        }
    }

    // ═══════════════ 优先级调整 ═══════════════

    public bool CanMoveUp (StrategyItemViewModel? item)
    {
        if (item is null) return false;
        var sameGroup = Strategies.Where(s => IsSamePriorityGroup(s , item)).OrderByDescending(s => s.Priority).ToList();
        var idx = sameGroup.IndexOf(item);
        return idx > 0;
    }

    public bool CanMoveDown (StrategyItemViewModel? item)
    {
        if (item is null) return false;
        var sameGroup = Strategies.Where(s => IsSamePriorityGroup(s , item)).OrderByDescending(s => s.Priority).ToList();
        var idx = sameGroup.IndexOf(item);
        return idx >= 0 && idx < sameGroup.Count - 1;
    }

    [RelayCommand]
    private async Task MoveUpAsync (StrategyItemViewModel? item)
    {
        if (item is null) return;
        // 只在同类策略中查找邻居
        var sameGroup = Strategies.Where(s => IsSamePriorityGroup(s , item)).OrderByDescending(s => s.Priority).ToList();
        var idx = sameGroup.IndexOf(item);
        if (idx <= 0) return;

        var neighbor = sameGroup[idx - 1];
        await ResolveAndSwapPriorityAsync(item , neighbor);
        RefreshPriorities();
        StatusMessage = string.Format(Resources.Strategy_MovedUpFmt , item.DisplayName , item.Priority);
    }

    [RelayCommand]
    private async Task MoveDownAsync (StrategyItemViewModel? item)
    {
        if (item is null) return;
        var sameGroup = Strategies.Where(s => IsSamePriorityGroup(s , item)).OrderByDescending(s => s.Priority).ToList();
        var idx = sameGroup.IndexOf(item);
        if (idx < 0 || idx >= sameGroup.Count - 1) return;

        var neighbor = sameGroup[idx + 1];
        await ResolveAndSwapPriorityAsync(neighbor , item);
        RefreshPriorities();
        StatusMessage = string.Format(Resources.Strategy_MovedDownFmt , item.DisplayName , item.Priority);
    }

    private async Task ResolveAndSwapPriorityAsync (
        StrategyItemViewModel first ,
        StrategyItemViewModel second)
    {
        if (first.Priority < second.Priority)
        {
            (first.Priority , second.Priority) = (second.Priority , first.Priority);
            return;
        }

        if (first.Priority == second.Priority)
        {
            var choice = await Dialog.ShowConfirmAsync(
                Resources.Strategy_PriorityConflict ,
                string.Format(Resources.Strategy_PriorityConflictMsg , first.DisplayName , second.DisplayName , first.Priority) + "\n\n" +
                string.Format(Resources.Strategy_PriorityConflictChoice1 , first.DisplayName) + "\n" +
                string.Format(Resources.Strategy_PriorityConflictChoice2 , second.DisplayName));
            if (choice)
                AssignWithCascade(first , second);
            else
                AssignWithCascade(second , first);
        }
        else
        {
            (first.Priority , second.Priority) = (second.Priority , first.Priority);
        }
    }

    /// <summary>两个策略是否属于同一优先级命名空间（都是独立策略，或都是依赖策略）。</summary>
    private static bool IsSamePriorityGroup (StrategyItemViewModel a , StrategyItemViewModel b)
        => a.IsIndependent == b.IsIndependent;

    private void AssignWithCascade (StrategyItemViewModel higher , StrategyItemViewModel lower)
    {
        higher.Priority = lower.Priority == int.MaxValue
            ? int.MaxValue
            : lower.Priority + 1;
        RefreshPriorities();
    }

    // ═══════════════ 优先级冲突验证 ═══════════════

    /// <summary>
    /// 保存单个策略前检查优先级是否与其他策略冲突。
    /// </summary>
    private async Task<bool> ValidatePriorityBeforeSaveAsync (StrategyItemViewModel item , int newPriority)
    {
        // 只在同类策略中检查冲突（独立 vs 独立，依赖 vs 依赖）
        var conflict = Strategies.FirstOrDefault(s =>
            s.Id != item.Id
            && s.Priority == newPriority
            && IsSamePriorityGroup(s , item));

        if (conflict is null) return true;

        await Dialog.ShowWarningAsync(
            Resources.Strategy_PriorityConflict ,
            string.Format(Resources.Strategy_PriorityTakenFmt , newPriority , conflict.DisplayName));
        return false;
    }

    /// <summary>
    /// 批量保存前检查所有待保存策略之间及与其余策略的优先级冲突。
    /// </summary>
    private async Task<bool> ValidatePriorityBeforeSaveAllAsync (
        List<StrategyItemViewModel> dirtyItems ,
        string? detailStrategyId ,
        int? detailPriority)
    {
        // 构建保存后的优先级快照：dirty 项用新的 Priority，详情编辑用 EditPriority
        var snapshot = Strategies.Select(s =>
        {
            int priority;
            if (s.Id == detailStrategyId && detailPriority.HasValue)
                priority = detailPriority.Value;
            else if (dirtyItems.Any(d => d.Id == s.Id))
                priority = dirtyItems.First(d => d.Id == s.Id).Priority;
            else
                priority = s.Priority;

            return (s.Id , s.DisplayName , Priority: priority , s.IsIndependent);
        }).OrderBy(s => s.IsIndependent).ThenByDescending(s => s.Priority).ToList();

        // 只在同类策略中检查冲突
        var duplicates = new List<string>();
        for (int i = 1; i < snapshot.Count; i++)
        {
            if (snapshot[i].IsIndependent == snapshot[i - 1].IsIndependent
                && snapshot[i].Priority >= snapshot[i - 1].Priority)
                duplicates.Add(string.Format(Resources.Strategy_DuplicateEntryFmt , snapshot[i].DisplayName , snapshot[i].Priority));
        }

        if (duplicates.Count == 0) return true;

        await Dialog.ShowWarningAsync(
            Resources.Strategy_PriorityConflict ,
            string.Format(Resources.Strategy_DuplicateWarningFmt , string.Join("\n" , duplicates)));
        return false;
    }

    /// <summary>
    /// 确保所有策略优先级严格递减（无重复、无逆序），返回被修复的策略名列表。
    /// </summary>
    /// <summary>
    /// 检测并修复优先级冲突。独立策略和依赖策略分别检查，互不干扰。
    /// </summary>
    private List<string> DetectAndFixPriorityConflicts ()
    {
        var fixedNames = new List<string>();
        foreach (var group in Strategies.GroupBy(s => s.IsIndependent))
        {
            var ordered = group.OrderByDescending(s => s.Priority).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Priority >= ordered[i - 1].Priority)
                {
                    fixedNames.Add(ordered[i].DisplayName);
                    ordered[i].Priority = Math.Max(int.MinValue + 1 , ordered[i - 1].Priority - 1);
                }
            }
        }
        return fixedNames;
    }

    /// <summary>
    /// 检测依赖策略（同宿主内）的优先级冲突，确保严格递减不重复。
    /// 依赖策略的优先级仅与同宿主下的其他依赖策略比较，与独立策略完全分离。
    /// </summary>
    private static List<string> DetectAndFixDependentPriorityConflicts (List<StrategyItemViewModel> children)
    {
        var fixedNames = new List<string>();
        var ordered = children.OrderByDescending(s => s.Priority).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Priority >= ordered[i - 1].Priority)
            {
                fixedNames.Add(ordered[i].DisplayName);
                ordered[i].Priority = Math.Max(int.MinValue + 1 , ordered[i - 1].Priority - 1);
            }
        }
        return fixedNames;
    }

    private void EnsureUniquePriorities ()
    {
        foreach (var group in Strategies.GroupBy(s => s.IsIndependent))
        {
            var ordered = group.OrderByDescending(s => s.Priority).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Priority >= ordered[i - 1].Priority)
                    ordered[i].Priority = Math.Max(int.MinValue + 1 , ordered[i - 1].Priority - 1);
            }
        }
    }

    private void ReSort ()
    {
        // 独立策略按 Priority 降序，每个宿主的依赖子项紧跟其后。
        var result = new List<StrategyItemViewModel>();
        foreach (var item in Strategies.Where(s => s.IsIndependent).OrderByDescending(s => s.Priority))
        {
            result.Add(item);
            if (item.Children is { Count: > 0 })
                result.AddRange(item.Children.OrderByDescending(c => c.Priority));
        }

        for (int i = 0; i < result.Count; i++)
        {
            var currentIdx = Strategies.IndexOf(result[i]);
            if (currentIdx != i)
            {
                var it = Strategies[currentIdx];
                Strategies.RemoveAt(currentIdx);
                Strategies.Insert(i , it);
            }
        }
    }

    /// <summary>
    /// 刷新所有优先级相关数据：修复组内冲突 → 重排列表 → 刷新绑定。
    /// 在任何涉及优先级变更的操作后调用此方法。
    /// </summary>
    private void RefreshPriorities ()
    {
        EnsureUniquePriorities();
        ReSort();
        OnPropertyChanged(nameof(HasChanges));
    }

    // ═══════════════ 保存当前（详情页） ═══════════════

    /// <summary>
    /// 仅保存当前选中策略的配置（优先级 + 启用 + 参数）。
    /// 保存前检测优先级冲突，冲突则拒绝保存。
    /// </summary>
    [RelayCommand]
    private async Task SaveCurrentConfigAsync (CancellationToken ct)
    {
        if (SelectedDetail is null || SelectedStrategy is null) return;

        if (!await ValidatePriorityBeforeSaveAsync(SelectedStrategy , EditPriority))
            return;

        var savedName = SelectedDetail.DisplayName;

        try
        {
            IsLoading = true;
            StatusMessage = Resources.Strategy_Saving;

            // 将详情编辑同步到侧栏项
            SelectedStrategy.Priority = EditPriority;
            SelectedStrategy.IsEnabled = EditIsEnabled;

            var config = new StrategyConfig
            {
                Source = SelectedDetail.Source ,
                Priority = EditPriority ,
                IsEnabled = EditIsEnabled ,
                Parameters = CollectDetailParameters()
            };

            await _facade.SaveStrategyConfigAsync(SelectedDetail.Id , config , ct);
            SelectedStrategy.MarkClean();
            RefreshPriorities();

            // 同时保存所有 dirty 的代码块配置（DeskMate、FixedSeat 等）
            foreach (var ce in ConfigBlockEditors)
            {
                if (ce.IsDirty && ce.IsLoaded)
                    await ce.SaveConfigCommand.ExecuteAsync(null);
            }

            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = string.Format(Resources.Strategy_SavedFmt , savedName);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_SaveFailed;
            await Dialog.ShowErrorAsync(Resources.Strategy_SaveConfigFailed , ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══════════════ 保存全部（侧栏底部 + 离开弹窗） ═══════════════

    /// <summary>
    /// 保存所有有变更的策略。由侧栏底部按钮和离开弹窗触发。
    /// 保存前检测所有待保存策略的优先级冲突。
    /// </summary>
    [RelayCommand]
    private async Task SaveAllAsync (CancellationToken ct)
    {
        // 构建"保存后"的优先级快照（基于当前待保存值，尚未同步到侧栏）
        var dirtyItems = Strategies.Where(s => s.HasChanges).ToList();
        if (_hasDetailChanges && SelectedStrategy is not null && !dirtyItems.Contains(SelectedStrategy))
            dirtyItems.Add(SelectedStrategy);

        // 用待保存的优先级构建快照进行冲突检测
        int? detailPriority = _hasDetailChanges ? EditPriority : null;
        if (!await ValidatePriorityBeforeSaveAllAsync(dirtyItems , SelectedStrategy?.Id , detailPriority))
            return;

        if (dirtyItems.Count == 0)
        {
            StatusMessage = Resources.Strategy_NoChanges;
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = Resources.Strategy_Saving;

            // 同步详情编辑到侧栏项
            if (_hasDetailChanges && SelectedStrategy is not null)
            {
                SelectedStrategy.Priority = EditPriority;
                SelectedStrategy.IsEnabled = EditIsEnabled;
            }

            foreach (var item in dirtyItems)
            {
                var parameters = item.Id == SelectedDetail?.Id && _hasDetailChanges
                    ? CollectDetailParameters()
                    : null;
                var config = new StrategyConfig
                {
                    Source = item.Source ,
                    Priority = item.Priority ,
                    IsEnabled = item.IsEnabled ,
                    Parameters = parameters!
                };
                await _facade.SaveStrategyConfigAsync(item.Id , config , ct);
                item.MarkClean();
            }

            RefreshPriorities();

            // 同时保存所有 dirty 的代码块配置（DeskMate、FixedSeat 等）
            foreach (var ce in ConfigBlockEditors)
            {
                if (ce.IsDirty && ce.IsLoaded)
                    await ce.SaveConfigCommand.ExecuteAsync(null);
            }

            _hasDetailChanges = false;
            OnPropertyChanged(nameof(HasChanges));
            StatusMessage = string.Format(Resources.Strategy_SavedCountFmt , dirtyItems.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_SaveFailed;
            await Dialog.ShowErrorAsync(Resources.Strategy_SaveConfigFailed , ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ResetConfigAsync ()
    {
        if (SelectedDetail is null) return;

        var confirmed = await Dialog.ShowConfirmAsync(Resources.Strategy_RestoreDefaults ,
            string.Format(Resources.Strategy_RestoreDefaultConfirmFmt , SelectedDetail.DisplayName));
        if (!confirmed) return;

        EditPriority = SelectedDetail.DefaultPriority;
        EditIsEnabled = SelectedDetail.DefaultEnabled;

        // 重置参数为默认值
        if (SelectedDetail.ParameterDefinitions is { Count: > 0 })
        {
            ParameterEditor?.LoadParameters(SelectedDetail.ParameterDefinitions , null);
        }

        // 确认后直接保存
        await SaveCurrentConfigCommand.ExecuteAsync(null);
        StatusMessage = Resources.Strategy_RestoredAndSaved;
    }

    // ═══════════════ 辅助 ═══════════════

    private Dictionary<string , object?> CollectDetailParameters ()
    {
        if (ParameterEditor is not null)
            return ParameterEditor.CollectValues();
        return [];
    }

    private static int GetParamInt (Dictionary<string , object?> parameters , string key)
    {
        if (!parameters.TryGetValue(key , out var v) || v is null) return 0;
        if (v is int i) return i;
        if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
            return je.GetInt32();
        return 0;
    }

    private static bool GetParamBool (Dictionary<string , object?> parameters , string key)
    {
        if (!parameters.TryGetValue(key , out var v) || v is null) return false;
        if (v is bool b) return b;
        if (v is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.True) return true;
            if (je.ValueKind == System.Text.Json.JsonValueKind.False) return false;
        }
        return false;
    }
}
