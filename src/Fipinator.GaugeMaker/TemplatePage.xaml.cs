// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Fipinator.Core;
using Microsoft.Win32;

namespace Fipinator.GaugeMaker;

/// <summary>
/// "Change how a gauge looks" (owner, 2026-11-14: one place instead of "New gauge" + "Edit a gauge"): the user's
/// own gauges and the ready-made ones in one searchable list. An own gauge opens straight in the editor; a
/// ready-made one is copied to Documents first under a name the user picks, and the original stays as it is.
/// </summary>
public partial class TemplatePage : UserControl
{
    private sealed record Item(string Text, GaugeChoice Choice, string Search)
    {
        public bool Mine => Choice.Group == "My gauges";
    }

    private readonly List<Item> _items;

    public TemplatePage(MainWindow window)
    {
        InitializeComponent();
        // Own gauges first, then the ready-made ones by group.
        _items = [.. GaugeCatalog.All(GaugeFiles.TemplatesFolder, GaugeFiles.MyGaugesFolder)
            .OrderBy(c => c.Group == "My gauges" ? 0 : 1)
            .Select(c => new Item(c.Group == "My gauges" ? $"My gauges  ·  {c.Name}" : $"{c.Group}  ·  {c.Name}", c, $"{c.Group} {c.Name}"))];
        Filter();
        Search.TextChanged += (_, _) => Filter();

        List.SelectionChanged += (_, _) => ShowSelected();
        ShowSelected(); // the first gauge is already picked: show it straight away
        List.MouseDoubleClick += (_, _) => { if (List.SelectedItem is Item { Mine: true } i) window.OpenQuickColours(i.Choice.Path); };
        if (_items.Count == 0) Description.Text = "No gauges were found next to the program. Reinstalling FIPinator 3000 should bring them back.";

        Back.Click += (_, _) => window.ShowStart();
        OpenFile.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open a gauge",
                Filter = "Gauge files (*.json)|*.json|All files|*.*",
                InitialDirectory = Directory.Exists(GaugeFiles.MyGaugesFolder) ? GaugeFiles.MyGaugesFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog(window) == true) window.OpenEditor(dialog.FileName);
        };
        Create.Click += (_, _) => { if (OwnCopy() is { } path) window.OpenEditor(path); };
        Quick.Click += (_, _) => { if (OwnCopy() is { } path) window.OpenQuickColours(path); };
        NameBox.TextChanged += (_, _) => Error.Text = "";
    }

    /// <summary>The picked gauge as the user's own file: theirs already, or a new named copy of a ready-made one.</summary>
    private string? OwnCopy()
    {
        if (List.SelectedItem is not Item item) { Error.Text = "Pick a gauge first."; return null; }
        if (item.Mine) return item.Choice.Path;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Error.Text = "Give your copy a name."; return null; }
        try
        {
            var gauge = GaugeDefinition.Load(item.Choice.Path);
            var template = new GaugeFiles.Template(gauge.Name, gauge.Description ?? "", item.Choice.Path, gauge.License);
            return GaugeFiles.CreateFromTemplate(template, NameBox.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Error.Text = $"Couldn't save it: {ex.Message}";
            Log.Error($"Gauge Maker: create failed: {ex}");
            return null;
        }
    }

    private void Filter()
    {
        var words = Search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List.ItemsSource = _items.Where(i => words.All(w => i.Search.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        if (List.Items.Count > 0 && List.SelectedIndex < 0) List.SelectedIndex = 0;
    }

    private void ShowSelected()
    {
        Error.Text = "";
        if (List.SelectedItem is not Item item) { Preview.Source = null; Description.Text = ""; return; }
        // Own gauge: open it as it is. Ready-made: name the copy first.
        NameRow.Visibility = item.Mine ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = $"My {item.Choice.Name}";
        try
        {
            var gauge = GaugeDefinition.Load(item.Choice.Path);
            var about = gauge.Description ?? "";
            if (gauge.License is { Length: > 0 } license) about += $"\n\n{license}";
            Description.Text = item.Mine ? $"Your gauge. {about}" : about;
            using var renderer = GaugeRenderer.FromFile(item.Choice.Path);
            using var bitmap = renderer.Render();
            Preview.Source = GaugeImages.ToImage(bitmap);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Preview.Source = null;
            Description.Text = ex.Message;
        }
    }
}
