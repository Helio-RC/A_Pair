using System;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SeatFlow.Presentation.Avalonia.Behaviors;

public static class CanvasZoomPan
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , bool>("Enabled" ,
            typeof(CanvasZoomPan) , defaultValue: false);

    private static readonly AttachedProperty<TranslateTransform> TranslateProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , TranslateTransform>("Translate" , typeof(CanvasZoomPan));
    private static readonly AttachedProperty<bool> IsPanningProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , bool>("IsPanning" , typeof(CanvasZoomPan));
    private static readonly AttachedProperty<Point> PanOriginProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , Point>("PanOrigin" , typeof(CanvasZoomPan));
    private static readonly AttachedProperty<Point> OriginalTranslateProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer , Point>("OriginalTranslate" , typeof(CanvasZoomPan));

    public static void SetEnabled (ScrollViewer element , bool value)
    {
        bool old = element.GetValue(EnabledProperty);
        if (old == value) return;
        element.SetValue(EnabledProperty , value);
        if (value)
        {
            element.PointerPressed += OnPointerPressed;
            element.PointerMoved += OnPointerMoved;
            element.PointerReleased += OnPointerReleased;
        }
        else
        {
            element.PointerPressed -= OnPointerPressed;
            element.PointerMoved -= OnPointerMoved;
            element.PointerReleased -= OnPointerReleased;
        }
    }

    private static void EnsureTransform (ScrollViewer sv)
    {
        if (sv.GetValue(TranslateProperty) != null) return;
        if (sv.Content is not Visual content) return;
        var translate = new TranslateTransform(0 , 0);
        content.RenderTransform = translate;
        sv.SetValue(TranslateProperty , translate);
    }

    private static void OnPointerPressed (object? sender , PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        // 若按下的是座位元素，设 NaN 哨兵后跳过，让拖放逻辑接管
        if (e.Source is StyledElement src && src.DataContext is SeatDisplayItem)
        {
            sv.SetValue(PanOriginProperty , new Point(double.NaN , double.NaN));
            return;
        }
        if (!e.GetCurrentPoint(sv).Properties.IsLeftButtonPressed) return;
        EnsureTransform(sv);
        sv.SetValue(PanOriginProperty , e.GetPosition(sv));
        sv.SetValue(OriginalTranslateProperty ,
            new Point(sv.GetValue(TranslateProperty)!.X , sv.GetValue(TranslateProperty)!.Y));
    }

    private static void OnPointerMoved (object? sender , PointerEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (!e.GetCurrentPoint(sv).Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(sv);
        var origin = sv.GetValue(PanOriginProperty);
        if (double.IsNaN(origin.X)) return; // 已由座位拖放接管，跳过平移
        if (!sv.GetValue(IsPanningProperty))
        {
            if (Math.Sqrt(((pos.X - origin.X) * (pos.X - origin.X)) + ((pos.Y - origin.Y) * (pos.Y - origin.Y))) < 4) return;
            sv.SetValue(IsPanningProperty , true);
            e.Handled = true;
        }
        var translate = sv.GetValue(TranslateProperty);
        if (translate == null) return;
        var origTrans = sv.GetValue(OriginalTranslateProperty);
        translate.X = origTrans.X + (pos.X - origin.X);
        translate.Y = origTrans.Y + (pos.Y - origin.Y);
    }

    private static void OnPointerReleased (object? sender , PointerReleasedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.SetValue(IsPanningProperty , false);
    }
}
