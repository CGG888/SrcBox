using System.Collections.Generic;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

public sealed record AddM3uDialogResult(string Name, string Url);
public sealed record EditM3uResult(bool IsDelete, string Name, string Url);

public sealed class MainWindowDialogActionsViewModel : ViewModelBase
{
    private const string PlaylistFilter = "Playlist Files (*.m3u;*.m3u8;*.txt)|*.m3u;*.m3u8;*.txt|All Files (*.*)|*.*";

    public string? PromptOpenFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = PlaylistFilter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public IReadOnlyList<string> PromptOpenFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = PlaylistFilter,
            Multiselect = true
        };
        return dlg.ShowDialog() == true ? dlg.FileNames : new List<string>();
    }

    public string? PromptOpenUrl(System.Windows.Window owner)
    {
        var dlg = new LibmpvIptvClient.AddM3uWindow
        {
            Owner = owner,
            Title = LibmpvIptvClient.Helpers.Localizer.S("Menu_OpenUrl", "打开链接")
        };
        dlg.HideNameField();
        if (dlg.ShowDialog() != true)
        {
            return null;
        }

        var url = dlg.SourceUrl.Trim();
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    public AddM3uDialogResult? PromptAddM3u(System.Windows.Window owner)
    {
        var dlg = new LibmpvIptvClient.AddM3uWindow { Owner = owner };
        if (dlg.ShowDialog() != true)
        {
            return null;
        }

        return new AddM3uDialogResult(dlg.SourceName, dlg.SourceUrl);
    }

    public EditM3uResult? PromptEditM3u(System.Windows.Window owner, string currentName, string currentUrl, bool topmost)
    {
        var dlg = new LibmpvIptvClient.EditM3uWindow(currentName, currentUrl) { Owner = owner };
        try { dlg.Topmost = topmost; } catch { }
        
        if (dlg.ShowDialog() == true)
        {
            return new EditM3uResult(dlg.IsDeleteRequested, dlg.SourceName, dlg.SourceUrl);
        }
        return null;
    }

    public LibmpvIptvClient.AboutWindow CreateAboutDialog(System.Windows.Window owner, bool topmost)
    {
        var dlg = new LibmpvIptvClient.AboutWindow { Owner = owner, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner };
        try { dlg.Topmost = topmost; } catch { }
        return dlg;
    }
}
