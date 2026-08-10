using System.Threading.Tasks;
using SeatFlow.Presentation.Avalonia.Lang;
using SeatFlow.Presentation.Avalonia.Views;
using Avalonia.Controls;

namespace SeatFlow.Presentation.Avalonia.Services;

public class DialogService : IDialogService
{
    private TopLevel? _topLevel;

    public void SetTopLevel (TopLevel topLevel) => _topLevel = topLevel;

    public Task ShowErrorAsync (string title , string message)
        => ShowAsync(title , message , DialogKind.Error);

    public Task ShowWarningAsync (string title , string message)
        => ShowAsync(title , message , DialogKind.Warning);

    public Task ShowInfoAsync (string title , string message)
        => ShowAsync(title , message , DialogKind.Info);

    public async Task<bool> ShowConfirmAsync (string title , string message)
    {
        if (_topLevel is not Window window)
        {
            System.Diagnostics.Debug.WriteLine("DialogService: ShowConfirmAsync called before SetTopLevel");
            return false;
        }

        var dialog = new DialogWindow
        {
            Title = title ,
            Message = message ,
            Kind = DialogKind.Confirm
        };

        return await dialog.ShowDialog<bool>(window) == true;
    }

    public async Task<int?> ShowMultiOptionAsync (string title , string message ,
        string primaryText , string secondaryText , string? cancelText = null)
    {
        if (_topLevel is not Window window)
        {
            System.Diagnostics.Debug.WriteLine("DialogService: ShowMultiOptionAsync called before SetTopLevel");
            return null;
        }

        var dialog = new DialogWindow
        {
            Title = title ,
            Message = message ,
            Kind = DialogKind.MultiOption ,
            Button1Text = primaryText ,
            Button2Text = secondaryText ,
            Button3Text = cancelText ?? Resources.Common_Cancel
        };

        await dialog.ShowDialog<bool>(window);
        return dialog.DialogResult;
    }

    public async Task<(bool Confirmed , string Input)> ShowInputAsync (string title , string prompt , string initialValue = "")
    {
        if (_topLevel is not Window window)
        {
            System.Diagnostics.Debug.WriteLine("DialogService: ShowInputAsync called before SetTopLevel");
            return (false , "");
        }

        var dialog = new InputWindow
        {
            Title = title ,
            Prompt = prompt ,
            Input = initialValue
        };

        var result = await dialog.ShowDialog<bool>(window);
        return (result , result ? dialog.Input : "");
    }

    private async Task ShowAsync (string title , string message , DialogKind kind)
    {
        if (_topLevel is not Window window)
        {
            System.Diagnostics.Debug.WriteLine("DialogService: ShowAsync called before SetTopLevel");
            return;
        }

        var dialog = new DialogWindow
        {
            Title = title ,
            Message = message ,
            Kind = kind
        };

        await dialog.ShowDialog<bool>(window);
    }
}
