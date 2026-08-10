namespace SeatFlow.Contracts.Models;

/// <summary>
/// 插件视角的座位视图。
/// 固定座位的唯一修改通道是 <see cref="IPluginWorkspace.TryMarkFixed"/>
/// （需要 manifest <c>capabilities</c> 声明 <c>"MarkFixedSeat"</c> 能力），
/// 避免插件绕过能力校验直接改变座位固定状态。
/// </summary>
public interface IPluginSeat
{
    string Id { get; }
    bool IsAvailable { get; }
    bool IsFixed { get; }
    string? OccupantId { get; }
}
