using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>引导配置文件根对象。</summary>
public sealed class OnboardingConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "3.0";

    /// <summary>启动引导阶段列表（完整工作流）。</summary>
    [JsonPropertyName("startupPhases")]
    public List<OnboardingPhaseDefinition> StartupPhases { get; set; } = [];

    /// <summary>页面独立引导块（PageKey 名称 → 引导阶段定义）。首次访问时触发。</summary>
    [JsonPropertyName("pageGuides")]
    public Dictionary<string , OnboardingPhaseDefinition> PageGuides { get; set; } = [];

    /// <summary>
    /// 启动引导完成后导航到的目标页面（<see cref="PageKey"/> 枚举名称）。
    /// 默认 <c>"Home"</c>。设为 <c>null</c> 或空字符串等同于 <c>"Home"</c>。
    /// </summary>
    [JsonPropertyName("completeAction")]
    public string CompleteAction { get; set; } = "Home";
}

/// <summary>引导的一个阶段（对应一个页面）。</summary>
public sealed class OnboardingPhaseDefinition
{
    /// <summary>
    /// 此阶段要导航到的页面（<see cref="Services.PageKey"/> 枚举名称）。
    /// <c>null</c> 表示留在当前页。
    /// </summary>
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    /// <summary>跨阶段导航到此页面时是否注入演示数据。默认 false。</summary>
    [JsonPropertyName("seedData")]
    public bool SeedData { get; set; }

    [JsonPropertyName("steps")]
    public List<OnboardingStepDefinition> Steps { get; set; } = [];
}

/// <summary>引导的一个步骤（对应 Guide 控件的一个 GuideStepOption）。</summary>
public sealed class OnboardingStepDefinition
{
    /// <summary>指向 <c>Resources.resx</c> 中标题字符串的键名。</summary>
    [JsonPropertyName("titleKey")]
    public string TitleKey { get; set; } = "";

    /// <summary>指向 <c>Resources.resx</c> 中描述字符串的键名。</summary>
    [JsonPropertyName("descKey")]
    public string DescKey { get; set; } = "";

    /// <summary>
    /// 目标控件的 <c>x:Name</c>。支持分号分隔的候选项（取第一个找到的）。
    /// 空字符串表示居中模态，无高亮目标。
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    /// <summary>弹窗位置：Top / Bottom / Left / Right / Center。默认 Right。</summary>
    [JsonPropertyName("placement")]
    public string Placement { get; set; } = "Right";

    /// <summary>是否显示遮罩动画。居中纯文本步骤可设为 false。</summary>
    [JsonPropertyName("showMask")]
    public bool ShowMask { get; set; } = true;

    /// <summary>是否显示指向箭头。居中纯文本步骤可设为 false。</summary>
    [JsonPropertyName("showArrow")]
    public bool ShowArrow { get; set; } = true;
}
