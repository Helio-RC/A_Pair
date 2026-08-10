using System.Threading.Tasks;
using Avalonia.Controls;

namespace SeatFlow.Presentation.Avalonia.Services;

public interface IDialogService
{
    void SetTopLevel (TopLevel topLevel);
    Task ShowErrorAsync (string title , string message);
    Task ShowWarningAsync (string title , string message);
    Task ShowInfoAsync (string title , string message);
    Task<bool> ShowConfirmAsync (string title , string message);

    /// <summary>显示文本输入对话框，返回 (是否确认, 输入文本)。</summary>
    Task<(bool Confirmed , string Input)> ShowInputAsync (string title , string prompt , string initialValue = "");

    /// <summary>显示多按钮对话框，返回 null(Windw关闭) / 0(第一个按钮) / 1(第二个按钮) / 2(第三个按钮)。</summary>
    Task<int?> ShowMultiOptionAsync (string title , string message ,
        string primaryText , string secondaryText , string? cancelText = null);
}
