using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SeatFlow.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatFlow.Presentation.Avalonia.ViewModels;

/// <summary>
/// 参数编辑器 ViewModel。将 StrategyParameterDefinition 包装为可编辑的参数项列表。
/// </summary>
public partial class ParameterEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ObservableCollection<EditableParameter> Parameters { get; set; } = [];

    /// <summary>
    /// 从 manifest 的 ParameterDefinitions 加载，用已有值填充。
    /// </summary>
    public void LoadParameters (
        List<StrategyParameterDefinition>? definitions ,
        Dictionary<string , object?>? currentValues)
    {
        foreach (var p in Parameters)
            p.PropertyChanged -= OnParameterPropertyChanged;
        Parameters.Clear();
        if (definitions is null) return;

        foreach (var def in definitions)
        {
            var value = currentValues?.TryGetValue(def.Name , out var v) == true ? v : def.DefaultValue;
            var param = new EditableParameter(def , value);
            param.PropertyChanged += OnParameterPropertyChanged;
            Parameters.Add(param);
        }
    }

    private void OnParameterPropertyChanged (object? sender , System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableParameter.IsDirty))
            OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>
    /// 收集当前所有参数值到字典。
    /// </summary>
    public Dictionary<string , object?> CollectValues ()
    {
        var dict = new Dictionary<string , object?>();
        foreach (var p in Parameters)
            dict[p.Definition.Name] = p.Value;
        return dict;
    }

    /// <summary>
    /// 是否有未保存的修改。
    /// </summary>
    public bool IsDirty => Parameters.Any(p => p.IsDirty);
}

/// <summary>
/// 单个可编辑参数。
/// </summary>
public partial class EditableParameter (StrategyParameterDefinition definition , object? value) : ObservableObject
{
    public StrategyParameterDefinition Definition { get; } = definition;

    [ObservableProperty]
    public partial object? Value { get; set; } = value;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    private readonly object? _originalValue = value;

    partial void OnValueChanged (object? value)
    {
        IsDirty = !Equals(value , _originalValue);
    }

    // Convenience casts for XAML bindings
    public double NumberValue
    {
        get => Value is double d ? d
             : Value is int i ? i
             : Value is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number ? je.GetDouble()
             : 0;
        set => Value = value;
    }

    public string TextValue
    {
        get => Value is System.Text.Json.JsonElement je
                ? (je.ValueKind == System.Text.Json.JsonValueKind.String
                    ? je.GetString() ?? string.Empty
                    : je.ToString())   // 非字符串类型（bool/数字）退化到 ToString，避免 GetString() 抛异常
             : Value?.ToString() ?? string.Empty;
        set => Value = value;
    }

    public bool ToggleValue
    {
        get => Value switch
        {
            bool b => b,
            System.Text.Json.JsonElement je when je.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => je.GetBoolean(),
            _ => false
        };
        set => Value = value;
    }

    public string LocalizedLabel => Helpers.LocalizeHelper.Resolve(Definition.Label);

    // Visibility helpers for compiled bindings
    public bool IsNumberInput => Definition.FieldType == StrategyFieldType.NumberInput;
    public bool IsTextInput => Definition.FieldType == StrategyFieldType.TextInput;
    public bool IsToggleSwitch => Definition.FieldType == StrategyFieldType.ToggleSwitch;
    public bool IsDropdown => Definition.FieldType == StrategyFieldType.Dropdown;
}
