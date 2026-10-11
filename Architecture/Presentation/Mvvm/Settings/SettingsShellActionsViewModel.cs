using System.Diagnostics;
using System.Windows;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;

public sealed class SettingsShellActionsViewModel : ViewModelBase
{
    public string DocsUrl => "https://srcbox.top/guide/catchup-timeshift.html";
    public string IssuesUrl => "https://github.com/CGG888/SrcBox/issues";

    public void OpenDocs()
    {
        OpenUrlInternal(DocsUrl);
    }

    public void OpenIssues()
    {
        OpenUrlInternal(IssuesUrl);
    }

    public void OpenUrl(string url)
    {
        OpenUrlInternal(url);
    }

    public void RaiseDebug(Action? debugRequested)
    {
        try { debugRequested?.Invoke(); } catch { }
    }

    private static void OpenUrlInternal(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
