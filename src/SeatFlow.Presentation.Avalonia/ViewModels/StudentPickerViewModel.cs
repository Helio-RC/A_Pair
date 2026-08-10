using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 学生选择器 ViewModel。绑定到一个学生列表和一个选中的学生 ID。
/// 支持通过 <see cref="SetExcludedIds"/> 设置排除列表，自动过滤下拉选项。
/// </summary>
public partial class StudentPickerViewModel : ViewModelBase
{
    /// <summary>完整学生列表（未过滤）。</summary>
    private List<StudentPickerItem> _allStudents = [];

    /// <summary>当前排除的学生 ID 集合。</summary>
    private HashSet<string> _excludedStudentIds = [];

    [ObservableProperty]
    public partial ObservableCollection<StudentPickerItem> Students { get; set; } = [];

    [ObservableProperty]
    public partial StudentPickerItem? SelectedStudent { get; set; }

    partial void OnSelectedStudentChanged (StudentPickerItem? value)
    {
        SelectedStudentId = value?.Id;
    }

    [ObservableProperty]
    public partial string? SelectedStudentId { get; set; }

    /// <summary>
    /// 从 Core.Models.Student 列表加载学生项。缓存完整列表并刷新过滤视图。
    /// </summary>
    public void LoadStudents (IEnumerable<SeatFlow.Core.Models.Student> students)
    {
        _allStudents = [.. students.Select(s => new StudentPickerItem { Id = s.Id , Name = s.Name })];
        RefreshFilteredStudents();
    }

    /// <summary>
    /// 设置排除的学生 ID 集合。排除的 ID 不会出现在下拉列表中（但当前选中项始终保留）。
    /// </summary>
    /// <param name="excludedIds">要排除的学生 ID 集合。传入空集合或 null 则不排除任何学生。</param>
    public void SetExcludedIds (HashSet<string> excludedIds)
    {
        _excludedStudentIds = excludedIds ?? [];
        // 保留当前选择（可能在保存前切换数据集时丢失）
        var currentSelectionId = SelectedStudent?.Id;
        RefreshFilteredStudents();
        // 如果当前选中的学生仍在过滤后的列表中，保持选中
        if (currentSelectionId is not null)
            SelectById(currentSelectionId);
    }

    /// <summary>
    /// 按 ID 设置选中项。
    /// </summary>
    public void SelectById (string? studentId)
    {
        if (studentId is null)
        {
            SelectedStudent = null;
            return;
        }
        SelectedStudent = Students.FirstOrDefault(s => s.Id == studentId);
    }

    /// <summary>
    /// 刷新过滤后的学生列表：从 <see cref="_allStudents"/> 中排除 <see cref="_excludedStudentIds"/>，
    /// 但保留当前选中的学生（确保选中项始终在自己的下拉列表中可见）。
    /// </summary>
    private void RefreshFilteredStudents ()
    {
        var selectedId = SelectedStudent?.Id;
        var filtered = _allStudents
            .Where(s => !_excludedStudentIds.Contains(s.Id) || s.Id == selectedId)
            .ToList();
        Students = new ObservableCollection<StudentPickerItem>(filtered);
    }
}

/// <summary>
/// 学生选择器的下拉项。
/// </summary>
public class StudentPickerItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Display => string.IsNullOrEmpty(Name) ? Id : Name;
}
