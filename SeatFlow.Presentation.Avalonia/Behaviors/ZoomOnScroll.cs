using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

public static class ZoomOnScroll
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , bool>("Enabled" , typeof(ZoomOnScroll) , defaultValue: false);

    public static readonly AttachedProperty<Action<double>?> OnZoomProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , Action<double>?>("OnZoom" , typeof(ZoomOnScroll));

    public static void SetEnabled (ScrollViewer el , bool v) { el.SetValue(EnabledProperty , v); }
    public static void SetOnZoom (ScrollViewer el , Action<double>? v) { el.SetValue(OnZoomProperty , v); }

    static ZoomOnScroll ()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnEnabledChanged);
    }

    private static void OnEnabledChanged (ScrollViewer sv , AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) sv.PointerWheelChanged += OnWheel;
        else sv.PointerWheelChanged -= OnWheel;
    }

    private static void OnWheel (object? sender , PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        // 仅在按住 Ctrl 时缩放，否则让 ScrollViewer 正常滚动
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        e.Handled = true;
        double delta = e.Delta.Y > 0 ? 0.1 : -0.1;
        sv.GetValue(OnZoomProperty)?.Invoke(delta);
    }
}
