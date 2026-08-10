namespace SeatFlow.Core.Strategies;

/// <summary>
/// 策略能力标识常量。Manifest 中声明的 <c>capabilities</c> 字段与此处常量一一对应。
/// 策略必须先在 manifest 中声明能力，运行时方可调用对应的能力接口方法。
/// 日后新增能力时在此添加 const + 对应接口即可。
/// </summary>
/// <remarks>
/// <b>使用流程：</b>
/// <list type="number">
/// <item>在 manifest.json 的 <c>capabilities</c> 数组中声明能力</item>
/// <item>ApplicationFacade 在管道执行前从 manifest 读取并注册到 workspace</item>
/// <item>策略通过对应接口调用能力方法（workspace 校验声明状态）</item>
/// </list>
/// </remarks>
public static class Capability
{
    /// <summary>
    /// 标注固定座位能力。允许策略通过 <see cref="IFixedSeatCapability.TryMarkFixed"/>
    /// 将座位标记为 <c>IsFixed=true</c> 并（可选）分配学生。调用时记录操作日志。
    /// </summary>
    public const string MarkFixedSeat = "MarkFixedSeat";

    // 未来拓展示例：
    // public const string SwapSeats = "SwapSeats";
}

/// <summary>
/// 固定座位能力接口。策略须在 manifest <c>capabilities</c> 中声明
/// <see cref="Capability.MarkFixedSeat"/> 后方可调用。
/// 由 <see cref="Workspace.SeatingWorkspace"/> 显式实现，
/// 通过 <see cref="Contracts.Models.IPluginWorkspace"/> 暴露给插件。
/// </summary>
/// <remarks>
/// <b>安全模型：</b>能力检查仅在 <c>TryMarkFixedImpl</c> 入口进行。
/// 内置策略被视为受信任，可以直接操作 <c>Seat.IsFixed</c>；
/// 插件策略必须通过本接口的声明-校验机制来修改座位固定状态。
/// </remarks>
public interface IFixedSeatCapability
{
    /// <summary>
    /// 尝试将指定座位标记为固定并（可选）分配给指定学生。
    /// 需要调用策略已在 manifest capabilities 中声明 <see cref="Capability.MarkFixedSeat"/>。
    /// </summary>
    /// <param name="seatId">座位 ID。</param>
    /// <param name="studentId">
    /// 学生 ID。为 null 时仅标记座位固定不分配学生；非 null 时先调用 TryAssignSeat 分配再标记固定。
    /// </param>
    /// <param name="strategyId">调用策略的 <c>Id</c>（用于能力校验和日志）。</param>
    /// <param name="displayName">策略展示名称（用于日志）。</param>
    /// <param name="error">失败原因。未声明能力时返回 "策略 {displayName} 未声明 MarkFixedSeat 能力"。</param>
    /// <returns>是否成功。未声明能力时返回 false 并自动记录 LogWarning。</returns>
    bool TryMarkFixed (string seatId , string? studentId , string strategyId , string displayName , out string error);
}
