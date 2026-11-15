// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Fipinator.Core;
using SkiaSharp;

namespace Fipinator.GaugeMaker;

/// <summary>
/// The gauge editor: the gauge's parts on the left, the gauge in the middle (click a part, drag to move it),
/// the picked part's settings, "Try it" sliders and the checks on the right. Undo/redo, Save (a built-in
/// gauge is saved as the user's own copy), a backup of the old file before every save, and live reload when
/// the file is changed outside (a text editor, or the user's own AI).
/// </summary>
public partial class EditorPage : UserControl, IDisposable
{
    private const int Backups = 20;
    private static readonly Lazy<IReadOnlyList<(string Sim, string Unit, string Label)>> Known = new(() =>
        GaugeEditor.KnownSimValues(new[] { GaugeFiles.TemplatesFolder, GaugeFiles.MyGaugesFolder }
            .Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.json", SearchOption.AllDirectories))));

    private readonly MainWindow _window;
    private string _path;
    private bool _isMine;
    private GaugeEditor? _editor;
    private string _savedJson = "";
    private PartId? _selected;
    private GaugeRenderer? _renderer;
    private IReadOnlyList<ValueRange> _ranges = [];
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _refreshing;

    // Dragging a part on the gauge.
    private Point? _dragFrom;
    private bool _dragged;

    public EditorPage(MainWindow window, string path)
    {
        InitializeComponent();
        _window = window;
        _path = Path.GetFullPath(path);
        RefPicture.Attach(Reference);

        Back.Click += (_, _) => window.ShowStart();
        Save.Click += (_, _) => SaveGauge();
        Undo.Click += (_, _) => { if (_editor?.CanUndo == true) { _editor.Undo(); Refresh(); } else Say("Nothing to undo."); };
        Redo.Click += (_, _) => { if (_editor?.CanRedo == true) { _editor.Redo(); Refresh(); } else Say("Nothing to redo."); };
        TextEditor.Click += (_, _) => OpenInTextEditor();
        Zoom.SelectionChanged += (_, _) => Render();
        Extremes.Click += (_, _) => Render();
        Reset.Click += (_, _) => { foreach (var r in _ranges) _values[r.Name] = r.Default; BuildValues(); Render(); };
        Look.SelectionChanged += (_, _) => ApplyLook();

        Add.Click += (_, _) => { Add.ContextMenu.PlacementTarget = Add; Add.ContextMenu.IsOpen = true; };
        AddNumberBox.Click += (_, _) => AddWithValue("number box", v => _editor!.AddNumberBox(v));
        AddNeedle.Click += (_, _) => AddWithValue("needle", v => _editor!.AddNeedle(v));
        AddGreenArc.Click += (_, _) => Edit(() => _selected = _editor!.AddArc(GaugeParts.Green));
        AddYellowArc.Click += (_, _) => Edit(() => _selected = _editor!.AddArc(GaugeParts.Yellow));
        AddRedArc.Click += (_, _) => Edit(() => _selected = _editor!.AddArc(GaugeParts.Red));
        AddTextItem.Click += (_, _) => Edit(() => _selected = _editor!.AddText("TEXT"));
        CopyPart.Click += (_, _) => WithPart(id => _selected = _editor!.Duplicate(id));
        Forward.Click += (_, _) => WithPart(id => _selected = _editor!.Reorder(id, +1));
        Backward.Click += (_, _) => WithPart(id => _selected = _editor!.Reorder(id, -1));
        RemovePart.Click += (_, _) => WithPart(id => { _editor!.Remove(id); _selected = null; });
        foreach (var b in NeedleCount.Children.OfType<Button>())
            b.Click += (_, _) => SetNeedles(int.Parse((string)b.Tag, CultureInfo.InvariantCulture));
        foreach (var c in RmiNeedles.Children.OfType<CheckBox>())
            c.Click += (_, _) => SetRmiNeedle(Enum.Parse<RmiRadio>((string)c.Tag), c.IsChecked == true);

        PartsList.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selected = PartsList.SelectedItem is ListBoxItem { Tag: PartId id } ? id : null;
            ShowSettings();
            ShowPicked();
        };

        Stage.MouseLeftButtonDown += StageDown;
        Stage.MouseMove += StageMove;
        Stage.MouseLeftButtonUp += StageUp;
        PreviewKeyDown += Keys;
        _reload.Tick += (_, _) => { _reload.Stop(); FileChangedOutside(); };

        Load();
        Loaded += (_, _) => Focus();
    }

    // ------------------------------------------------------------------ loading and saving

    private void Load()
    {
        _isMine = IsMine(_path);
        FilePath.Text = _path;
        _window.Title = $"FIPinator 3000 - {Path.GetFileNameWithoutExtension(_path)}";
        try
        {
            var text = File.ReadAllText(_path);
            _editor = new GaugeEditor(text);
            _savedJson = _editor.Json;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _editor = null;
            GaugeName.Text = Path.GetFileNameWithoutExtension(_path);
            ShowChecks([$"This file can't be opened as a gauge: {Friendly(ex)}"], ok: false);
            ShowBar("Fix the file in a text editor (or Undo your AI's change), then open it again.", "Open in text editor", OpenInTextEditor);
            return;
        }
        Watch();
        if (!_isMine)
            ShowBar("This is a built-in gauge. Change anything you like: Save keeps it as your own gauge in My gauges, and the built-in one stays as it is.");
        Refresh(resetValues: true);
    }

    private static bool IsMine(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(GaugeFiles.MyGaugesFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private bool Dirty => _editor is not null && _editor.Json != _savedJson;

    /// <summary>Saves; a built-in gauge becomes the user's own copy. Returns false if it couldn't save.</summary>
    private bool SaveGauge()
    {
        if (_editor is null) return false;
        if (_editor.Check(out var problems) is null)
        {
            ShowBar($"Not saved: the gauge has a mistake ({problems.FirstOrDefault()}). Undo the last change, or fix it, then save.");
            return false;
        }
        if (!Dirty && _isMine) { Say("Already saved."); return true; }
        try
        {
            if (_isMine)
            {
                BackUp(_path);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, _editor.Json);
                File.Move(temp, _path, overwrite: true);
                _savedJson = _editor.Json;
                GaugeFiles.KeepSpadCopy(_path);
                Log.Info($"Gauge Maker: saved {_path}.");
                Say("Saved. FIPinator Live shows the change the next time the panel starts.");
            }
            else
            {
                var node = JsonNode.Parse(_editor.Json)!.AsObject();
                var name = _editor.Name.Length > 0 ? _editor.Name : "My gauge";
                node["author"] = "Made with FIPinator 3000";
                // The copy lives elsewhere: point the font at the one shipped with the programs.
                if (node["font"]?.GetValue<string>() is { } font) node["font"] = Path.GetFileName(font);
                var path = GaugeFiles.SaveNew(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), name);
                Log.Info($"Gauge Maker: saved built-in gauge {_path} as {path}.");
                _path = path;
                var keepSelection = _selected;
                Load();
                _selected = keepSelection;
                Refresh();
                ShowBar($"Saved as your own gauge: {Path.GetFileName(path)} in My gauges. The built-in one is unchanged.");
            }
            Refresh();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Gauge Maker: couldn't save {_path}: {ex.Message}");
            ShowBar($"Couldn't save: {ex.Message} Nothing was changed. Close other programs using the file and try again.");
            return false;
        }
    }

    /// <summary>Keeps the file as it was in Documents\FIPinator 3000\Backups\(gauge)\, the newest 20.</summary>
    internal static void BackUp(string path)
    {
        if (!File.Exists(path)) return;
        var folder = Path.Combine(Path.GetDirectoryName(GaugeFiles.MyGaugesFolder)!, "Backups", Path.GetFileNameWithoutExtension(path));
        Directory.CreateDirectory(folder);
        File.Copy(path, Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss-fff}.json"), overwrite: true);
        foreach (var old in Directory.GetFiles(folder, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(Backups))
            File.Delete(old);
    }

    /// <summary>Before leaving the editor: unsaved changes? Save, throw away, or stay. True = OK to leave.</summary>
    public bool ConfirmLeave()
    {
        if (!Dirty) return true;
        var answer = MessageBox.Show(_window, "Save your changes to this gauge first?", "FIPinator 3000",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveGauge(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // ------------------------------------------------------------------ the file changed outside 3000

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_path)!, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        // Editors (and AIs) often save in several steps; wait a moment, then look once.
        void Changed() => Dispatcher.BeginInvoke(() => { _reload.Stop(); _reload.Start(); });
        _watcher.Changed += (_, _) => Changed();
        _watcher.Created += (_, _) => Changed();
        _watcher.Renamed += (_, _) => Changed();
    }

    private void FileChangedOutside()
    {
        if (_editor is null) { Load(); return; }
        string text;
        try { text = File.ReadAllText(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // still being written; another event follows
        string json;
        try { json = new GaugeEditor(text).Json; }
        catch (JsonException ex)
        {
            ShowBar($"The file was changed outside FIPinator 3000, but it has a mistake ({Friendly(ex)}). Still showing the last good version.");
            return;
        }
        if (json == _savedJson || json == _editor.Json) return; // our own save, or nothing new
        if (Dirty)
        {
            ShowBar("The file was changed outside FIPinator 3000 (a text editor or your AI), and you have unsaved changes here.",
                "Load the new file", () => TakeOutsideChange(json), "Keep mine", HideBar);
            return;
        }
        TakeOutsideChange(json);
    }

    private void TakeOutsideChange(string json)
    {
        _editor!.Replace(json);
        _savedJson = json;
        if (_editor.Check(out _) is not null && _isMine) GaugeFiles.KeepSpadCopy(_path);
        Refresh();
        ShowBar("The file was changed outside FIPinator 3000 (a text editor or your AI). Showing the new version: Undo goes back.");
    }

    // ------------------------------------------------------------------ editing

    /// <summary>Runs one edit; a plain error from the editor shows in the bar and changes nothing.</summary>
    private void Edit(Action edit)
    {
        if (_editor is null) return;
        try { edit(); }
        catch (ArgumentException ex) { ShowBar(ex.Message); }
        Refresh();
    }

    private void WithPart(Action<PartId> edit)
    {
        if (_selected is not { } id) { Say("Pick a part first: click it in the list or on the gauge."); return; }
        Edit(() => edit(id));
    }

    private void AddWithValue(string what, Func<string, PartId> add)
    {
        if (_editor is null) return;
        var value = PickValue(what);
        if (value is null) return;
        Edit(() => _selected = add(value));
    }

    /// <summary>Asks what a new part shows: one of the gauge's values or one from the sim. Null = cancelled.</summary>
    private string? PickValue(string what)
    {
        var picked = SimValuePicker.Pick(_window, [.. _editor!.ValueChoices().Select(v => ("", v.Name, $"This gauge's: {v.Label}")), .. Known.Value]);
        if (picked is not { } p) return null;
        // The gauge's own values are listed with an empty sim and their name in place of the unit.
        return p.Sim.Length == 0 ? p.Unit : _editor.UseSimValue(p.Sim, p.Unit);
    }

    private void SetNeedles(int count)
    {
        if (_editor is null) return;
        if (_editor.SetEngineNeedles(count) is { } why) { ShowBar(why); return; }
        Refresh();
    }

    private void SetRmiNeedle(RmiRadio radio, bool on)
    {
        if (_editor is null) return;
        if (_editor.SetRmiNeedle(radio, on) is { } why) ShowBar(why);
        Refresh();
    }

    private void ApplyLook()
    {
        if (_editor is null || Look.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<GaugeLook>(tag, out var look)) return;
        try { _editor.ApplyLook(look); }
        catch (ArgumentException ex) { ShowBar($"This gauge can't change its look: {ex.Message}"); }
        Look.SelectedIndex = 0;
        Refresh();
    }

    private void Apply(string key, string? text)
    {
        if (_editor is null || _selected is not { } id) return;
        try { _editor.Set(id, key, text); }
        catch (ArgumentException ex) { ShowBar(ex.Message); }
        Refresh();
    }

    // ------------------------------------------------------------------ showing it

    /// <summary>Shows the gauge as it is now: parts, settings, sliders, checks and the picture.</summary>
    private void Refresh(bool resetValues = false)
    {
        if (_editor is null) return;
        _refreshing = true;
        try
        {
            GaugeName.Text = _editor.Name + (Dirty ? "  (not saved)" : "");
            SaveText.Text = _isMine ? (Dirty ? "Save" : "Saved") : "Save as mine";

            var parts = _editor.Parts();
            if (_selected is { } sel && !parts.Any(p => p.Id == sel)) _selected = null;
            PartsList.Items.Clear();
            foreach (var part in parts)
            {
                var item = new ListBoxItem { Tag = part.Id, Padding = new Thickness(6 + part.Depth * 16, 4, 6, 4) };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = Glyph(part.Kind), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Width = 22, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = part.Title, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = part.Title });
                item.Content = row;
                PartsList.Items.Add(item);
                if (_selected is { } s && part.Id == s) PartsList.SelectedItem = item;
            }
            if (PartsList.SelectedItem is { } picked) PartsList.ScrollIntoView(picked);

            var engines = _editor.EngineNeedles;
            NeedleHint.Text = engines > 0 ? $"This dial has {engines} engine needle{(engines == 1 ? "" : "s")}." : "This dial's needle doesn't follow an engine.";
            var rmi = _editor.IsRmi && engines == 0;
            RmiSection.Visibility = rmi ? Visibility.Visible : Visibility.Collapsed;
            EngineSection.Visibility = rmi ? Visibility.Collapsed : Visibility.Visible;
            if (rmi)
                foreach (var c in RmiNeedles.Children.OfType<CheckBox>())
                    c.IsChecked = _editor.HasRmiNeedle(Enum.Parse<RmiRadio>((string)c.Tag));

            BackgroundSlot.Content = new ColourButton(_editor.Background, false, hex => Edit(() => _editor.Background = hex!));

            var gauge = _editor.Check(out var problems);
            if (gauge is not null)
            {
                var renderer = new GaugeRenderer(gauge, Path.GetDirectoryName(_path)!);
                _renderer?.Dispose();
                _renderer = renderer;
                var old = resetValues ? [] : new Dictionary<string, double>(_values);
                _ranges = GaugeValueRanges.For(gauge);
                _values.Clear();
                foreach (var r in _ranges) _values[r.Name] = old.TryGetValue(r.Name, out var v) ? r.Clamp(v) : r.Default;
                BuildValues();
                var notes = new List<string>();
                if (renderer.FontWarning is { } fw) notes.Add(fw);
                ShowChecks(notes, ok: true);
                if (SpadExport.SpadInstalled())
                {
                    var (ready, why) = SpadExport.Status(gauge);
                    Checks.Children.Add(Line(ready ? "" : "", ready ? "Also saved for SPAD.neXt when you save." : why,
                        ready ? "SystemFillColorSuccessBrush" : "SystemFillColorAttentionBrush"));
                }
            }
            else
            {
                ShowChecks(problems, ok: false);
            }
            ShowSettings();
            Render();
        }
        finally { _refreshing = false; }
    }

    private void ShowSettings()
    {
        SettingsPanel.Children.Clear();
        if (_editor is null) return;
        if (_selected is not { } id)
        {
            SettingsTitle.Text = "Gauge";
            var name = new TextBox { Text = _editor.Name };
            void Rename() { if (name.Text.Trim() != _editor.Name && name.Text.Trim().Length > 0) Edit(() => _editor.Name = name.Text); }
            name.LostFocus += (_, _) => Rename();
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); };
            SettingsPanel.Children.Add(Row("Name", name));
            SettingsPanel.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 10, 0, 0),
                Text = "Pick a part (in the list or on the gauge) to change its colour, size or what it shows. Drag it on the gauge to move it; the arrow keys nudge it." });
            return;
        }
        var part = _editor.Parts().FirstOrDefault(p => p.Id == id);
        if (part is null) return;
        SettingsTitle.Text = part.Title;
        foreach (var s in _editor.Settings(id))
        {
            FrameworkElement control = s.Kind switch
            {
                SettingKind.Colour => new ColourButton(s.Current, canBeNone: s.Key.EndsWith("stroke", StringComparison.Ordinal) || s.Key is "fill" or "fillTop" or "fillBottom" || s.Key.EndsWith(".fill", StringComparison.Ordinal),
                    hex => Apply(s.Key, hex)),
                SettingKind.Number => NumberBox(s),
                SettingKind.Text => TextBoxFor(s),
                SettingKind.Choice => ChoiceBox(s),
                SettingKind.Value => ValueBox(s),
                _ => new TextBlock { Text = s.Current ?? "" },
            };
            SettingsPanel.Children.Add(Row(s.Label, control));
        }
        if (part.Kind is PartKind.Ticks or PartKind.Labels)
            SettingsPanel.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 10, 0, 0),
                Text = "Where the marks go on the scale (every how many, from where) is set in the file: Open in text editor, or ask your AI." });
    }

    private static Grid Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = control is ColourButton ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private FrameworkElement NumberBox(PartSetting s)
    {
        var box = new TextBox { Text = s.Current ?? "", MinWidth = 60 };
        void Commit() { if (box.Text != s.Current) Apply(s.Key, box.Text.Replace(',', '.')); }
        void Step(int dir)
        {
            if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) v = 0;
            Apply(s.Key, (v + dir * s.Step).ToString("0.###", CultureInfo.InvariantCulture));
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { Step(+1); e.Handled = true; }
            else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
        };
        var minus = new Button { Content = "−", Width = 34, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Smaller" };
        var plus = new Button { Content = "+", Width = 34, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Bigger" };
        minus.Click += (_, _) => Step(-1);
        plus.Click += (_, _) => Step(+1);
        var panel = new DockPanel();
        DockPanel.SetDock(plus, Dock.Right);
        DockPanel.SetDock(minus, Dock.Right);
        panel.Children.Add(plus);
        panel.Children.Add(minus);
        panel.Children.Add(box);
        return panel;
    }

    private FrameworkElement TextBoxFor(PartSetting s)
    {
        var box = new TextBox { Text = s.Current ?? "" };
        void Commit() { if (box.Text != (s.Current ?? "")) Apply(s.Key, box.Text); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return box;
    }

    private static readonly Dictionary<string, string> ChoiceNames = new(StringComparer.Ordinal)
    {
        ["classic"] = "Classic (tapered)", ["pointer"] = "Pointer (DC-6 engines)", ["arrow"] = "Arrow (RMI needle 1)", ["doubleArrow"] = "Double arrow (RMI needle 2)",
        ["0"] = "123", ["00"] = "01", ["000"] = "001", ["0000"] = "0001", ["0.0"] = "12.3", ["0.00"] = "1.23",
    };

    private FrameworkElement ChoiceBox(PartSetting s)
    {
        var box = new ComboBox();
        foreach (var c in s.Choices ?? [])
        {
            var item = new ComboBoxItem { Content = ChoiceNames.GetValueOrDefault(c, c), Tag = c };
            box.Items.Add(item);
            if (c == s.Current) box.SelectedItem = item;
        }
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: string c } && c != s.Current) Apply(s.Key, c); };
        return box;
    }

    private FrameworkElement ValueBox(PartSetting s)
    {
        var box = new ComboBox();
        foreach (var (name, label) in _editor!.ValueChoices())
        {
            var item = new ComboBoxItem { Content = label, Tag = name, ToolTip = label };
            box.Items.Add(item);
            if (name == s.Current) box.SelectedItem = item;
        }
        box.Items.Add(new ComboBoxItem { Content = "Something else from the sim...", Tag = "" });
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not ComboBoxItem { Tag: string name }) return;
            if (name.Length == 0)
            {
                if (SimValuePicker.Pick(_window, Known.Value) is { } p)
                {
                    try
                    {
                        var value = _editor.UseSimValue(p.Sim, p.Unit);
                        _editor.Set(_selected!.Value, s.Key, value);
                    }
                    catch (ArgumentException ex) { ShowBar(ex.Message); }
                }
                Refresh();
                return;
            }
            if (name != s.Current) Apply(s.Key, name);
        };
        return box;
    }

    private void Render()
    {
        if (_renderer is null) return;
        var zoom = Zoom.SelectedIndex + 1;
        if (Extremes.IsChecked == true)
        {
            Preview.Source = RenderExtremes();
            Preview.Width = Preview.Source.Width;
            Preview.Height = Preview.Source.Height;
            Picked.Visibility = Visibility.Collapsed;
            return;
        }
        using var bitmap = _renderer.Render(_values);
        Preview.Source = GaugeImages.ToImage(bitmap);
        Preview.Width = bitmap.Width * zoom;
        Preview.Height = bitmap.Height * zoom;
        Overlay.Width = Preview.Width;
        Overlay.Height = Preview.Height;
        ShowPicked();
    }

    /// <summary>A dashed outline around the picked part.</summary>
    private void ShowPicked()
    {
        if (_editor is null || _selected is not { } id || Extremes.IsChecked == true || _editor.Bounds(id) is not { } b)
        {
            Picked.Visibility = Visibility.Collapsed;
            return;
        }
        var z = Zoom.SelectedIndex + 1;
        Canvas.SetLeft(Picked, b.X * z - 2);
        Canvas.SetTop(Picked, b.Y * z - 2);
        Picked.Width = Math.Max(4, b.W * z + 4);
        Picked.Height = Math.Max(4, b.H * z + 4);
        Picked.Visibility = Visibility.Visible;
    }

    /// <summary>Lowest, normal and highest, two across, with captions.</summary>
    private ImageSource RenderExtremes()
    {
        Dictionary<string, double> Set(Func<ValueRange, double> pick) => _ranges.ToDictionary(r => r.Name, pick, StringComparer.Ordinal);
        var shots = new (string Caption, Dictionary<string, double> Values)[] { ("Lowest", Set(r => r.Min)), ("Normal", Set(r => r.Default)), ("Highest", Set(r => r.Max)) };
        const int w = 320, h = 240, gap = 16, caption = 26;
        using var sheet = new SKBitmap(new SKImageInfo(w * 2 + gap, (h + caption) * 2 + gap, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(new SKColor(0x20, 0x22, 0x24));
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 15);
            for (var i = 0; i < shots.Length; i++)
            {
                var x = (i % 2) * (w + gap);
                var y = (i / 2) * (h + caption + gap);
                canvas.DrawText(shots[i].Caption, x + 4, y + 18, SKTextAlign.Left, font, paint);
                using var gauge = _renderer!.Render(shots[i].Values);
                using var image = SKImage.FromBitmap(gauge);
                canvas.DrawImage(image, x, y + caption, new SKSamplingOptions(SKFilterMode.Nearest), null);
            }
        }
        return GaugeImages.ToImage(sheet);
    }

    private void BuildValues()
    {
        Values.Children.Clear();
        if (_ranges.Count == 0)
        {
            Values.Children.Add(new TextBlock { Text = "This gauge has no values to move.", Style = (Style)FindResource("Hint") });
            return;
        }
        foreach (var range in _ranges)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition());
            row.RowDefinitions.Add(new RowDefinition());
            var label = _editor!.ValueChoices().FirstOrDefault(v => v.Name == range.Name).Label ?? range.Name;
            var name = new TextBlock { Text = label, ToolTip = $"{range.Name} · double-click to reset", TextWrapping = TextWrapping.Wrap };
            var shown = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") };
            Grid.SetColumn(shown, 1);
            row.Children.Add(name);
            row.Children.Add(shown);
            if (range.IsOnOff)
            {
                var toggle = new CheckBox { Content = "On", IsChecked = _values[range.Name] >= 0.5 };
                toggle.Click += (_, _) => { _values[range.Name] = toggle.IsChecked == true ? 1 : 0; Render(); };
                Grid.SetRow(toggle, 1);
                Grid.SetColumnSpan(toggle, 2);
                row.Children.Add(toggle);
            }
            else
            {
                var slider = new Slider { Minimum = range.Min, Maximum = range.Max, Value = _values[range.Name], SmallChange = range.Step, LargeChange = range.Step * 10, IsSnapToTickEnabled = true, TickFrequency = range.Step };
                void Show() => shown.Text = _values[range.Name].ToString(range.Step >= 1 ? "0" : range.Step >= 0.1 ? "0.0" : "0.00", CultureInfo.InvariantCulture);
                slider.ValueChanged += (_, e) => { _values[range.Name] = e.NewValue; Show(); Render(); };
                name.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) slider.Value = range.Default; };
                Grid.SetRow(slider, 1);
                Grid.SetColumnSpan(slider, 2);
                row.Children.Add(slider);
                Show();
            }
            Values.Children.Add(row);
        }
    }

    private void ShowChecks(IReadOnlyList<string> lines, bool ok)
    {
        Checks.Children.Clear();
        if (ok && lines.Count == 0) { Checks.Children.Add(Line("", "No problems found.", "SystemFillColorSuccessBrush")); return; }
        foreach (var l in lines) Checks.Children.Add(Line(ok ? "" : "", l, ok ? "SystemFillColorCautionBrush" : "SystemFillColorCriticalBrush"));
        if (!ok && _renderer is not null)
            Checks.Children.Add(new TextBlock { Text = "The picture shows the last version without mistakes. Undo goes back.", Style = (Style)FindResource("Hint"), Margin = new Thickness(26, 6, 0, 0) });
    }

    private UIElement Line(string glyph, string text, string brush)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Width = 26, Foreground = (Brush)FindResource(brush) });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static string Glyph(PartKind kind) => kind switch
    {
        PartKind.Needle => "",
        PartKind.NumberBox or PartKind.Number => "",
        PartKind.Text => "",
        PartKind.Ticks or PartKind.Labels => "",
        PartKind.Arc => "",
        PartKind.Circle or PartKind.Hub => "",
        PartKind.Box => "",
        PartKind.Group => "",
        PartKind.Glass => "",
        _ => "",
    };

    // ------------------------------------------------------------------ the bar for messages

    private void ShowBar(string text, string? button = null, Action? click = null, string? button2 = null, Action? click2 = null)
    {
        BarText.Text = text;
        Bar.Visibility = Visibility.Visible;
        SetBarButton(BarButton, button, click);
        SetBarButton(BarButton2, button2, click2);
    }

    private void SetBarButton(Button b, string? text, Action? click)
    {
        b.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        b.Content = text;
        b.Tag = click;
        b.Click -= BarClick;
        b.Click += BarClick;
    }

    private void BarClick(object sender, RoutedEventArgs e)
    {
        var action = (sender as Button)?.Tag as Action;
        HideBar();
        action?.Invoke();
    }

    private void HideBar() => Bar.Visibility = Visibility.Collapsed;

    /// <summary>A short message in the bar (replaces the last one).</summary>
    private void Say(string text) => ShowBar(text);

    // ------------------------------------------------------------------ mouse and keys

    private Point ToGauge(Point p)
    {
        var z = Zoom.SelectedIndex + 1;
        return new Point(p.X / z, p.Y / z);
    }

    private void StageDown(object sender, MouseButtonEventArgs e)
    {
        if (_editor is null || Extremes.IsChecked == true) return;
        var at = ToGauge(e.GetPosition(Preview));
        var hit = _editor.PartAt(at.X, at.Y);
        // Clicking inside the picked part keeps it picked (so a small part on top doesn't steal the drag).
        if (_selected is { } sel && _editor.Bounds(sel) is { } b && at.X >= b.X && at.Y >= b.Y && at.X <= b.X + b.W && at.Y <= b.Y + b.H) hit = sel;
        if (hit != _selected)
        {
            _selected = hit;
            Refresh();
        }
        if (_selected is null) return;
        _editor.Checkpoint();
        _dragFrom = at;
        _dragged = false;
        Stage.CaptureMouse();
        Focus();
        e.Handled = true;
    }

    private void StageMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || _editor is null || _selected is not { } id) return;
        var at = ToGauge(e.GetPosition(Preview));
        var (dx, dy) = (Math.Round(at.X - from.X), Math.Round(at.Y - from.Y));
        if (dx == 0 && dy == 0) return;
        if (!_dragged && Math.Abs(dx) + Math.Abs(dy) < 2) return; // a click, not a drag
        _dragged = true;
        try { _editor.Move(id, dx, dy, checkpoint: false); }
        catch (ArgumentException) { return; }
        _dragFrom = new Point(from.X + dx, from.Y + dy);
        DrawQuick();
    }

    private void StageUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFrom is null) return;
        _dragFrom = null;
        Stage.ReleaseMouseCapture();
        _editor?.DropCheckpointIfUnchanged();
        if (_dragged) Refresh();
    }

    /// <summary>While dragging: just redraw the gauge and the outline (the lists stay as they are).</summary>
    private void DrawQuick()
    {
        if (_editor?.Check(out _) is not { } gauge) return;
        var renderer = new GaugeRenderer(gauge, Path.GetDirectoryName(_path)!);
        _renderer?.Dispose();
        _renderer = renderer;
        Render();
    }

    private void Keys(object sender, KeyEventArgs e)
    {
        if (_editor is null) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.S) { SaveGauge(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) return; // typing in a box: leave its keys alone
        if (ctrl && e.Key == Key.Z) { Undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { Redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; return; }
        if (_selected is not { } id) return;
        if (e.Key == Key.Delete) { WithPart(p => { _editor.Remove(p); _selected = null; }); e.Handled = true; return; }
        if (Keyboard.FocusedElement is ComboBox or ListBox && e.Key is Key.Up or Key.Down) return; // moving in a list
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1;
        var (dx, dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
        if (dx == 0 && dy == 0) return;
        Edit(() => _editor.Move(id, dx, dy));
        e.Handled = true;
    }

    // ------------------------------------------------------------------ odds and ends

    private void OpenInTextEditor()
    {
        try { Process.Start(new ProcessStartInfo { FileName = _path, Verb = "edit", UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{_path}\"", UseShellExecute = true })?.Dispose();
        }
        if (!_isMine) ShowBar("This is a built-in gauge: changes made in the text editor go into the built-in file. To keep your own copy, Save as mine first.");
    }

    private static string Friendly(Exception ex) => ex switch
    {
        JsonException { LineNumber: { } line } j => $"line {line + 1}: {j.Message.Split(" Path:")[0].Split(" LineNumber:")[0]}",
        _ => ex.Message,
    };

    public void Dispose()
    {
        _reload.Stop();
        _watcher?.Dispose();
        _renderer?.Dispose();
    }
}
