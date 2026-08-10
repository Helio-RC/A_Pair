using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 配置块编辑器 ViewModel。管理单个 codeBlock 的数据集选择、配置行编辑和保存。
/// </summary>
public partial class ConfigBlockEditorViewModel (IApplicationFacade facade) : ViewModelBase
{
    private readonly IApplicationFacade _facade = facade;
    private CancellationTokenSource? _loadCts;
    private List<SeatFlow.Core.Models.Student>? _cachedStudents;
    /// <summary>缓存的会场布局定义，用于后续行创建时设置座位选择器范围。</summary>
    private ClassroomLayoutDefinition? _cachedLayout;

    // ── CodeBlock 定义 ──

    [ObservableProperty]
    public partial StrategyCodeBlock? CodeBlock { get; set; }

    public string LocalizedTitle => Helpers.LocalizeHelper.SafeResolve(CodeBlock?.Title);
    public string LocalizedDescription => Helpers.LocalizeHelper.SafeResolve(CodeBlock?.Description);
    public bool HasDescription => !string.IsNullOrEmpty(LocalizedDescription);

    public bool ShowStudentPicker => CodeBlock?.ShowStudentPicker
        ?? CodeBlock?.DataType is StrategyDataType.Student or StrategyDataType.Both;
    public bool ShowVenuePicker => CodeBlock?.ShowVenuePicker
        ?? CodeBlock?.DataType is StrategyDataType.Venue or StrategyDataType.Both;
    public bool ShowSeatPosition => CodeBlock?.ShowSeatPosition != false
        && CodeBlock?.DataType is StrategyDataType.Venue or StrategyDataType.Both;
    public bool HasCustomFields => CodeBlock?.Fields.Count > 0;
    public bool IsValuePair => CodeBlock?.DisplayMode == StrategyDisplayMode.ValuePair;

    // ── 数据集选择 ──

    /// <summary>策略 ID（用于持久化路径）。</summary>
    [ObservableProperty]
    public partial string StrategyId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<DatasetItem> AvailableDatasets { get; set; } = [];

    [ObservableProperty]
    public partial DatasetItem? SelectedDataset { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DatasetItem> AvailableVenues { get; set; } = [];

    [ObservableProperty]
    public partial DatasetItem? SelectedVenue { get; set; }

    partial void OnSelectedDatasetChanged (DatasetItem? value)
    {
        _ = LoadAndPopulateAsync();
    }

    partial void OnSelectedVenueChanged (DatasetItem? value)
    {
        _ = LoadAndPopulateAsync();
    }

    private async Task LoadAndPopulateAsync ()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        try
        {
            await LoadVenueLayoutInfoAsync(ct);
            await LoadConfigAsync(ct);
        }
        catch (OperationCanceledException) { /* 取消后仍继续加载学生，确保下拉列表刷新 */ }

        // 学生加载不受取消影响——确保切换数据集后下拉列表始终得到最新学生数据
        await PopulateStudentPickersAsync(CancellationToken.None);
    }

    // ── 配置行 ──

