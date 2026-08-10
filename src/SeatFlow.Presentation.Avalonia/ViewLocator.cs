using System;
using System.Diagnostics.CodeAnalysis;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SeatFlow.Presentation.Avalonia
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away." ,
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        public Control? Build (object? param)
        {
            if (param is null)
                return null;

            var fullName = param.GetType().FullName;
            if (fullName is null) return null;

            var name = fullName.Replace("ViewModel" , "View" , StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null && Activator.CreateInstance(type) is Control control)
            {
                control.DataContext = param;
                return control;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match (object? data)
        {
            return data is ViewModelBase;
        }
    }
}
