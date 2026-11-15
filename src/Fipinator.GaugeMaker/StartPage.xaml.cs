// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Fipinator.GaugeMaker;

public partial class StartPage : UserControl
{
    public StartPage(MainWindow window)
    {
        InitializeComponent();
        SetButton.Click += (_, _) => window.ShowAirspeed();
        CombineButton.Click += (_, _) => window.ShowCombine();
        PanelsButton.Click += (_, _) => window.ShowPanels();
        if (Fipinator.Core.SpadExport.SpadInstalled()) SpadButton.Visibility = System.Windows.Visibility.Visible;
        SpadButton.Click += (_, _) => SpadHelpWindow.Show(window);
        AiGuideButton.Click += (_, _) => OpenGuide(window, "Making gauges with AI.md", "docs/guides/making-gauges-with-ai.md");
        SkillButton.Click += (_, _) => OpenGuide(window, "Gauge-making guide.md", "docs/guides/gauge-making-guide.md");
        TweakButton.Click += (_, _) => window.ShowTemplates();
        LogsButton.Click += (_, _) => OpenLogs();
        CopyReportButton.Click += (_, _) => CopyReport();
        GitHubReportButton.Click += (_, _) => ReportOnGitHub();
    }

    /// <summary>Opens one of the AI files installed next to the program (the AI README or the gauge skill).</summary>
    private static void OpenGuide(Window owner, string file, string inRepo)
    {
        var guide = Path.Combine(AppContext.BaseDirectory, file);
        try
        {
            if (!File.Exists(guide)) throw new FileNotFoundException("The guide isn't installed.", guide);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{guide}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Fipinator.Core.Log.Warn($"Gauge Maker: AI guide not opened: {ex.Message}");
            MessageBox.Show(owner, $"That file couldn't be opened. It's also in the FIPinator project on GitHub: {inRepo}.", "FIPinator 3000");
        }
    }

    private static string Version => typeof(StartPage).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(Fipinator.Core.Log.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Fipinator.Core.Log.Folder, UseShellExecute = true, Verb = "open" })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            HelpStatus.Text = @"Couldn't open the log folder. It's at %LOCALAPPDATA%\FIPinator 3000\logs.";
            Fipinator.Core.Log.Warn($"Gauge Maker: log folder not opened: {ex.Message}");
        }
    }

    private void CopyReport()
    {
        try
        {
            Clipboard.SetText(Fipinator.Core.IssueReport.Simple("FIPinator 3000", Version));
            HelpStatus.Text = "Problem report copied. Paste it into a message (Ctrl+V). Your user name and FIP serial numbers are hidden.";
            Fipinator.Core.Log.Info("Gauge Maker: problem report copied.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            HelpStatus.Text = "Couldn't copy to the clipboard. Try again in a moment.";
        }
    }

    private void ReportOnGitHub()
    {
        string[] setup = [$"FIPinator 3000 {Version}, {DateTime.Now:yyyy-MM-dd HH:mm}", $"Windows: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}"];
        var url = Fipinator.Core.IssueReport.GitHubIssueUrl("FIPinator 3000", setup, Fipinator.Core.IssueReport.GaugeMakerPage);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            HelpStatus.Text = "GitHub's New issue page is opening in your browser, already filled in. Add what happened, then submit it there.";
            Fipinator.Core.Log.Info("Gauge Maker: GitHub issue page opened.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            HelpStatus.Text = "Couldn't open the browser. The project page is " + Fipinator.Core.IssueReport.GaugeMakerPage;
            Fipinator.Core.Log.Warn($"Gauge Maker: GitHub page not opened: {ex.Message}");
        }
    }
}