    [ObservableProperty]
    public partial ObservableCollection<ConfigBlockRowViewModel> Rows { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>每行学生选择器数量。初始值为 StudentPickerCount，SeatsPerDeskFromVenue 时从会场读取。</summary>
    [ObservableProperty]
    public partial int SeatsPerDesk { get; set; } = 1;

    public bool SeatsPerDeskFromVenue => CodeBlock?.SeatsPerDeskFromVenue == true;

    [RelayCommand]
    private void AddRow ()
    {
        var row = new ConfigBlockRowViewModel(CodeBlock , SeatsPerDesk)
        {
            Index = Rows.Count + 1
        };
        if (_cachedStudents is not null)
            row.LoadStudents(_cachedStudents);
        row.PropertyChanged += (_ , _) => IsDirty = true;
        WireRowStudentPickers(row);
        Rows.Add(row);
        if (_cachedLayout is not null)
            ApplyVenueLayoutBounds(_cachedLayout);
        ApplyGlobalDedup();
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveRow (ConfigBlockRowViewModel? row)
    {
        if (row is not null && Rows.Remove(row))
        {
            UnwireRowStudentPickers(row);
            IsDirty = true;
            Reindex();
            ApplyGlobalDedup();
        }
    }

    private void Reindex ()
    {
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].Index = i + 1;
    }

    // ── 加载/保存 ──

    public void Initialize (StrategyCodeBlock codeBlock , string strategyId ,
        IEnumerable<DatasetItem> datasets , IEnumerable<DatasetItem> venues)
    {
        CodeBlock = codeBlock;
        StrategyId = strategyId;
        SeatsPerDesk = Math.Max(1 , codeBlock?.StudentPickerCount ?? 1);
        AvailableDatasets = new ObservableCollection<DatasetItem>(datasets);
        AvailableVenues = new ObservableCollection<DatasetItem>(venues);

        OnPropertyChanged(nameof(LocalizedTitle));
        OnPropertyChanged(nameof(LocalizedDescription));
        OnPropertyChanged(nameof(ShowStudentPicker));
        OnPropertyChanged(nameof(ShowVenuePicker));
        OnPropertyChanged(nameof(ShowSeatPosition));
        OnPropertyChanged(nameof(HasCustomFields));
        OnPropertyChanged(nameof(IsValuePair));
    }

    private async Task LoadConfigAsync (CancellationToken ct)
    {
        if (string.IsNullOrEmpty(StrategyId)) return;

        var trigger = CodeBlock?.LoadTrigger ?? StrategyLoadTrigger.Both;
        bool isFuzzy = trigger == StrategyLoadTrigger.Any;

        // 守卫：根据 LoadTrigger 决定何时允许加载
        if (isFuzzy)
        {
            // 模糊模式：至少一个选择器有值
            if (SelectedDataset is null && SelectedVenue is null) return;
        }
        else
        {
            // 精确模式（默认）：已显示的选择器都必须有值
            bool needDataset = ShowStudentPicker;
            bool needVenue = ShowVenuePicker;
            if ((needDataset && SelectedDataset is null) || (needVenue && SelectedVenue is null))
            {
                Rows.Clear();  // 清除不匹配当前选择器的旧行
                return;
            }
        }

        var configs = await _facade.LoadStrategyDatasetConfigsAsync(StrategyId , ct);
        ct.ThrowIfCancellationRequested();

        // 过滤：根据 LoadTrigger 选择匹配策略
        var config = isFuzzy
            ? configs.FirstOrDefault(c =>
                (SelectedDataset is null || c.DatasetId == SelectedDataset.Id) &&
                (SelectedVenue is null || c.VenueId == SelectedVenue.Id))
            : configs.FirstOrDefault(c =>
                c.DatasetId == SelectedDataset?.Id && c.VenueId == SelectedVenue?.Id);

        Rows.Clear();
        if (config?.Rows is not null)
        {
            foreach (var row in config.Rows)
            {
                var vm = ConfigBlockRowViewModel.FromConfigRow(row , CodeBlock , SeatsPerDesk);
                vm.PropertyChanged += (_ , _) => IsDirty = true;
                WireRowStudentPickers(vm);
                Rows.Add(vm);
            }
        }
        IsLoaded = true;
        IsDirty = false;
        if (_cachedLayout is not null)
            ApplyVenueLayoutBounds(_cachedLayout);
        ApplyGlobalDedup();
    }

    private async Task PopulateStudentPickersAsync (CancellationToken ct)
    {
        if (SelectedDataset is null || !ShowStudentPicker) return;
        var students = await _facade.LoadStudentDatasetAsync(SelectedDataset.Id , ct);
        if (students is null) return;
        _cachedStudents = students;
        foreach (var row in Rows)
            row.LoadStudents(students);
        ApplyGlobalDedup();
    }

    /// <summary>
    /// 加载选中会场的数据：更新 SeatsPerDesk（DeskMate）和座位选择器范围（所有布局类型）。
    /// </summary>
    private async Task LoadVenueLayoutInfoAsync (CancellationToken ct)
    {
        if (SelectedVenue is null) { _cachedLayout = null; return; }
        var layout = await _facade.LoadVenueAsync(SelectedVenue.Id , ct);
        if (layout is null) return;

        // SeatsPerDesk 动态更新（仅 DeskMate：SeatsPerDeskFromVenue=true）
        if (SeatsPerDeskFromVenue && layout.Metadata is GridLayoutMetadata gridMeta)
        {
            var newSize = Math.Max(1 , gridMeta.SeatsPerDesk);
            if (SeatsPerDesk != newSize)
            {
                if (Rows.Count > 0)
                {
                    Rows.Clear();
                    IsDirty = true;
                }
                SeatsPerDesk = newSize;
            }
        }

        // 缓存布局，供后续行创建时设置座位选择器范围
        _cachedLayout = layout;
    }

    /// <summary>
    /// 根据会场布局元数据更新所有行的座位选择器范围。
    /// </summary>
    private void ApplyVenueLayoutBounds (ClassroomLayoutDefinition layout)
    {
        if (layout.Metadata is GridLayoutMetadata gm)
        {
            var maxRow = gm.ColumnRowCounts is { Count: > 0 }
                ? gm.ColumnRowCounts.Max()
                : gm.Rows;
            foreach (var row in Rows)
            {
                row.SeatPicker.GridMaxRow = Math.Max(1 , maxRow);
                row.SeatPicker.GridMaxColumn = Math.Max(1 , gm.Columns);
            }
        }
        else if (layout.Metadata is PolarLayoutMetadata pm)
        {
            var maxRing = pm.RingSeatCounts is { Count: > 0 }
                ? pm.RingSeatCounts.Count
                : pm.Rings;
            foreach (var row in Rows)
            {
                row.SeatPicker.PolarMaxRing = Math.Max(1 , maxRing);
            }
        }
        // Freeform: 无 Metadata 范围，保持默认值
    }

    [RelayCommand]
    private async Task SaveConfigAsync (CancellationToken ct)
    {
        // 根据 codeBlock 声明检查必选选择器是否已选定
        // 避免 dataType:Both 时仅选一项就保存出不完整的配置
        bool needDataset = ShowStudentPicker;
        bool needVenue = ShowVenuePicker;
        if ((needDataset && SelectedDataset is null) || (needVenue && SelectedVenue is null))
            return;

        var config = new StrategyDatasetConfig
        {
            StrategyId = StrategyId ,
            DatasetId = SelectedDataset?.Id ,
            VenueId = SelectedVenue?.Id ,
            Rows = [.. Rows.Select(r => r.ToConfigRow())]
        };
        await _facade.SaveStrategyDatasetConfigAsync(config , ct);
        IsDirty = false;
    }

    // ── 防重复逻辑 ──

    private bool _applyingGlobalDedup;

    /// <summary>
    /// 为指定行的所有 StudentPicker 订阅变更事件，当选中学生变化时触发全局防重复。
    /// </summary>
    private void WireRowStudentPickers (ConfigBlockRowViewModel row)
    {
        foreach (var sp in row.StudentPickers)
        {
            sp.PropertyChanged += OnStudentPickerChanged;
        }
        // 通过回调接收行级防重复的变更通知，用于触发全局防重复
        row.OnStudentSelectionChanged += OnRowStudentSelectionChanged;
    }

    /// <summary>
    /// 取消订阅指定行的 StudentPicker 变更事件。
    /// </summary>
    private void UnwireRowStudentPickers (ConfigBlockRowViewModel row)
    {
        foreach (var sp in row.StudentPickers)
        {
            sp.PropertyChanged -= OnStudentPickerChanged;
        }
        row.OnStudentSelectionChanged -= OnRowStudentSelectionChanged;
    }

    private void OnStudentPickerChanged (object? sender , System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudentPickerViewModel.SelectedStudent))
            ApplyGlobalDedup();
    }

    private void OnRowStudentSelectionChanged ()
    {
        ApplyGlobalDedup();
    }

    /// <summary>
    /// 全局防重复：遍历所有行的所有 StudentPicker，
    /// 将其他选择器已选的学生 ID 设为当前选择器的排除列表。
    /// 仅当 CodeBlock.PreventDuplicateAcrossRows 为 true 时生效。
    /// </summary>
    private void ApplyGlobalDedup ()
    {
        if (_applyingGlobalDedup) return;
        if (CodeBlock?.PreventDuplicateAcrossRows != true) return;

        _applyingGlobalDedup = true;
        try
        {
            // 收集所有选择器的已选学生 ID
            var allSelected = new HashSet<string>();
            foreach (var row in Rows)
            {
                foreach (var sp in row.StudentPickers)
                {
                    if (sp.SelectedStudentId is not null)
                        allSelected.Add(sp.SelectedStudentId);
                }
            }

            // 检测并清除跨行重复：若同一学生 ID 出现在多个选择器中，
            // 只保留第一个（按行序、选择器序），清除其余
            var seen = new HashSet<string>();
            foreach (var row in Rows)
            {
                foreach (var sp in row.StudentPickers)
                {
                    if (sp.SelectedStudentId is not null)
                    {
                        if (!seen.Add(sp.SelectedStudentId))
                        {
                            // 重复：清除该选择器的选中项
                            sp.SelectById(null);
                        }
                    }
                }
            }

            // 重新收集（可能在清除重复后变化）
            allSelected.Clear();
            foreach (var row in Rows)
            {
                foreach (var sp in row.StudentPickers)
                {
                    if (sp.SelectedStudentId is not null)
                        allSelected.Add(sp.SelectedStudentId);
                }
            }

            // 为每个选择器设置排除列表（排除所有其他选择器的已选学生）
            foreach (var row in Rows)
            {
                foreach (var sp in row.StudentPickers)
                {
                    var excluded = new HashSet<string>(allSelected);
                    // 保留当前选择器自己的选中项（不下拉消失）
                    if (sp.SelectedStudentId is not null)
                        excluded.Remove(sp.SelectedStudentId);
                    sp.SetExcludedIds(excluded);
                }
            }
        }
        finally
        {
            _applyingGlobalDedup = false;
        }
    }
}

