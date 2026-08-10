namespace SeatFlow.Contracts.Models;

/// <summary>
/// 插件视角的学生只读视图。
/// </summary>
public interface IPluginStudent
{
    string Id { get; }
    string Name { get; }
    float? Height { get; }
    bool NeedsFrontRow { get; }
    int FrontRowPreferenceScore { get; }
}
