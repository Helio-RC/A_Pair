using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SeatFlow.Presentation.Avalonia.Services;

public class FileService : IFileService
{
    private TopLevel? _topLevel;
    private int _dialogLock;

    public void SetTopLevel (TopLevel topLevel) => _topLevel = topLevel;

    public async Task<IStorageFile?> OpenFileAsync (string title , IReadOnlyList<FilePickerFileType> types)
    {
        if (_topLevel is null) return null;
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return null;
        try
        {
            var files = await Dispatcher.UIThread.InvokeAsync(() =>
                _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title ,
                    FileTypeFilter = types ,
                    AllowMultiple = false
                }));
            return files.Count > 0 ? files[0] : null;
        }
        finally
        {
            await Task.Delay(150);
            Interlocked.Exchange(ref _dialogLock , 0);
        }
    }

    public async Task<IStorageFile?> SaveFileAsync (string title , IReadOnlyList<FilePickerFileType> types , string? suggestedFileName = null)
    {
        if (_topLevel is null) return null;
        if (Interlocked.CompareExchange(ref _dialogLock , 1 , 0) != 0) return null;
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title ,
                    FileTypeChoices = types ,
                    SuggestedFileName = suggestedFileName
                }));
        }
        finally
        {
            await Task.Delay(150);
            Interlocked.Exchange(ref _dialogLock , 0);
        }
    }
}
