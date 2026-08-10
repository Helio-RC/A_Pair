namespace SeatFlow.Core.Models;

/// <summary>
/// 策略级全局参数的声明式定义。来自 manifest JSON 的 parameters[]。
/// 每个参数在 UI 中渲染为对应的输入控件。
/// </summary>
public sealed class StrategyParameterDefinition
{
    /// <summary>参数名，存入 StrategyConfig.Parameters 的 key。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>输入控件类型。</summary>
    public StrategyFieldType FieldType { get; init; }

    /// <summary>多语言标签。key 为语言代码（zh-CN / en-US 等）。</summary>
    public Dictionary<string , string> Label { get; init; } = [];

    /// <summary>默认值。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>NumberInput 的最小值（可选）。</summary>
    public double? MinValue { get; init; }

    /// <summary>NumberInput 的最大值（可选）。</summary>
    public double? MaxValue { get; init; }

    /// <summary>下拉框数据源："dataset:{field}" 或 "inline"。</summary>
    public string? DropdownSource { get; init; }

    /// <summary>内联下拉选项值列表。</summary>
    public List<string>? DropdownValues { get; init; }
}

/// <summary>
/// 按数据集/会场的配置块声明。来自 manifest JSON 的 codeBlocks[]。
/// 每个 codeBlock 在 UI 中渲染为一个独立的可编辑配置区。
/// </summary>
public sealed class StrategyCodeBlock
{
    /// <summary>块标题的多语言文本。</summary>
    public Dictionary<string , string> Title { get; init; } = [];

    /// <summary>块描述的多语言文本（可选）。</summary>
    public Dictionary<string , string>? Description { get; init; }

    /// <summary>引用的数据类型。</summary>
    public StrategyDataType DataType { get; init; }

    /// <summary>UI 显示模式。</summary>
    public StrategyDisplayMode DisplayMode { get; init; }

    /// <summary>是否显示学生选择器。默认根据 DataType 自动判断。</summary>
    public bool? ShowStudentPicker { get; init; }

    /// <summary>是否显示座位定位选择器（行列/环角度/XY）。默认 true，自动匹配策略可设为 false。</summary>
    public bool ShowSeatPosition { get; init; } = true;

    /// <summary>是否显示会場选择器。默认根据 DataType 自动判断。</summary>
    public bool? ShowVenuePicker { get; init; }

    /// <summary>是否显示性别选择器（默认 false）。声明开启后每行渲染一个 Male/Female ComboBox，值存入 CustomValues["Gender"]。</summary>
    public bool ShowGenderPicker { get; init; }

    /// <summary>每行学生选择器数量，默认 1。</summary>
    public int StudentPickerCount { get; init; } = 1;

    /// <summary>学生选择器数量是否从会场的 SeatsPerDesk 动态获取。DeskMate 设为 true。</summary>
    public bool SeatsPerDeskFromVenue { get; init; }

    /// <summary>
    /// dataType:Both 时配置加载的触发方式。
    /// Both=两个选择器都需有值后精确匹配加载（默认），Any=任一选择器有值即模糊匹配加载。
    /// </summary>
    public StrategyLoadTrigger LoadTrigger { get; init; } = StrategyLoadTrigger.Both;

    /// <summary>
    /// 是否禁止同一行内学生选择器值重复（同对防重复）。
    /// 设为 true 时，同一行的多个 StudentPicker 下拉列表互相排除已选学生，
    /// 确保一桌多人不能是同一个人。DeskMate 策略应设为 true。
    /// </summary>
    public bool PreventDuplicateInRow { get; init; }

    /// <summary>
    /// 是否禁止跨行学生选择器值重复（全局防重复）。
    /// 设为 true 时，该配置块所有行的所有 StudentPicker 下拉列表互相排除已选学生，
    /// 确保一个学生只能出现在一个配置行中。FixedSeat 策略应设为 true。
    /// </summary>
    public bool PreventDuplicateAcrossRows { get; init; }

    /// <summary>配置行的字段定义列表。</summary></field_definition></field_definition>
    public List<StrategyFieldDefinition> Fields { get; init; } = [];
}

/// <summary>
/// 配置行内单个字段的声明式定义。
/// </summary>
public sealed class StrategyFieldDefinition
{
    /// <summary>字段名，存入 StrategyConfigRow.Values 的 key。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>字段的输入控件类型。</summary>
    public StrategyFieldType FieldType { get; init; }

    /// <summary>多语言标签（模板文字）。</summary>
    public Dictionary<string , string> Label { get; init; } = [];

    /// <summary>下拉框数据源（仅 FieldType=Dropdown 时有效）。</summary>
    public string? DropdownSource { get; init; }

    /// <summary>内联下拉选项值列表。</summary>
    public List<string>? DropdownValues { get; init; }
}

/// <summary>
/// 字段控件类型枚举。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum StrategyFieldType
{
    NumberInput,
    TextInput,
    ToggleSwitch,
    Dropdown,
    StudentPicker,
    SeatPosition
}

/// <summary>
/// codeBlock 引用的数据类型。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum StrategyDataType
{
    Student,
    Venue,
    Both
}

/// <summary>
/// codeBlock 的 UI 显示模式。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum StrategyDisplayMode
{
    Table,
    ValuePair
}

/// <summary>
/// 控制 dataType:Both 时配置块的加载触发方式。
/// Both=两个选择器都需有值后才加载（精确匹配），Any=任一选择器有值即加载（模糊匹配）。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum StrategyLoadTrigger
{
    /// <summary>两个选择器都需有值才加载，ID 精确匹配。</summary>
    Both,
    /// <summary>任一选择器有值即加载，未选定的作为通配符。</summary>
    Any
}
