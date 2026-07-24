using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Services;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

public partial class MemberManagementViewModel : ViewModelBase, IFileDropHandler
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialog;
    private readonly ILogger<MemberManagementViewModel> _logger;

    [ObservableProperty]
    public partial ObservableCollection<Student> Students { get; set; } = [];

    /// <summary>底部新增行的绑定源，用户填写后通过 AddNewStudentCommand 加入表格。</summary>
    [ObservableProperty]
    public partial Student NewStudent { get; set; } = new();

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    public partial bool IsLoading { get; set; }

    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    [NotifyPropertyChangedFor(nameof(IsImportMode))]
    [NotifyPropertyChangedFor(nameof(IsUpdateMode))]
    public partial bool IsEmpty { get; set; } = true;

    public bool HasData => !IsEmpty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = Resources.Member_Ready;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int StudentCount { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<StudentDatasetInfo> SavedDatasets { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDataset))]
    public partial StudentDatasetInfo? SelectedDataset { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDatasets { get; set; }

    [ObservableProperty]
    public partial string? CurrentDatasetId { get; set; }

    [ObservableProperty]
    public partial string? CurrentDatasetName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpanded))]
    public partial bool IsCompact { get; set; }

    public bool HasExpanded => !IsCompact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    public partial bool IsSidebarExpanded { get; set; } = true;

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    public partial double SidebarListWidth { get; set; } = 240;

    private bool _userWantsSidebarExpanded = true;

    // ── 脏状态追踪 ──
    private static readonly JsonSerializerOptions _studentJsonOptions = new()
    {
        WriteIndented = false ,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private string? _originalStudentsJson;
    private StudentDatasetInfo? _previousDataset;
    private bool _suppressDatasetLoad;

    private bool IsNewStudentDirty =>
        !string.IsNullOrWhiteSpace(NewStudent.Name) ||
        NewStudent.Height.HasValue ||
        NewStudent.Gender.HasValue ||
        NewStudent.NeedsFrontRow;

    private bool IsDirty =>
        IsNewStudentDirty ||
        (_originalStudentsJson != null &&
        SerializeStudents() != _originalStudentsJson);

    private string SerializeStudents () =>
        JsonSerializer.Serialize(Students , _studentJsonOptions);

    private void MarkClean () => _originalStudentsJson = SerializeStudents();
    private void MarkDirty () => _originalStudentsJson ??= "";

    partial void OnSelectedDatasetChanged (StudentDatasetInfo? value)
    {
        if (_suppressDatasetLoad)
            return;
        if (value is null)
        {
            // 用户取消选中（Toggle 模式再次点击已选项） → 卸载数据
            _ = ClearDataInternalAsync();
            return;
        }
        _ = SwitchToDatasetAsync(value);
    }

    private async Task SwitchToDatasetAsync (StudentDatasetInfo target)
    {
        if (IsDirty)
        {
            var choice = await Dialog.ShowMultiOptionAsync(
                Resources.Member_UnsavedChanges ,
                Resources.Member_UnsavedChangesMsg ,
                Resources.Common_Save ,
                Resources.Common_Discard ,
                Resources.Common_Cancel);

            switch (choice)
            {
                case 0: // 保存
                    await SaveInternalAsync(CancellationToken.None);
                    break;
                case 1: // 放弃
                    break;
                default: // 取消或关闭窗口
                    _suppressDatasetLoad = true;
                    SelectedDataset = _previousDataset;
                    _suppressDatasetLoad = false;
                    return;
            }
        }

        NewStudent = new Student();
        await LoadDatasetAsync(target , CancellationToken.None);
        _previousDataset = target;
    }

    public void OnWindowWidthChanged (double windowWidth)
    {
        IsCompact = windowWidth < 960;
        if (windowWidth < 780)
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarListWidth = value ? 240 : 100;

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsSidebarExpanded = !_userWantsSidebarExpanded;
        IsSidebarExpanded = _userWantsSidebarExpanded;
    }
    public bool HasSelectedDataset => SelectedDataset is not null;

    /// <summary>编辑区无数据时显示"从文件导入"按钮。</summary>
    public bool IsImportMode => !HasData;

    /// <summary>编辑区有数据时显示"从文件更新"按钮。</summary>
    public bool IsUpdateMode => HasData;

    /// <summary>仅供引导系统使用：设置演示数据集但不触发磁盘加载。</summary>
    public void SetGuideDataset (StudentDatasetInfo dataset)
    {
        _suppressDatasetLoad = true;
        SelectedDataset = dataset;
        _suppressDatasetLoad = false;
    }

    /// <summary>仅供引导系统使用：设置 _suppressDatasetLoad 阻止 SelectedDataset 变化时的副作用。</summary>
    internal void SetSuppressDatasetLoad (bool value) => _suppressDatasetLoad = value;

    /// <summary>仅供引导系统使用：重置脏状态追踪快照，防止 ClearPageData 后的误判。</summary>
    internal void ResetDirtyState () => _originalStudentsJson = null;

    public string StudentCountDisplay => string.Format(Resources.Member_MemberCountFmt , StudentCount);
    public string FilePathDisplay => string.IsNullOrEmpty(FilePath) ? "" : string.Format(Resources.Member_DataSourceFmt , FilePath);
    public string StudentCountDisplay2 => string.Format(Resources.Member_PersonCountFmt , StudentCount);

    public MemberManagementViewModel (IApplicationFacade facade , IFileService fileService , IDialogService dialog , ILogger<MemberManagementViewModel>? logger = null)
    {
        _facade = facade;
        _fileService = fileService;
        _dialog = dialog;
        _logger = logger ?? NullLogger<MemberManagementViewModel>.Instance;
        _ = RefreshDatasetsAsync(CancellationToken.None);
    }

    private async Task RefreshDatasetsAsync (CancellationToken ct)
    {
        try
        {
            var datasets = await _facade.ListStudentDatasetsAsync(ct);
            SavedDatasets = new ObservableCollection<StudentDatasetInfo>(datasets);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "刷新数据集列表失败");
        }
    }

    private int _dialogLock;
    private static readonly FilePickerFileType[] StudentFileTypes =
    [
        new(Resources.Member_MemberDataFile) { Patterns = ["*.csv", "*.xlsx", "*.json"] },
        new(Resources.Data_CSVFile) { Patterns = ["*.csv"] },
        new(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] },
        new(Resources.Data_JSONFile) { Patterns = ["*.json"] },
        FilePickerFileTypes.All
    ];

    private static readonly FilePickerFileType[] TemplateFileTypes =
    [
        new(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }
    ];

    private static readonly Dictionary<string , (string Suffix , string DisplayName)> TemplateLocales = new()
    {
        ["zh_cn"] = ("zh_cn" , Resources.Member_SampleFileCN) ,
        ["zh_tw"] = ("zh_tw" , "學生匯入範本.xlsx") ,
        ["ja_jp"] = ("ja_jp" , "学生インポートテンプレート.xlsx") ,
        ["ko_kr"] = ("ko_kr" , "학생가져오기템플릿.xlsx") ,
    };

    private const string DefaultTemplateSuffix = "en_us";
    private const string DefaultTemplateDisplayName = "MemberImportTemplate.xlsx";

    [RelayCommand]
    private async Task ExportTemplateAsync (CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return;
        try
        {
            string? errorTitle = null;
            string? errorMsg = null;

            try
            {
                var (suffix , displayName) = await ResolveTemplateLocaleAsync(ct);
                var uri = new Uri($"avares://SeatFlow/Assets/Files/Sample_{suffix}.xlsx");

                if (!AssetLoader.Exists(uri))
                {
                    suffix = DefaultTemplateSuffix;
                    displayName = DefaultTemplateDisplayName;
                    uri = new Uri($"avares://SeatFlow/Assets/Files/Sample_{suffix}.xlsx");
                }

                if (!AssetLoader.Exists(uri))
                {
                    errorTitle = Resources.Member_TemplateMissing;
                    errorMsg = string.Format(Resources.Member_TemplateMissingMsg);
                    return;
                }

                IStorageFile? tmplFile;
                try { tmplFile = await _fileService.SaveFileAsync(Resources.Common_Save , TemplateFileTypes , displayName); }
                catch (Exception ex) { _logger.LogDebug(ex , "文件对话框取消或异常: 导出模板"); return; }
                if (tmplFile is null) return;

                using var source = AssetLoader.Open(uri);
                await using var destination = File.Create(tmplFile.Path.LocalPath);
                await source.CopyToAsync(destination , ct);

                StatusMessage = Resources.Data_TemplateSaved;
            }
            catch (Exception ex)
            {
                errorTitle = Resources.Data_TemplateSaveFailed;
                errorMsg = string.Format(Resources.Member_TemplateSaveError) + "\n" + ex.Message;
            }
            finally
            {
                if (errorTitle != null)
                    await _dialog.ShowErrorAsync(errorTitle , errorMsg!);
            }
        }
        finally { await Task.Delay(150 , CancellationToken.None); Interlocked.Exchange(ref _dialogLock , 0); }
    }

    private async Task<(string Suffix , string DisplayName)> ResolveTemplateLocaleAsync (CancellationToken ct)
    {
        try
        {
            var settings = await _facade.LoadAppSettingsAsync(ct);
            var lang = !string.IsNullOrEmpty(settings.Language)
                ? settings.Language
                : CultureInfo.CurrentUICulture.Name.Replace('-' , '_').ToLowerInvariant();

            if (TemplateLocales.TryGetValue(lang , out var entry))
                return entry;

            var prefix = lang.Split('_')[0];
            var fallback = TemplateLocales.FirstOrDefault(kv => kv.Key.StartsWith(prefix));
            return fallback.Value is (var f, var d) ? (f , d) : (DefaultTemplateSuffix , DefaultTemplateDisplayName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "解析模板语言失败，使用默认值");
            return (DefaultTemplateSuffix , DefaultTemplateDisplayName);
        }
    }

    [RelayCommand]
    private async Task ImportAsync (CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return;
        try
        {
            IStorageFile? importFile;
            try { importFile = await _fileService.OpenFileAsync(Resources.Member_ImportData , StudentFileTypes); }
            catch (Exception ex) { _logger.LogDebug(ex , "文件对话框取消或异常: 导入"); return; }
            if (importFile is null) return;

            await ImportFromPathAsync(importFile.Path.LocalPath , ct);
        }
        finally { await Task.Delay(150 , CancellationToken.None); Interlocked.Exchange(ref _dialogLock , 0); }
    }

    /// <summary>70×70 自动扫描阈值。</summary>
    private const int MaxAutoScanSize = 70;

    /// <summary>打开用户文档中的人员管理导入帮助页面。</summary>
    private static void OpenHelpDocs ()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://seatflow.work/docs/user/03-member-management#section-7" ,
                UseShellExecute = true
            });
        }
        catch
        {
            // 无法打开浏览器时静默忽略
        }
    }

    /// <summary>从指定路径导入学生数据（跳过文件对话框）。供 ImportAsync 和拖放使用。</summary>
    /// <returns>true 表示导入成功（至少有一条有效数据）。</returns>
    private async Task<bool> ImportFromPathAsync (string filePath , CancellationToken ct)
    {
        string? errorTitle = null;
        string? errorMsg = null;

        try
        {
            FilePath = filePath;
            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = Resources.Member_Importing;

            // Phase 1: 检查数据范围，超出阈值则弹窗询问
            int scanRows, scanCols;
            try
            {
                var (totalRows , totalCols) = await _facade.GetDataSourceDimensionsAsync(FilePath , ct);
                if (totalRows > MaxAutoScanSize || totalCols > MaxAutoScanSize)
                {
                    var choice = await Dialog.ShowMultiOptionAsync(
                        Resources.Member_ImportRangeTooLarge ,
                        string.Format(Resources.Member_ImportRangeTooLargeMsg , totalRows , totalCols) ,
                        Resources.Member_ImportFullScan ,
                        Resources.Member_ImportLimitedScan);
                    if (choice == 0) // 完全扫描
                    {
                        scanRows = totalRows;
                        scanCols = totalCols;
                    }
                    else // 仅扫描前 70×70（或取消）
                    {
                        scanRows = Math.Min(totalRows , MaxAutoScanSize);
                        scanCols = Math.Min(totalCols , MaxAutoScanSize);
                    }
                }
                else
                {
                    scanRows = totalRows;
                    scanCols = totalCols;
                }
            }
            catch
            {
                // 维度读取失败时使用默认值，让 LoadStudentsAsync 自行处理
                scanRows = MaxAutoScanSize;
                scanCols = MaxAutoScanSize;
            }

            // Phase 2: 加载学生数据（使用 Phase 1 确定的范围）
            var students = await _facade.LoadStudentsAsync(FilePath , scanRows , scanCols , ct);

            Students = new ObservableCollection<Student>(students);
            StudentCount = Students.Count;
            IsEmpty = StudentCount == 0;
            StatusMessage = IsEmpty ? Resources.Member_NoImport : $"已导入 {StudentCount} 名学生";

            // 自动保存到托管存储
            if (!IsEmpty)
            {
                var name = Path.GetFileNameWithoutExtension(FilePath);
                CurrentDatasetId = await _facade.SaveStudentDatasetAsync(name , students , Path.GetFileName(FilePath) , ct);
                CurrentDatasetName = name;
                MarkClean();
                _ = RefreshDatasetsAsync(ct);
            }

            if (IsEmpty)
            {
                errorTitle = Resources.Member_ImportResult;
                errorMsg = Resources.Member_NoNameField;
            }

            return !IsEmpty;
        }
        catch (Exception ex)
        {
            errorTitle = Resources.Member_ImportFailed;
            errorMsg = ex is FileNotFoundException
                ? string.Format(Resources.Member_FileNotFoundFmt , FilePath)
                : string.Format(Resources.Member_ImportErrorFmt , ex.Message);
            StatusMessage = Resources.Member_ImportFailed;
            return false;
        }
        finally
        {
            IsLoading = false;
            if (errorTitle != null)
            {
                // 导入失败：显示带帮助按钮的对话框
                var choice = await Dialog.ShowMultiOptionAsync(
                    errorTitle , errorMsg! ,
                    Resources.Common_OK ,
                    Resources.Member_ViewHelp);
                if (choice == 1) // 查看帮助
                    OpenHelpDocs();
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  IFileDropHandler
    // ═══════════════════════════════════════════════

    IReadOnlyList<string> IFileDropHandler.AcceptedFileExtensions { get; } =
        [".csv", ".xlsx", ".json"];

    async Task<bool> IFileDropHandler.HandleFileDropAsync (IReadOnlyList<string> filePaths , CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0)
            return false;
        try
        {
            return await ImportFromPathAsync(filePaths[0] , ct);
        }
        finally
        {
            await Task.Delay(150 , CancellationToken.None);
            Interlocked.Exchange(ref _dialogLock , 0);
        }
    }

    /// <summary>从文件更新当前数据集，保持 CurrentDatasetId 不变。</summary>
    [RelayCommand]
    private async Task UpdateFromFileAsync (CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return;
        try
        {
            string? errorTitle = null;
            string? errorMsg = null;

            try
            {
                IStorageFile? importFile;
                try { importFile = await _fileService.OpenFileAsync(Resources.Member_UpdateFromFile , StudentFileTypes); }
                catch (Exception ex) { _logger.LogDebug(ex , "文件对话框取消或异常: 打开文件"); return; }
                if (importFile is null) return;
                var file = importFile;

                FilePath = file.Path.LocalPath;
                IsLoading = true;
                ErrorMessage = string.Empty;
                StatusMessage = "正在更新...";

                var students = await _facade.LoadStudentsAsync(FilePath , ct);

                Students = new ObservableCollection<Student>(students);
                StudentCount = Students.Count;
                IsEmpty = StudentCount == 0;
                StatusMessage = IsEmpty ? "文件中无有效数据" : $"已从文件更新 {StudentCount} 名学生";

                // 保存到托管存储
                if (!IsEmpty)
                {
                    var name = CurrentDatasetName ?? Path.GetFileNameWithoutExtension(FilePath);
                    if (CurrentDatasetId is not null)
                    {
                        // 已有关联数据集 → 原地更新，保持 ID 不变
                        await _facade.UpdateStudentDatasetAsync(CurrentDatasetId , name , students ,
                            Path.GetFileName(FilePath) , ct);
                    }
                    else
                    {
                        // 无关联数据集（未保存的编辑区数据）→ 另存为新数据集
                        CurrentDatasetId = await _facade.SaveStudentDatasetAsync(name , students ,
                            Path.GetFileName(FilePath) , ct);
                        CurrentDatasetName = name;
                    }
                    MarkClean();
                    _ = RefreshDatasetsAsync(ct);
                }

                if (IsEmpty)
                {
                    errorTitle = "更新结果";
                    errorMsg = "文件中未找到有效学生数据。";
                }
            }
            catch (Exception ex)
            {
                errorTitle = Resources.Member_ImportFailed;
                errorMsg = ex is FileNotFoundException
                    ? string.Format(Resources.Member_FileNotFoundFmt , FilePath)
                    : $"更新失败：{ex.Message}";
                StatusMessage = "更新失败";
            }
            finally
            {
                IsLoading = false;
                if (errorTitle != null)
                    await _dialog.ShowErrorAsync(errorTitle , errorMsg!);
            }
        }
        finally { await Task.Delay(150 , CancellationToken.None); Interlocked.Exchange(ref _dialogLock , 0); }
    }

    [RelayCommand]
    private async Task ExportCsvAsync (CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Csv , [new(Resources.Data_CSVFile) { Patterns = ["*.csv"] }] , ct);
    }

    [RelayCommand]
    private async Task ExportExcelAsync (CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Excel , [new(Resources.Data_ExcelFile) { Patterns = ["*.xlsx"] }] , ct);
    }

    [RelayCommand]
    private async Task ExportJsonAsync (CancellationToken ct)
    {
        await ExportAsync(ExportFormat.Json , [new(Resources.Data_JSONFile) { Patterns = ["*.json"] }] , ct);
    }

    private async Task ExportAsync (ExportFormat format , FilePickerFileType[] types , CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return;
        try
        {
            string? errorTitle = null;
            string? errorMsg = null;

            if (Students.Count == 0)
            {
                await _dialog.ShowWarningAsync("无数据" , Resources.Member_NoDataToExport);
                return;
            }

            try
            {
                IStorageFile? exportFile;
                try { exportFile = await _fileService.SaveFileAsync(Resources.Data_Export , types); }
                catch (Exception ex) { _logger.LogDebug(ex , "文件对话框取消或异常: 导出CSV"); return; }
                if (exportFile is null) return;
                var file = exportFile;

                IsLoading = true;
                ErrorMessage = string.Empty;
                StatusMessage = Resources.Member_Exporting;

                await _facade.ExportStudentsAsync(file.Path.LocalPath , Students , format , ct);

                StatusMessage = Resources.Member_ExportDone;
            }
            catch (Exception ex)
            {
                errorTitle = Resources.Member_ExportFailed;
                errorMsg = string.Format(Resources.Member_ExportErrorFmt , ex.Message);
                StatusMessage = Resources.Member_ExportFailed;
            }
            finally
            {
                IsLoading = false;
                if (errorTitle != null)
                    await _dialog.ShowErrorAsync(errorTitle , errorMsg!);
            }
        }
        finally { await Task.Delay(150 , CancellationToken.None); Interlocked.Exchange(ref _dialogLock , 0); }
    }

    [RelayCommand]
    private async Task ClearDataAsync ()
    {
        await ClearDataInternalAsync();
    }

    /// <summary>卸载数据的核心逻辑（可由取消选中或按钮触发）。仅 IsDirty 时弹确认窗。</summary>
    private async Task ClearDataInternalAsync ()
    {
        if (IsDirty)
        {
            var confirmed = await _dialog.ShowConfirmAsync(Resources.Member_ClearConfirm ,
                string.Format(Resources.Member_ClearConfirmMsg , StudentCount));
            if (!confirmed) return;
        }

        Students.Clear();
        StudentCount = 0;
        IsEmpty = true;
        CurrentDatasetId = null;
        CurrentDatasetName = null;
        FilePath = string.Empty;
        ErrorMessage = string.Empty;
        _originalStudentsJson = null;
        NewStudent = new Student();
        StatusMessage = Resources.Member_Ready;
    }

    public override async Task<bool> CanLeaveAsync ()
    {
        if (!IsDirty && !IsNewStudentDirty)
        {
            await ClearDataInternalAsync();
            return true;
        }

        var choice = await Dialog.ShowMultiOptionAsync(
            Resources.Member_UnsavedChanges ,
            Resources.Member_UnsavedChangesMsg ,
            Resources.Common_Save ,
            Resources.Common_Discard ,
            Resources.Common_Cancel);

        switch (choice)
        {
            case 0: // 保存
                await SaveInternalAsync(CancellationToken.None);
                break;
            case 1: // 放弃
                break;
            default: // 取消
                return false;
        }

        await ClearDataInternalAsync();
        return true;
    }

    [RelayCommand]
    private void DeleteStudent (Student student)
    {
        if (Students.Remove(student))
        {
            MarkDirty();
            StudentCount = Students.Count;
            IsEmpty = StudentCount == 0;
            StatusMessage = string.Format(Resources.Member_DeletedRowFmt , student.Name , StudentCount);
        }
    }

    [RelayCommand]
    private void AddNewStudent ()
    {
        if (string.IsNullOrWhiteSpace(NewStudent.Name))
            return;

        MarkDirty();

        Students.Add(new Student
        {
            Name = NewStudent.Name.Trim() ,
            Height = NewStudent.Height ,
            Gender = NewStudent.Gender ,
            NeedsFrontRow = NewStudent.NeedsFrontRow
        });

        NewStudent = new Student();
        StudentCount = Students.Count;
        IsEmpty = false;
        StatusMessage = string.Format(Resources.Member_AddedRowFmt , StudentCount);
    }

    private async Task LoadDatasetAsync (StudentDatasetInfo dataset , CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = Resources.Member_Loading;

        try
        {
            var students = await _facade.LoadStudentDatasetAsync(dataset.Id , ct);
            if (students is not null)
            {
                CurrentDatasetId = dataset.Id;
                CurrentDatasetName = dataset.Name;
                Students = new ObservableCollection<Student>(students);
                StudentCount = Students.Count;
                IsEmpty = StudentCount == 0;
                FilePath = dataset.OriginalFileName ?? dataset.Name;
                MarkClean();
                NewStudent = new Student();
                StatusMessage = StudentCount > 0
                    ? string.Format(Resources.Member_LoadedFmt , StudentCount)
                    : Resources.Member_EmptyDataset;
            }
            else
            {
                StatusMessage = Resources.Member_DatasetNotFound;
                await _dialog.ShowErrorAsync(Resources.Data_LoadFailed , $"找不到数据集「{dataset.Name}」的文件。");
                await RefreshDatasetsAsync(ct);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Data_LoadFailed;
            await _dialog.ShowErrorAsync(Resources.Data_LoadFailed , ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedDatasetAsync (CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        var confirmed = await _dialog.ShowConfirmAsync(Resources.Data_DeleteConfirm ,
            string.Format(Resources.Member_DeleteConfirmMsg , SelectedDataset.Name));
        if (!confirmed) return;

        try
        {
            await _facade.DeleteStudentDatasetAsync(SelectedDataset.Id , ct);
            SelectedDataset = null;
            await RefreshDatasetsAsync(ct);
            StatusMessage = Resources.Member_Deleted;
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Member_DeleteFailed , ex.Message);
        }
    }

    [RelayCommand]
    private async Task RenameSelectedDatasetAsync (CancellationToken ct)
    {
        if (SelectedDataset is null) return;

        var (confirmed , newName) = await _dialog.ShowInputAsync(Resources.Member_RenameTitle ,
            string.Format(Resources.Member_RenamePrompt , SelectedDataset.Name) , SelectedDataset.Name);
        if (!confirmed || string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            await _facade.RenameStudentDatasetAsync(SelectedDataset.Id , newName.Trim() , ct);

            if (CurrentDatasetId == SelectedDataset.Id)
                CurrentDatasetName = newName.Trim();

            SelectedDataset = null;
            await RefreshDatasetsAsync(ct);
            StatusMessage = Resources.Member_Renamed;
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Member_RenameFailed , ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync (CancellationToken ct)
    {
        if (Students.Count == 0) return;

        var errors = ValidateStudents();
        if (errors.Count > 0)
        {
            await _dialog.ShowErrorAsync(Resources.Data_ValidationFailed ,
                string.Join('\n' , errors.Take(10)));
            return;
        }

        // 检查空行是否有未完成的数据
        if (IsNewStudentDirty)
        {
            var choice = await Dialog.ShowMultiOptionAsync(
                Resources.Member_NewRowPendingTitle ,
                Resources.Member_NewRowPendingMsg ,
                Resources.Member_DiscardAndSave ,
                Resources.Common_Cancel);
            if (choice != 0) return;
            NewStudent = new Student();
        }

        // 无关联数据集 → 另存为
        if (CurrentDatasetId is null)
        {
            await RenameSaveAsync(ct);
            return;
        }

        await SaveInternalAsync(ct);
    }

    /// <summary>直接保存到 CurrentDatasetId，无确认弹窗，不刷新侧栏选中状态。</summary>
    private async Task SaveInternalAsync (CancellationToken ct)
    {
        var datasetName = CurrentDatasetName ?? Resources.Member_Unnamed;

        try
        {
            if (CurrentDatasetId is not null)
                await _facade.DeleteStudentDatasetAsync(CurrentDatasetId , ct);

            CurrentDatasetId = await _facade.SaveStudentDatasetAsync(datasetName , [.. Students] , null , ct);
            CurrentDatasetName = datasetName;
            MarkClean();
            await RefreshDatasetsAsync(ct);
            StatusMessage = string.Format(Resources.Member_SavedFmt , datasetName);
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Data_SaveFailed , ex.Message);
        }
    }

    private List<string> ValidateStudents ()
    {
        var errors = new List<string>();

        for (int i = 0; i < Students.Count; i++)
        {
            var s = Students[i];
            var row = i + 1;

            // 跳过完全空行（所有字段均未填写）
            if (string.IsNullOrWhiteSpace(s.Name) && s.Height == null && s.Gender == null && !s.NeedsFrontRow)
                continue;

            if (string.IsNullOrWhiteSpace(s.Name))
                errors.Add(string.Format(Resources.Member_NameEmptyFmt , row));

            if (s.Height.HasValue && s.Height.Value <= 0)
                errors.Add(string.Format(Resources.Member_HeightInvalidFmt , row , s.Name));

            if (s.Gender.HasValue && !Enum.IsDefined(s.Gender.Value))
                errors.Add(string.Format(Resources.Member_GenderInvalidFmt , row , s.Name));
        }

        return errors;
    }



    [RelayCommand]
    private async Task RenameSaveAsync (CancellationToken ct)
    {
        var (confirmed , newName) = await _dialog.ShowInputAsync(Resources.Member_SaveAsTitle ,
            Resources.Member_SaveAsPrompt , "");
        if (!confirmed || string.IsNullOrWhiteSpace(newName)) return;

        try
        {
            var newId = await _facade.SaveStudentDatasetAsync(newName.Trim() , [.. Students] , null , ct);
            CurrentDatasetId = newId;
            CurrentDatasetName = newName.Trim();
            MarkClean();
            await RefreshDatasetsAsync(ct);
            StatusMessage = string.Format(Resources.Member_SavedAsFmt , newName.Trim());
        }
        catch (Exception ex)
        {
            await _dialog.ShowErrorAsync(Resources.Data_SaveFailed , ex.Message);
        }
    }
}
