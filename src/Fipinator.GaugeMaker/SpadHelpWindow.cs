// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

/// <summary>
/// "Using SPAD.neXt?" (owner, 2026-11-14: short, and pointing to the SPAD gauges download and the guide): where the
/// gauges saved for SPAD.neXt are, a button to the SPAD gauges download (what's ported and tested) and one to
/// "Using SPAD.neXt and FIPinator Live together". Only explains: it doesn't save, export or change anything.
/// </summary>
internal static class SpadHelpWindow
{
    public static void Show(Window owner)
    {
        var folder = SpadExport.GaugesFolder;
        var window = new Window
        {
            Title = "Using SPAD.neXt", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24), MaxWidth = 580 };
        TextBlock Text(string text, double top = 10, bool bold = false, double size = 14) => new()
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };

        panel.Children.Add(Text("Using SPAD.neXt?", 0, bold: true, size: 20));
        panel.Children.Add(Text("FIPinator gauges also come as SPAD.neXt gauges: a port of the same gauges, less tested than FIPinator Live (some need work). The SPAD.neXt gauges download says what's been ported, what's tested and what's Live only."));
        panel.Children.Add(Text("Many people use SPAD.neXt for their yoke, throttle and other gear and FIPinator Live for the FIPs. The guide shows how."));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var repo = new Button { Content = "See the SPAD.neXt gauges (GitHub)", Margin = new Thickness(0, 0, 8, 0) };
        repo.Click += (_, _) => Open(SpadExport.GaugesRepo);
        var guide = new Button { Content = "How to use SPAD.neXt and Live together" };
        guide.Click += (_, _) =>
        {
            // The copy installed with FIPinator 3000, or the project page.
            var local = Path.Combine(AppContext.BaseDirectory, "Using SPAD.neXt and FIPinator Live.md");
            if (File.Exists(local)) OpenFile(local); else Open(SpadExport.TogetherGuide);
        };
        links.Children.Add(repo);
        links.Children.Add(guide);
        panel.Children.Add(links);

        panel.Children.Add(Text("Gauges you save here", 22, bold: true, size: 16));
        panel.Children.Add(Text("When SPAD.neXt is on this PC, every gauge you save in FIPinator 3000 is also saved for SPAD.neXt, in this folder (add it once in SPAD.neXt's FIP gauge list with \"Add location\"):", 4));
        panel.Children.Add(new TextBox { Text = folder, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 0) });
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copy = new Button { Content = "Copy folder path", Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(folder); copy.Content = "Copied"; }
            catch (System.Runtime.InteropServices.COMException) { copy.Content = "Couldn't copy: try again"; }
        };
        var open = new Button { Content = "Open folder" };
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" })?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Log.Warn($"Couldn't open {folder}: {ex.Message}");
            }
        };
        folderButtons.Children.Add(copy);
        folderButtons.Children.Add(open);
        panel.Children.Add(folderButtons);

        var ok = new Button
        {
            Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0), Style = (Style)Application.Current.FindResource("AccentButtonStyle"),
        };
        ok.Click += (_, _) => window.Close();
        panel.Children.Add(ok);
        window.Content = panel;
        window.ShowDialog();
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { Log.Warn($"Couldn't open {url}: {ex.Message}"); }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { Log.Warn($"Couldn't open {path}: {ex.Message}"); }
    }
}
