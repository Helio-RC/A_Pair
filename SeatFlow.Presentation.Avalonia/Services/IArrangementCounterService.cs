using System.Threading.Tasks;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>
/// 内存中的排座次数计数器，记录"生成座位安排"按钮的点击次数，
/// 并在离开座位安排页面时上报到后端 API。
/// 线程安全。
/// </summary>
public interface IArrangementCounterService
{
    /// <summary>
    /// 原子递增内存计数器。<br/>
    /// 在每次成功生成座位安排后调用。可从任意线程安全调用。
    /// </summary>
    void Increment();

    /// <summary>
    /// 原子读取并重置计数器，然后向后台 API 上报累积值。<br/>
    /// 上报为 fire-and-forget 模式，失败静默丢弃。<br/>
    /// 返回上报的累积值（0 表示没有需要上报的计数）。
    /// </summary>
    Task<int> ReportAndResetAsync();
}
