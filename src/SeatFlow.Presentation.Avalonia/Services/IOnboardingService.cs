using System.Threading.Tasks;
using CodeWF.AvaloniaControls.Controls;

namespace SeatFlow.Presentation.Avalonia.Services;

/// <summary>引导服务——纯机械式桥接 JSON 配置与 Guide 控件，零业务逻辑推断。</summary>
public interface IOnboardingService
{
    /// <summary>引导是否当前活跃。</summary>
    bool IsActive { get; }

    /// <summary>启动（或重新启动）启动引导。</summary>
    void StartOnboarding ();

    /// <summary>
    /// 检查并触发页面的独立引导块（首次访问时）。
    /// 启动引导进行中或该页面已展示过则跳过。
    /// </summary>
    /// <returns>true 表示触发了页面引导。</returns>
    bool TryShowPageGuide (PageKey page);

    /// <summary>标记页面引导已完成并持久化。</summary>
    Task MarkPageGuideShownAsync (PageKey page);

    /// <summary>
    /// 在 Guide 控件显示步骤前调用。解析 Target 控件并处理跨阶段页面导航。
    /// </summary>
    /// <param name="stepIndex">全量平铺步骤列表中的索引。</param>
    /// <param name="step">可修改的步骤选项（设置 Target 等）。</param>
    void HandleStepOpening (int stepIndex , IGuideStepOption step);

    /// <summary>用户点击最后一步的"完成"按钮。</summary>
    void HandleGuideCompleted ();

    /// <summary>用户点击 × 或按 Esc。返回 false 表示用户取消关闭（应重新显示 Guide）。</summary>
    Task<bool> HandleGuideClosedAsync ();

    /// <summary>
    /// 窗口失活时调用（最小化、Alt+Tab 切走）。
    /// 静默隐藏 Guide Popup 遮罩和卡片，不结束引导、不弹出确认对话框。
    /// </summary>
    void HandleWindowDeactivated ();

    /// <summary>
    /// 窗口激活时调用（从最小化恢复、Alt+Tab 切回）。
    /// 若引导被 HandleWindowDeactivated 隐藏，则从同一步骤恢复显示。
    /// </summary>
    void HandleWindowActivated ();
}
