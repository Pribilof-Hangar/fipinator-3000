// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

/// <summary>
/// "Combine gauges": tick gauges (built-in templates and your own), choose "more needles on one
/// dial" or "side by side", see it, save it as your own gauge. Uses <see cref="GaugeCombiner"/>.
/// </summary>
public partial class CombinePage : UserControl, IDisposable
{
    private sealed record Choice(string Name, string Path, string Where);

    private readonly MainWindow _window;
    private readonly List<Choice> _all;
    private readonly List<Choice> _ticked = [];
    private string? _json;
    private GaugeRenderer? _renderer;

    public CombinePage(MainWindow window)
    {
        InitializeComponent();
        _window = window;
        _all = [.. GaugeFiles.Templates().Select(t => new Choice(t.Name, t.Path, "template")).Concat(MyGauges())];

        Back.Click += (_, _) => window.ShowStart();
        Save.Click += (_, _) => SaveGauge();
        Filter.TextChanged += (_, _) => Fill();
        Needles.Checked += (_, _) => Build();
        Side.Checked += (_, _) => Build();
        Fill();
        Build();
    }

    private static IEnumerable<Choice> MyGauges()
    {
        if (!Directory.Exists(GaugeFiles.MyGaugesFolder)) yield break;
        foreach (var file in Directory.GetFiles(GaugeFiles.MyGaugesFolder, "*.json"))
        {
            string? name = null;
            try { name = GaugeDefinition.Load(file).Name; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { Log.Warn($"Gauge Maker: {file} skipped: {ex.Message}"); }
            if (name is not null) yield return new Choice(name, file, "my gauge");
        }
    }

    private void Fill()
    {
        List.Items.Clear();
        var words = Filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var choice in _all.Where(c => words.All(w => c.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase))).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox { Content = $"{choice.Name}", ToolTip = $"{choice.Where}: {choice.Path}", IsChecked = _ticked.Contains(choice), Margin = new Thickness(0, 2, 0, 2) };
            box.Checked += (_, _) => { if (!_ticked.Contains(choice)) _ticked.Add(choice); Build(); };
            box.Unchecked += (_, _) => { _ticked.Remove(choice); Build(); };
            List.Items.Add(box);
        }
    }

    private void Build()
    {
        if (!IsInitialized) return;
        Picked.Text = _ticked.Count == 0 ? "Nothing ticked yet." : "In this order: " + string.Join("  →  ", _ticked.Select((c, i) => $"{i + 1}. {c.Name}"));
        Error.Text = "";
        _json = null;
        Save.IsEnabled = false;
        Preview.Source = null;

        var side = Side.IsChecked == true;
        if (_ticked.Count < 2) { Error.Text = side ? "Tick two gauges." : "Tick two or more gauges."; return; }
        if (side && _ticked.Count > 2) { Error.Text = "Side by side takes exactly two gauges; untick the extra ones."; return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Tag as string == NameBox.Text)
        {
            NameBox.Text = side ? $"{_ticked[0].Name} + {_ticked[1].Name}" : $"{_ticked[0].Name} (combined)";
            NameBox.Tag = NameBox.Text; // still the suggested name, so keep suggesting
        }

        try
        {
            var files = _ticked.Select(c => File.ReadAllText(c.Path)).ToList();
            var json = side ? GaugeCombiner.SideBySide(files[0], files[1], NameBox.Text.Trim()) : GaugeCombiner.MoreNeedles(files, NameBox.Text.Trim());
            // Point the font at the one shipped with the programs, so the saved gauge draws the same anywhere.
            var node = JsonNode.Parse(json)!.AsObject();
            if (node["font"]?.GetValue<string>() is { } font) node["font"] = Path.GetFileName(font);
            node["author"] = "Made with FIPinator 3000";
            json = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var gauge = GaugeDefinition.Parse(json);
            if (gauge.Problems() is { Count: > 0 } problems) { Error.Text = "These don't combine cleanly: " + problems[0]; return; }
            var renderer = new GaugeRenderer(gauge, AppContext.BaseDirectory);
            _renderer?.Dispose();
            _renderer = renderer;
            using var bitmap = renderer.Render();
            Preview.Source = GaugeImages.ToImage(bitmap);
            _json = json;
            Save.IsEnabled = true;
        }
        catch (ArgumentException ex) { Error.Text = ex.Message; }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            Log.Warn($"Gauge Maker: combine failed: {ex}");
            Error.Text = "One of the gauges couldn't be read. Details are in the log.";
        }
    }

    private void SaveGauge()
    {
        SaveError.Text = "";
        if (_json is null) return;
        var name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "Combined gauge";
        var node = JsonNode.Parse(_json)!.AsObject();
        node["name"] = name;
        try
        {
            var path = GaugeFiles.SaveNew(node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), name);
            Log.Info($"Gauge Maker: saved combined gauge {path} ({string.Join(", ", _ticked.Select(c => c.Name))}).");
            _window.OpenEditor(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Gauge Maker: couldn't save the combined gauge: {ex.Message}");
            SaveError.Text = $"Couldn't save in {GaugeFiles.MyGaugesFolder}: {ex.Message}";
        }
    }

    public void Dispose() => _renderer?.Dispose();
}
