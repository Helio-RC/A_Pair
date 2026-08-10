using System.Text.Json.Serialization;
using SeatFlow.Contracts.Models;
using SeatFlow.Core.Enums;
using SeatFlow.Core.Utilities;

namespace SeatFlow.Core.Models;

/// <summary>
/// 表示一名学生，包含排座所需的基本信息与轮换辅助数据。
/// </summary>
public class Student : IPluginStudent
{
    /// <summary>
    /// 学生唯一标识符，默认自动生成 GUID。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 学生姓名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 身高（厘米），可选字段，可用于身高优先排座策略。
    /// </summary>
    public float? Height { get; set; }

    /// <summary>
    /// 性别，可选字段。
    /// </summary>
    public Gender? Gender { get; set; }

    /// <summary>
    /// 是否需要前排座位（如视力不佳等特殊需求）。
    /// </summary>
    public bool NeedsFrontRow { get; set; }

    /// <summary>
    /// 最近 N 次座位历史记录（环形缓冲区），用于轮换算法避免重复。
    /// 默认容量为 3，记录最近坐过的座位 ID。
    /// </summary>
    [JsonIgnore]
    public CircularHistory<string> RecentSeatHistory { get; set; } = new(10);

    /// <summary>
    /// 前排偏好分数，由轮换策略动态调整，分数越高越优先分配到前排。
    /// </summary>
    public int FrontRowPreferenceScore { get; set; }

    /// <summary>
    /// 扩展属性挂载点，供插件或自定义逻辑附加额外数据。
    /// </summary>
    [JsonIgnore]
    public AttributeBag Extensions { get; set; } = new();
}
