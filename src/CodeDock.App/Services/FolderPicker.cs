using Microsoft.Win32;

namespace CodeDock.App.Services;

public sealed class FolderPicker : IFolderPicker
{
    public string? PickProjectFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a project to CodeDock",
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
