using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SeatFlow.Presentation.Avalonia.Views;

internal partial class InputWindow : Window
{
    public string Prompt
    {
        get => PromptBlock.Text ?? "";
        set => PromptBlock.Text = value;
    }

    public string Input
    {
        get => InputBox.Text ?? "";
        set => InputBox.Text = value;
    }

    public InputWindow ()
    {
        InitializeComponent();

        OkButton.Click += (_ , _) => Close(true);
        CancelButton.Click += (_ , _) => Close(false);
    }

    protected override void OnLoaded (RoutedEventArgs e)
    {
        base.OnLoaded(e);
        InputBox.Focus();
        InputBox.SelectAll();
    }
}
