// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

/// <summary>
/// "Quick colours": recolour one of the user's own gauges without the full editor. The gauge's colours are
/// listed in plain groups (background, case and dial, markings, readouts, needles, coloured ranges); picking a
/// new colour changes it in that group only. A whole ready-made look can be applied too. Saves like the editor
/// (dated backup first, SPAD copy kept up to date).
/// </summary>
public partial class QuickColoursPage : UserControl
{
    private readonly MainWindow _window;
    private readonly string _path;
    private readonly GaugeEditor _editor;
    private string _savedJson;

    public QuickColoursPage(MainWindow window, string path)
    {
        _window = window;
        _path = path;
        InitializeComponent();
        _editor = new GaugeEditor(File.ReadAllText(path));
        _savedJson = _editor.Json;
        Heading.Text = $"Quick colours: {_editor.Name}";
        Back.Click += (_, _) => window.ShowStart();
        FullEditor.Click += (_, _) => window.OpenEditor(_path); // asks about unsaved colours first
        Undo.Click += (_, _) => { _editor.Undo(); Refresh("Undone."); };
        Save.Click += (_, _) => SaveGauge();
        Look.SelectionChanged += (_, _) =>
        {
            if (Look.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<GaugeLook>(tag, out var look)) return;
            try { _editor.ApplyLook(look); Refresh($"Look changed to {((ComboBoxItem)Look.SelectedItem).Content}."); }
            catch (ArgumentException ex) { Status.Text = ex.Message; }
            Look.SelectedIndex = 0;
        };
        Refresh("Nothing changes until you save.");
    }

    private bool Dirty => _editor.Json != _savedJson;

    private void Refresh(string status)
    {
        Status.Text = status;
        Undo.IsEnabled = _editor.CanUndo;
        Save.IsEnabled = Dirty;
        var gauge = _editor.Check(out var problems);
        if (gauge is null) { Preview.Source = null; Status.Text = $"The gauge has a mistake: {problems.FirstOrDefault()} Undo the last change."; }
        else
        {
            using var renderer = new GaugeRenderer(gauge, Path.GetDirectoryName(_path)!);
            using var bitmap = renderer.Render();
            Preview.Source = GaugeImages.ToImage(bitmap);
        }

        Groups.Children.Clear();
        foreach (var group in _editor.ColourGroups().GroupBy(g => g.Role))
        {
            Groups.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
            var row = new WrapPanel();
            foreach (var (role, colour, count) in group)
            {
                var button = new ColourButton(colour, canBeNone: false, picked =>
                {
                    if (picked is null || picked.Equals(colour, StringComparison.OrdinalIgnoreCase)) return;
                    _editor.ReplaceColour(role, colour, picked);
                    Refresh($"{role}: {colour} changed to {picked}. Save to keep it.");
                })
                { Margin = new Thickness(0, 0, 8, 6), ToolTip = count == 1 ? "Used once" : $"Used {count} times in this group" };
                row.Children.Add(button);
            }
            Groups.Children.Add(row);
        }
    }

    private bool SaveGauge()
    {
        if (_editor.Check(out var problems) is null) { Status.Text = $"Not saved: the gauge has a mistake ({problems.FirstOrDefault()}). Undo the last change."; return false; }
        try
        {
            EditorPage.BackUp(_path);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, _editor.Json);
            File.Move(temp, _path, overwrite: true);
            _savedJson = _editor.Json;
            GaugeFiles.KeepSpadCopy(_path);
            Log.Info($"Gauge Maker: quick colours saved {_path}.");
            Refresh("Saved. FIPinator Live shows the change the next time the panel starts.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Gauge Maker: couldn't save {_path}: {ex.Message}");
            Status.Text = $"Couldn't save: {ex.Message} Nothing was changed. Close other programs using the file and try again.";
            return false;
        }
    }

    /// <summary>Before leaving: unsaved colours? Save, throw away, or stay. True = OK to leave.</summary>
    public bool ConfirmLeave()
    {
        if (!Dirty) return true;
        return MessageBox.Show(_window, "Save your colour changes first?", "FIPinator 3000", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => SaveGauge(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }
}
