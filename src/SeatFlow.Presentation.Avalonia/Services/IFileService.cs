using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SeatFlow.Presentation.Avalonia.Services;

public interface IFileService
{
    void SetTopLevel (TopLevel topLevel);
    Task<IStorageFile?> OpenFileAsync (string title , IReadOnlyList<FilePickerFileType> types);
    Task<IStorageFile?> SaveFileAsync (string title , IReadOnlyList<FilePickerFileType> types , string? suggestedFileName = null);
}