/// <summary>
/// 单个配置行的 ViewModel。支持按 SeatsPerDesk 动态数量的 StudentPicker。
/// </summary>
public partial class ConfigBlockRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial StrategyCodeBlock? CodeBlock { get; set; }
    private bool _applyingRowDedup;
    /// <summary>待定学生选择（索引→学生ID），在 LoadStudents 完成后自动应用。</summary>
    private Dictionary<int , string?>? _pendingSelections;

    public ConfigBlockRowViewModel (StrategyCodeBlock? codeBlock , int seatsPerDesk = 1)
    {
        CodeBlock = codeBlock;
        SeatsPerDesk = seatsPerDesk;
        SeatPicker = new SeatPositionPickerViewModel();
        for (int i = 0; i < seatsPerDesk; i++)
        {
            var sp = new StudentPickerViewModel();
            sp.PropertyChanged += OnStudentPickerPropertyChanged;
            StudentPickers.Add(sp);
        }
    }

    /// <summary>当任何 StudentPicker 的选中学生变化时触发（用于编辑器级别的全局防重复）。</summary>
    public event Action? OnStudentSelectionChanged;

    private void OnStudentPickerPropertyChanged (object? sender , System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudentPickerViewModel.SelectedStudent))
        {
            ApplyRowLevelDedup();
            OnStudentSelectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// 行级防重复：同一行内的多个 StudentPicker 互相排除已选学生，
    /// 确保同行内不会选中同一学生。仅当 CodeBlock.PreventDuplicateInRow 为 true 时生效。
    /// 注意：若同时启用了 PreventDuplicateAcrossRows，则由 ConfigBlockEditorViewModel 统一处理，
    /// 本方法仅在没有全局防重复时执行（避免重复计算排除集）。
    /// </summary>
    private void ApplyRowLevelDedup ()
    {
        if (_applyingRowDedup) return;
        if (CodeBlock?.PreventDuplicateInRow != true) return;
        // 若编辑器侧已启用全局防重复，则由 ApplyGlobalDedup 统一处理
        if (CodeBlock.PreventDuplicateAcrossRows) return;

        _applyingRowDedup = true;
        try
        {
            // 收集当前行所有选择器的已选学生 ID
            var selectedIds = new HashSet<string>();
            foreach (var sp in StudentPickers)
            {
                if (sp.SelectedStudentId is not null)
                    selectedIds.Add(sp.SelectedStudentId);
            }

            // 检测并清除同行重复
            var seen = new HashSet<string>();
            foreach (var sp in StudentPickers)
            {
                if (sp.SelectedStudentId is not null)
                {
                    if (!seen.Add(sp.SelectedStudentId))
                    {
                        // 重复：清除该选择器
                        sp.SelectById(null);
                    }
                }
            }

            // 重新收集
            selectedIds.Clear();
            foreach (var sp in StudentPickers)
            {
                if (sp.SelectedStudentId is not null)
                    selectedIds.Add(sp.SelectedStudentId);
            }

            // 应用排除列表（排除其他选择器的已选学生）
            foreach (var sp in StudentPickers)
            {
                var excluded = new HashSet<string>(selectedIds);
                // 保留当前选择器自己的选中项（不下拉消失）
                if (sp.SelectedStudentId is not null)
                    excluded.Remove(sp.SelectedStudentId);
                sp.SetExcludedIds(excluded);
            }
        }
        finally
        {
            _applyingRowDedup = false;
        }
    }

    [ObservableProperty]
    public partial int Index { get; set; }
    public int SeatsPerDesk { get; }

    /// <summary>动态数量的学生选择器（由 SeatsPerDesk 决定）。</summary>
    public ObservableCollection<StudentPickerViewModel> StudentPickers { get; } = [];

    public SeatPositionPickerViewModel SeatPicker { get; }

    /// <summary>CodeBlock 是否声明了性别选择器。</summary>
    public bool ShowGenderPicker => CodeBlock?.ShowGenderPicker == true;

    /// <summary>性别下拉选项列表（静态）。</summary>
    public static List<string> GenderOptions { get; } = ["Male" , "Female"];

    /// <summary>性别选择器的当前值（"Male" / "Female" / null），直接读写 CustomValues["Gender"]。</summary>
    [ObservableProperty]
    public partial string? GenderValue { get; set; }

    partial void OnGenderValueChanged (string? value)
    {
        CustomValues["Gender"] = value;
    }

    /// <summary>自定义字段值。</summary>
    [ObservableProperty]
    public partial Dictionary<string , object?> CustomValues { get; set; } = [];

    public void SetCustomValue (string name , object? value)
    {
        CustomValues[name] = value;
        OnPropertyChanged(nameof(CustomValues));
    }

    public bool ShowStudentPicker => CodeBlock?.ShowStudentPicker
        ?? CodeBlock?.DataType is StrategyDataType.Student or StrategyDataType.Both;
    public bool ShowSeatPosition => CodeBlock?.ShowSeatPosition != false
        && CodeBlock?.DataType is StrategyDataType.Venue or StrategyDataType.Both;

    /// <summary>加载学生到所有 StudentPicker，加载后自动应用待定的持久化选择。</summary>
    public void LoadStudents (IEnumerable<SeatFlow.Core.Models.Student> students)
    {
        var list = students.ToList();
        foreach (var sp in StudentPickers)
            sp.LoadStudents(list);
        ApplyPendingSelections();
    }

    /// <summary>
    /// 在学生列表就绪后应用 FromConfigRow 存储的待定选择。
    /// </summary>
    private void ApplyPendingSelections ()
    {
        if (_pendingSelections is null) return;
        for (int i = 0; i < StudentPickers.Count; i++)
        {
            if (_pendingSelections.TryGetValue(i , out var sid))
                StudentPickers[i].SelectById(sid);
        }
        _pendingSelections = null;
    }

    public static ConfigBlockRowViewModel FromConfigRow (StrategyConfigRow row , StrategyCodeBlock? codeBlock , int seatsPerDesk = 1)
    {
        var vm = new ConfigBlockRowViewModel(codeBlock , seatsPerDesk)
        {
            Index = row.Index ,
            CustomValues = row.Values ?? []
        };
        // 延迟学生选择：将 ID 存入 _pendingSelections，等 LoadStudents 完成后自动应用
        // （避免 SelectById 在 Students 列表为空时被调用导致选中信息丢失）
        vm._pendingSelections = new Dictionary<int , string?>();
        if (row.StudentId is not null)
            vm._pendingSelections[0] = row.StudentId;
        // 额外的同桌学生（存入 CustomValues["student1"], ["student2"], ...）
        for (int i = 1; i < vm.StudentPickers.Count; i++)
        {
            var key = $"student{i}";
            if (row.Values?.TryGetValue(key , out var sid) == true)
                vm._pendingSelections[i] = sid?.ToString();
        }
        vm.SeatPicker.Row = row.SeatRow ?? 1;
        vm.SeatPicker.Column = row.SeatColumn ?? 1;
        vm.SeatPicker.Ring = row.SeatRing ?? 1;
        vm.SeatPicker.Angle = row.SeatAngle ?? 0;
        vm.SeatPicker.X = row.SeatX ?? 0;
        vm.SeatPicker.Y = row.SeatY ?? 0;
        // 恢复性别值
        if (row.Values?.TryGetValue("Gender" , out var gv) == true)
            vm.GenderValue = gv?.ToString();
        return vm;
    }

    public StrategyConfigRow ToConfigRow ()
    {
        var values = new Dictionary<string , object?>(CustomValues);
        // 第一个学生存到 StudentId，其余存到 values
        var studentId = StudentPickers.Count > 0 ? StudentPickers[0].SelectedStudentId : null;
        for (int i = 1; i < StudentPickers.Count; i++)
            values[$"student{i}"] = StudentPickers[i].SelectedStudentId;
        return new StrategyConfigRow
        {
            Index = Index ,
            StudentId = studentId ,
            SeatRow = SeatPicker.IsGrid ? SeatPicker.Row : null ,
            SeatColumn = SeatPicker.IsGrid ? SeatPicker.Column : null ,
            SeatRing = SeatPicker.IsPolar ? SeatPicker.Ring : null ,
            SeatAngle = SeatPicker.IsPolar ? SeatPicker.Angle : null ,
            SeatX = SeatPicker.IsFreeform ? SeatPicker.X : null ,
            SeatY = SeatPicker.IsFreeform ? SeatPicker.Y : null ,
            Values = values
        };
    }
}

/// <summary>
/// 数据集/会场选择项。
/// </summary>
public class DatasetItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString () => string.IsNullOrEmpty(Name) ? Id : Name;
}
