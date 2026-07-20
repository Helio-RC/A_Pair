using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SeatFlow.Presentation.Avalonia.Views;

public partial class HomeView : UserControl
{
    public HomeView ()
    {
        InitializeComponent();
    }

    public static readonly IValueConverter ContrastForeground = new FuncValueConverter<IBrush?, IBrush?>(
        brush =>
        {
            if (brush is ISolidColorBrush solid)
            {
                var c = solid.Color;
                // WCAG relative luminance
                var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return lum > 0.55 ? Brushes.Black : Brushes.White;
            }
            return Brushes.White;
        });
}
