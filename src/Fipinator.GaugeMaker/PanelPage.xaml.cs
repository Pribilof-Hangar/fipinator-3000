// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

/// <summary>
/// "Set up your panel": the panel's FIPs as boxes on a desk, laid out like the real FIPs (positions saved in the
/// panel file, ignored by FIPinator Live). Drag a gauge onto another box to swap them (on the same box: swap
/// pages), or drag any gauge from the full list onto a box. Any gauge fits any FIP (owner, 2026-11-14). Saving
/// keeps a dated copy of the panel first; "Save as" makes a new panel and leaves the original alone.
/// </summary>
public partial class PanelPage : UserControl
{
    private sealed record PanelItem(string Name, string Path);
    private sealed record Choice(string Text, string Path, string Search);
    private sealed record GaugeDrag(string Serial, int Page);

    private const double BoxWidth = 180, BoxHeight = 230;

    /// <summary>The desk is a grid of slots, one FIP box each: any number of rows and columns.</summary>
    private const int SlotWidth = 200, SlotHeight = 250, Edge = 10;

    /// <summary>
    /// Spots on the desk: 12 across and 12 down (owner, 2026-11-14), or more when a panel has more FIPs than
    /// that, so every FIP fits in one row or one column. The desk scrolls, also while dragging a box to an edge.
    /// </summary>
    private int Columns => Math.Max(12, _fips.Count);

    /// <inheritdoc cref="Columns"/>
    private int Rows => Math.Max(12, _fips.Count);
    private const string GaugeFormat = "FipinatorGauge", LibraryFormat = "FipinatorLibraryGauge";

    private readonly PanelLibrary _library = PanelLibrary.ForCurrentUser();
    private readonly MainWindow _window;
    private PanelItem? _panel;
    private PanelSettings? _settings;
    private List<FipAssignment> _fips = [];

    /// <summary>
    /// The panel's layouts (owner, 2026-11-14): the main one first, then e.g. "Engines"; each a full set of gauges
    /// for the same FIPs. <see cref="_fips"/> is the one being edited. Desk places, left-knob flipping and flipping
    /// by itself belong to the FIP, so they're kept the same in every layout.
    /// </summary>
    private List<(string Name, List<FipAssignment> Fips)> _layouts = [];
    private int _layoutIndex;
    private LayoutButton? _layoutButton;
    private bool _fillingLayoutControls;
    private readonly Dictionary<string, int> _shownPage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private List<Choice> _choices = [];
    private string? _selected;
    private bool _changed;
    private Point _pressAt;

    public PanelPage(MainWindow window, string? panelPath = null)
    {
        _window = window;
        InitializeComponent();
        Back.Click += (_, _) => window.ShowStart();
        Panels.SelectionChanged += (_, _) => { if (Panels.SelectedItem is PanelItem p) LoadPanel(p); };
        Search.TextChanged += (_, _) => FilterLibrary();
        Library.SelectionChanged += (_, _) => { Preview.Source = Library.SelectedItem is Choice c ? Picture(c.Path) : null; UpdateButtons(); };
        Library.PreviewMouseLeftButtonDown += (_, e) => _pressAt = e.GetPosition(Library);
        Library.PreviewMouseMove += LibraryDrag;
        PutHere.Click += (_, _) => { if (Library.SelectedItem is Choice c && _selected is { } s) PutGauge(s, Shown(s), c.Path); };
        AddPage.Click += (_, _) => { if (Library.SelectedItem is Choice c && _selected is { } s) AddGaugePage(s, c.Path); };
        Undo.Click += (_, _) => { if (_panel is not null) LoadPanel(_panel); };
        Save.Click += (_, _) => SavePanel();
        SaveAs.Click += (_, _) => SaveAsNew();
        // A gauge dropped on an empty spot moves its whole FIP there (same as dragging the box's top bar).
        Desk.AllowDrop = true;
        Desk.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(GaugeFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        Desk.Drop += (_, e) =>
        {
            if (e.Data.GetData(GaugeFormat) is not GaugeDrag drag) return;
            var at = e.GetPosition(Desk);
            var (x, y) = SnapToSlot(at.X - BoxWidth / 2, at.Y - 20);
            MoveFip(drag.Serial, x, y);
        };
        Layouts.SelectionChanged += (_, _) =>
        {
            if (_fillingLayoutControls || Layouts.SelectedIndex < 0 || Layouts.SelectedIndex == _layoutIndex) return;
            ShowLayout(Layouts.SelectedIndex);
        };
        NewLayout.Click += (_, _) => AddLayout();
        RenameLayout.Click += (_, _) => RenameCurrentLayout();
        DeleteLayout.Click += (_, _) => DeleteCurrentLayout();
        SwitchFip.SelectionChanged += (_, _) => LayoutButtonChanged();
        SwitchButton.SelectionChanged += (_, _) => LayoutButtonChanged();
        FillPanels(panelPath ?? _library.ActivePath());
        if (Panels.Items.Count == 0) Status.Text = "No panels yet. Set up your FIPs in FIPinator Live first.";
    }

    private string Folder => Path.GetDirectoryName(_panel!.Path)!;

    private void FillPanels(string? select)
    {
        var items = _library.List().Select(p => new PanelItem(p.Name, p.Path)).ToList();
        Panels.ItemsSource = items;
        Panels.SelectedItem = items.FirstOrDefault(p => p.Path.Equals(select, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
    }

    private void LoadPanel(PanelItem panel)
    {
        try
        {
            _panel = panel;
            _settings = PanelSettings.Load(panel.Path);
            var main = _settings.Fips.Select(f => f with { Pages = [.. f.Pages] }).ToList();
            _layouts = [.. _settings.AllLayouts().Select((l, i) => (l.Name, i == 0 ? main
                // Every FIP in every layout: one a layout leaves out gets its main-layout gauges.
                : main.Select(m => (l.Fips.FirstOrDefault(f => f.Serial.Equals(m.Serial, StringComparison.OrdinalIgnoreCase)) ?? m) with
                    { Pages = [.. (l.Fips.FirstOrDefault(f => f.Serial.Equals(m.Serial, StringComparison.OrdinalIgnoreCase)) ?? m).Pages] }).ToList()))];
            _layoutIndex = 0;
            _fips = _layouts[0].Fips;
            _layoutButton = _settings.LayoutButton;
            SyncFipSettings();
            _shownPage.Clear();
            _selected = _fips.FirstOrDefault()?.Serial;
            SetChanged(false);
            Status.Text = "Nothing changes until you save. A copy of the panel is kept before saving.";
            LoadLibrary();
            FillLayoutControls();
            DrawDesk();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status.Text = $"Couldn't open that panel: {ex.Message}";
            Log.Warn($"Gauge Maker: panel {panel.Path} can't be opened: {ex.Message}");
        }
    }

    private void SetChanged(bool changed)
    {
        _changed = changed;
        Save.IsEnabled = Undo.IsEnabled = changed;
        SaveAs.IsEnabled = _panel is not null;
    }

    // ---------- The gauge list ----------

    private void LoadLibrary()
    {
        var used = _fips.SelectMany(f => PanelSettings.PagePaths(f, Folder)).ToList();
        // The gauges come from the same place as the panel's own, so the panel stays in one place.
        var root = GaugeKinds.LibraryRoot(used);
        var library = root ?? (Directory.Exists(LiveGauges) ? LiveGauges : GaugeFiles.TemplatesFolder);
        _choices = [.. GaugeCatalog.All(library, GaugeFiles.MyGaugesFolder)
            .Select(c => new Choice($"{c.Group}  ·  {c.Name}", c.Path, $"{c.Group} {c.Name}"))];
        FilterLibrary();
    }

    private void FilterLibrary()
    {
        var words = Search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Library.ItemsSource = _choices.Where(c => words.All(w => c.Search.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    /// <summary>The gauges installed with FIPinator Live (what panels made in Live point to).</summary>
    private static string LiveGauges => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FIPinator Live", "Gauges");

    private void LibraryDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Library.SelectedItem is not Choice c || !FarEnough(e.GetPosition(Library))) return;
        DragDrop.DoDragDrop(Library, new DataObject(LibraryFormat, c.Path), DragDropEffects.Copy);
    }

    private bool FarEnough(Point now) =>
        Math.Abs(now.X - _pressAt.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(now.Y - _pressAt.Y) > SystemParameters.MinimumVerticalDragDistance;

    private void UpdateButtons() => PutHere.IsEnabled = AddPage.IsEnabled = Library.SelectedItem is Choice && _selected is not null;

    // ---------- The desk ----------

    private void DrawDesk()
    {
        Desk.Children.Clear();
        // Empty spots, drawn as dashed outlines, so it's clear where a FIP can go.
        for (var row = 0; row < Rows; row++)
            for (var column = 0; column < Columns; column++)
            {
                var (sx, sy) = SlotPlace(column, row);
                var spot = new System.Windows.Shapes.Rectangle
                {
                    Width = BoxWidth, Height = BoxHeight, RadiusX = 4, RadiusY = 4, Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x56)),
                    StrokeThickness = 1.5, StrokeDashArray = [4, 4], IsHitTestVisible = false,
                };
                Canvas.SetLeft(spot, sx);
                Canvas.SetTop(spot, sy);
                Desk.Children.Add(spot);
            }
        for (var i = 0; i < _fips.Count; i++)
        {
            var fip = _fips[i];
            var box = MakeBox(fip);
            var (x, y) = fip.DeskX is { } dx && fip.DeskY is { } dy ? SnapToSlot(dx, dy) : SlotPlace(i % 4, i / 4);
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            Desk.Children.Add(box);
        }
        Desk.Width = 2 * Edge + Columns * SlotWidth - (SlotWidth - BoxWidth);
        Desk.Height = 2 * Edge + Rows * SlotHeight - (SlotHeight - BoxHeight);
        UpdateButtons();
    }

    private Border MakeBox(FipAssignment fip)
    {
        var serial = fip.Serial;
        var pages = PagesOf(fip);
        var shown = Shown(serial);
        var path = Path.GetFullPath(Path.Combine(Folder, pages[shown]));

        var bar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x46)), Cursor = Cursors.SizeAll };
        var title = new TextBlock { Text = $"FIP ...{Short(serial)}", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 4, 4), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(title);
        var box = new Border
        {
            Width = BoxWidth, Height = BoxHeight, Background = new SolidColorBrush(Color.FromRgb(0x26, 0x2A, 0x2E)), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(serial.Equals(_selected, StringComparison.OrdinalIgnoreCase) ? 3 : 1),
            BorderBrush = serial.Equals(_selected, StringComparison.OrdinalIgnoreCase) ? SelectedBrush : EdgeBrush,
            AllowDrop = true, Tag = serial,
        };
        MoveWithBar(bar, box, serial);

        var image = new Image { Source = Picture(path), Width = 160, Height = 120, Margin = new Thickness(0, 6, 0, 4), Cursor = Cursors.Hand, ToolTip = "Drag onto another FIP to swap" };
        image.MouseLeftButtonDown += (_, e) => { _pressAt = e.GetPosition(Desk); Select(serial); };
        image.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !FarEnough(e.GetPosition(Desk))) return;
            DragDrop.DoDragDrop(image, new DataObject(GaugeFormat, new GaugeDrag(serial, Shown(serial))), DragDropEffects.Move);
        };

        var name = new TextBlock { Text = NameOf(path), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(8, 0, 8, 4), TextAlignment = TextAlignment.Center };
        var pageRow = new DockPanel { Margin = new Thickness(6, 0, 6, 6), LastChildFill = true };
        if (pages.Count > 1)
        {
            var back = SmallButton("", "Show the page before", () => ShowPage(serial, -1));
            var next = SmallButton("", "Show the next page", () => ShowPage(serial, +1));
            var remove = SmallButton("", "Take this page off the FIP", () => RemovePage(serial, Shown(serial)));
            DockPanel.SetDock(back, Dock.Left);
            DockPanel.SetDock(remove, Dock.Right);
            DockPanel.SetDock(next, Dock.Right);
            pageRow.Children.Add(back);
            pageRow.Children.Add(remove);
            pageRow.Children.Add(next);
        }
        var tags = new List<string>();
        if (pages.Count > 1 && fip.AutoFlipSeconds > 0) tags.Add($"auto {fip.AutoFlipSeconds} s");
        if (pages.Count > 1 && fip.LeftKnobPages) tags.Add("knob flips");
        if (_layouts.Count > 1 && _layoutButton is { } lb && lb.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase)) tags.Add($"{ButtonLabel(lb.Button)}: layouts");
        pageRow.Children.Add(new TextBlock
        {
            Text = (pages.Count > 1 ? $"page {shown + 1} of {pages.Count}" : "one gauge") + (tags.Count > 0 ? "  ·  " + string.Join(", ", tags) : ""),
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xA5, 0x9B)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });

        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(image);
        stack.Children.Add(name);
        stack.Children.Add(pageRow);
        box.Child = stack;
        box.MouseLeftButtonDown += (_, _) => Select(serial);
        box.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(GaugeFormat) ? DragDropEffects.Move : e.Data.GetDataPresent(LibraryFormat) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        box.Drop += (_, e) => DropOn(serial, e);
        box.ContextMenu = BoxMenu(fip, pages.Count);
        box.ContextMenuOpening += (_, _) => Select(serial);
        return box;
    }

    private static Button SmallButton(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Padding = new Thickness(6, 2, 6, 2), MinWidth = 0, MinHeight = 0, ToolTip = tip, Margin = new Thickness(2, 0, 2, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static (int X, int Y) SlotPlace(int column, int row) => (Edge + column * SlotWidth, Edge + row * SlotHeight);

    /// <summary>The slot nearest to a spot on the desk (so boxes line up in neat rows and columns).</summary>
    private (int X, int Y) SnapToSlot(double x, double y) =>
        SlotPlace(Math.Clamp((int)Math.Round((x - Edge) / SlotWidth), 0, Columns - 1), Math.Clamp((int)Math.Round((y - Edge) / SlotHeight), 0, Rows - 1));

    /// <summary>
    /// Dragging a box by its top bar moves it on the desk; let go and it snaps into the nearest slot. If another
    /// FIP is already there, the two change places.
    /// </summary>
    private void MoveWithBar(FrameworkElement bar, Border box, string serial)
    {
        Point? grab = null;
        var from = (X: 0, Y: 0);
        bar.MouseLeftButtonDown += (_, e) =>
        {
            grab = e.GetPosition(box);
            from = SnapToSlot(Canvas.GetLeft(box), Canvas.GetTop(box));
            bar.CaptureMouse();
            Panel.SetZIndex(box, 1);
            Select(serial);
            e.Handled = true;
        };
        void Follow()
        {
            if (grab is not { } g || !bar.IsMouseCaptured) return;
            var at = Mouse.GetPosition(Desk);
            Canvas.SetLeft(box, Math.Clamp(at.X - g.X, 0, Desk.Width - BoxWidth));
            Canvas.SetTop(box, Math.Clamp(at.Y - g.Y, 0, Desk.Height - BoxHeight));
        }
        // Near an edge of the visible desk, keep scrolling while the mouse is held there.
        var scroller = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        scroller.Tick += (_, _) =>
        {
            if (grab is null) { scroller.Stop(); return; }
            var p = Mouse.GetPosition(DeskScroll);
            const double zone = 40, step = 25;
            double dx = p.X < zone ? -step : p.X > DeskScroll.ViewportWidth - zone ? step : 0;
            double dy = p.Y < zone ? -step : p.Y > DeskScroll.ViewportHeight - zone ? step : 0;
            if (dx != 0) DeskScroll.ScrollToHorizontalOffset(DeskScroll.HorizontalOffset + dx);
            if (dy != 0) DeskScroll.ScrollToVerticalOffset(DeskScroll.VerticalOffset + dy);
            if (dx != 0 || dy != 0) Follow();
        };
        bar.MouseMove += (_, _) => { Follow(); if (grab is not null && !scroller.IsEnabled) scroller.Start(); };
        bar.MouseLeftButtonUp += (_, _) =>
        {
            if (grab is null) return;
            grab = null;
            scroller.Stop();
            bar.ReleaseMouseCapture();
            var (x, y) = SnapToSlot(Canvas.GetLeft(box), Canvas.GetTop(box));
            if ((x, y) == from) { DrawDesk(); return; }
            MoveFip(serial, x, y);
        };
    }

    /// <summary>Puts a FIP's box in a desk spot; a FIP already there takes the moved one's old spot.</summary>
    private void MoveFip(string serial, int x, int y)
    {
        var i = Index(serial);
        if (i < 0) return;
        // Fix every box's place first (boxes not placed yet are where DrawDesk put them).
        for (var k = 0; k < _fips.Count; k++)
            if (_fips[k].DeskX is null || _fips[k].DeskY is null)
            {
                var (px, py) = SlotPlace(k % 4, k / 4);
                _fips[k] = _fips[k] with { DeskX = px, DeskY = py };
            }
        var from = (X: _fips[i].DeskX!.Value, Y: _fips[i].DeskY!.Value);
        if (SnapToSlot(from.X, from.Y) == (x, y)) { DrawDesk(); return; }
        var other = _fips.FindIndex(f => f != _fips[i] && f.DeskX is { } ox && f.DeskY is { } oy && SnapToSlot(ox, oy) == (x, y));
        if (other >= 0) _fips[other] = _fips[other] with { DeskX = from.X, DeskY = from.Y };
        _fips[i] = _fips[i] with { DeskX = x, DeskY = y };
        SyncFipSettings();
        Changed(other >= 0 ? $"FIP ...{Short(serial)} and FIP ...{Short(_fips[other].Serial)} changed places on the desk." : $"FIP ...{Short(serial)} moved on the desk.");
    }

    /// <summary>Marks a box as the selected FIP (restyles the boxes; no redraw, so a drag can start from it).</summary>
    private void Select(string serial)
    {
        _selected = serial;
        foreach (var box in Desk.Children.OfType<Border>())
        {
            var on = serial.Equals(box.Tag as string, StringComparison.OrdinalIgnoreCase);
            box.BorderThickness = new Thickness(on ? 3 : 1);
            box.BorderBrush = on ? SelectedBrush : EdgeBrush;
        }
        UpdateButtons();
    }

    private static readonly SolidColorBrush EdgeBrush = new(Color.FromRgb(0x3A, 0x40, 0x46));
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromRgb(0xF2, 0xA0, 0x3D)); // cockpit amber

    private void ShowPage(string serial, int step)
    {
        var count = PagesOf(_fips[Index(serial)]).Count;
        _shownPage[serial] = (Shown(serial) + step + count) % count;
        _selected = serial;
        DrawDesk();
    }

    private void DropOn(string target, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(LibraryFormat) is string path) { PutGauge(target, Shown(target), path); return; }
        if (e.Data.GetData(GaugeFormat) is not GaugeDrag from) return;
        var to = new GaugeDrag(target, Shown(target));
        if (from == to) return;
        var a = Index(from.Serial);
        var b = Index(to.Serial);
        if (a < 0 || b < 0) return;
        var pagesA = PagesOf(_fips[a]);
        var gaugeA = pagesA[from.Page];
        if (a == b)
        {
            (pagesA[from.Page], pagesA[to.Page]) = (pagesA[to.Page], pagesA[from.Page]);
            SetPages(a, pagesA);
            _shownPage[target] = to.Page;
            Changed($"FIP ...{Short(target)}: pages {from.Page + 1} and {to.Page + 1} swapped.");
            return;
        }
        var pagesB = PagesOf(_fips[b]);
        var gaugeB = pagesB[to.Page];
        pagesA[from.Page] = gaugeB;
        pagesB[to.Page] = gaugeA;
        SetPages(a, pagesA);
        SetPages(b, pagesB);
        _selected = target;
        Changed($"Swapped: FIP ...{Short(target)} now shows {NameOf(Full(gaugeA))}, FIP ...{Short(from.Serial)} shows {NameOf(Full(gaugeB))}.");
    }

    private void PutGauge(string serial, int page, string path)
    {
        var i = Index(serial);
        if (i < 0) return;
        var pages = PagesOf(_fips[i]);
        pages[page] = path;
        SetPages(i, pages);
        _selected = serial;
        Changed($"FIP ...{Short(serial)} will show {NameOf(path)}.");
    }

    private void AddGaugePage(string serial, string path)
    {
        var i = Index(serial);
        if (i < 0) return;
        var pages = PagesOf(_fips[i]);
        if (pages.Count >= 16) { Status.Text = "A FIP can have 16 pages at most."; return; }
        pages.Add(path);
        SetPages(i, pages);
        _shownPage[serial] = pages.Count - 1;
        _selected = serial;
        Changed($"FIP ...{Short(serial)} now has {pages.Count} pages ({NameOf(path)} added at the end)."
            + (_fips[i].LeftKnobPages || _fips[i].AutoFlipSeconds > 0 ? "" : " On the FIP, the page up/down buttons flip between them (or right-click the box to flip with the left knob)."));
    }

    private void RemovePage(string serial, int page)
    {
        var i = Index(serial);
        var pages = PagesOf(_fips[i]);
        if (pages.Count < 2) return;
        var removed = NameOf(Full(pages[page]));
        pages.RemoveAt(page);
        SetPages(i, pages);
        _shownPage[serial] = Math.Min(page, pages.Count - 1);
        Changed($"FIP ...{Short(serial)}: {removed} taken off.");
    }

    private void Changed(string what)
    {
        SetChanged(true);
        Status.Text = what + " Save the panel to keep it.";
        DrawDesk();
    }

    // ---------- Saving ----------

    private void SavePanel()
    {
        if (_panel is null || _settings is null || !_changed) return;
        try
        {
            var backup = $"{_panel.Path}.before-3000-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(_panel.Path, backup, overwrite: false);
            ToSettings(_settings, f => f).Save(_panel.Path);
            _settings = PanelSettings.Load(_panel.Path);
            SetChanged(false);
            Status.Text = $"Saved. The old version is kept as {Path.GetFileName(backup)}. FIPinator Live shows the change after you switch panels or restart it. You can close FIPinator 3000 now.";
            Log.Info($"Gauge Maker: panel {_panel.Name} saved (backup {Path.GetFileName(backup)}).");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status.Text = $"Couldn't save the panel: {ex.Message} Nothing was changed.";
            Log.Warn($"Gauge Maker: panel {_panel.Name} not saved: {ex.Message}");
        }
    }

    private void SaveAsNew()
    {
        if (_panel is null || _settings is null) return;
        var name = AskName(_panel.Name + " 2");
        if (name is null) return;
        var file = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch)).Trim().TrimEnd('.');
        if (file.Length == 0) { Status.Text = "That name can't be used. Try letters and numbers."; return; }
        var path = Path.Combine(_library.PanelsFolder, file + ".json");
        if (File.Exists(path) || _library.List().Any(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
        {
            Status.Text = $"There's already a panel called \"{name}\". Pick another name; nothing was saved.";
            return;
        }
        try
        {
            // Gauge paths stay pointing at the same files (made absolute, so the new panel works from its own folder).
            (ToSettings(_settings, f => f with { Gauge = Full(f.Gauge), Pages = [.. f.Pages.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Full)] }) with { Name = name }).Save(path);
            Log.Info($"Gauge Maker: panel saved as new panel {name} ({path}).");
            var original = _panel.Name;
            FillPanels(path);
            Status.Text = $"Saved as \"{name}\". \"{original}\" is unchanged. You can close FIPinator 3000 now."
                + " Pick it in FIPinator Live's panel list to use it.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status.Text = $"Couldn't save the new panel: {ex.Message}";
            Log.Warn($"Gauge Maker: save as {name} failed: {ex.Message}");
        }
    }

    /// <summary>Right-click on a FIP box: replace its gauge, add one to flip through, take a page off, how to flip.</summary>
    private ContextMenu BoxMenu(FipAssignment fip, int pageCount)
    {
        var serial = fip.Serial;
        var menu = new ContextMenu();
        MenuItem Item(string text, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = text, IsEnabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
        Item("Replace this gauge...", () => { if (PickGauge("Replace the gauge on FIP ..." + Short(serial)) is { } path) PutGauge(serial, Shown(serial), path); });
        Item("Add a gauge to flip through...", () => { if (PickGauge("Add a gauge to FIP ..." + Short(serial)) is { } path) AddGaugePage(serial, path); }, pageCount < 16);
        Item("Take this page off", () => RemovePage(serial, Shown(serial)), pageCount > 1);
        menu.Items.Add(new Separator());
        var knob = Item("Flip pages with the left knob (instead of the page buttons)", () =>
        {
            var i = Index(serial);
            if (i < 0) return;
            _fips[i] = _fips[i] with { LeftKnobPages = !_fips[i].LeftKnobPages };
            SyncFipSettings();
            Changed(_fips[i].LeftKnobPages
                ? $"FIP ...{Short(serial)}: the left knob flips its pages (only the right knob works on the gauge)."
                : $"FIP ...{Short(serial)}: the page up/down buttons flip its pages (both knobs work on the gauge).");
        });
        knob.IsCheckable = true;
        knob.IsChecked = fip.LeftKnobPages;

        // Flipping pages by itself (FIPinator Live does the flipping; FIPs with more than one page).
        var cycle = new MenuItem { Header = "Flip pages by itself" };
        foreach (var seconds in new[] { 0, 3, 5, 10, 20, 30 })
        {
            var item = new MenuItem { Header = seconds == 0 ? "Off" : $"Every {seconds} seconds", IsCheckable = true, IsChecked = fip.AutoFlipSeconds == seconds };
            item.Click += (_, _) => SetAutoFlip(serial, seconds);
            cycle.Items.Add(item);
        }
        cycle.Items.Add(new Separator());
        var all = new MenuItem { Header = "Use this for every FIP with pages" };
        all.Click += (_, _) => SetAutoFlipForAll(_fips[Index(serial)].AutoFlipSeconds);
        cycle.Items.Add(all);
        menu.Items.Add(cycle);

        if (_layouts.Count > 1)
        {
            menu.Items.Add(new Separator());
            var isSwitch = _layoutButton is { } b && b.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase);
            var sw = Item(isSwitch ? $"This FIP's {ButtonLabel(_layoutButton!.Button)} switches layouts" : "Switch layouts with this FIP's S6 button", () =>
            {
                _layoutButton = isSwitch ? null : new LayoutButton { Serial = serial, Button = _layoutButton?.Button ?? "button6" };
                FillLayoutControls();
                Changed(_layoutButton is null ? "No FIP button switches layouts now." : $"FIP ...{Short(serial)}'s {ButtonLabel(_layoutButton.Button)} switches layouts.");
            });
            sw.IsCheckable = true;
            sw.IsChecked = isSwitch;
        }
        return menu;
    }

    /// <summary>A small window with the full gauge list (search, picture): the chosen gauge's path, or null.</summary>
    private string? PickGauge(string title)
    {
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        var list = new ListBox { DisplayMemberPath = "Text", Height = 360, Width = 340, ItemsSource = _choices };
        var picture = new Image { Width = 320, Height = 240, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var ok = new Button { Content = "Use this gauge", IsDefault = true, IsEnabled = false, Style = (Style)FindResource("AccentButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        search.TextChanged += (_, _) =>
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.ItemsSource = _choices.Where(c => words.All(w => c.Search.Contains(w, StringComparison.CurrentCultureIgnoreCase))).ToList();
        };
        list.SelectionChanged += (_, _) => { picture.Source = list.SelectedItem is Choice c ? Picture(c.Path) : null; ok.IsEnabled = list.SelectedItem is Choice; };

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Type to search (e.g. fuel flow, horizon, 727):", Margin = new Thickness(0, 0, 0, 4) });
        left.Children.Add(search);
        left.Children.Add(list);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(left);
        row.Children.Add(picture);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(row);
        stack.Children.Add(buttons);
        var dialog = new Window
        {
            Title = title, Content = stack, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            Owner = _window, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is Choice) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => search.Focus();
        return dialog.ShowDialog() == true && list.SelectedItem is Choice chosen ? chosen.Path : null;
    }

    private string? AskName(string suggestion) => AskName(suggestion, "Name for the new panel (e.g. DC-6 1):", "Save as a new panel", "Save");

    private string? AskName(string suggestion, string question, string title, string okText)
    {
        var box = new TextBox { Text = suggestion, Margin = new Thickness(0, 8, 0, 12), MinWidth = 300 };
        var ok = new Button { Content = okText, IsDefault = true, Style = (Style)FindResource("AccentButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = question });
        stack.Children.Add(box);
        stack.Children.Add(buttons);
        var dialog = new Window
        {
            Title = title, Content = stack, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            Owner = _window, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }

    // ---------- Layouts ----------

    /// <summary>The panel as it is now: every layout and the layout button, gauge paths passed through <paramref name="path"/>.</summary>
    private PanelSettings ToSettings(PanelSettings basis, Func<FipAssignment, FipAssignment> path) => basis with
    {
        Fips = [.. _layouts[0].Fips.Select(path)],
        LayoutName = _layouts.Count > 1 ? _layouts[0].Name : basis.LayoutName,
        Layouts = [.. _layouts.Skip(1).Select(l => new PanelLayout { Name = l.Name, Fips = [.. l.Fips.Select(path)] })],
        LayoutButton = _layouts.Count > 1 ? _layoutButton : null,
    };

    /// <summary>Desk place, left-knob flipping and flipping by itself are the FIP's: the same in every layout.</summary>
    private void SyncFipSettings()
    {
        foreach (var fip in _fips)
            for (var l = 0; l < _layouts.Count; l++)
            {
                var list = _layouts[l].Fips;
                var i = list.FindIndex(f => f.Serial.Equals(fip.Serial, StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && !ReferenceEquals(list, _fips))
                    list[i] = list[i] with { DeskX = fip.DeskX, DeskY = fip.DeskY, LeftKnobPages = fip.LeftKnobPages, AutoFlipSeconds = fip.AutoFlipSeconds };
            }
    }

    private void FillLayoutControls()
    {
        _fillingLayoutControls = true;
        Layouts.ItemsSource = _layouts.Select(l => l.Name).ToList();
        Layouts.SelectedIndex = _layoutIndex;
        RenameLayout.IsEnabled = true;
        DeleteLayout.IsEnabled = _layouts.Count > 1 && _layoutIndex > 0;
        SwitchRow.Visibility = _layouts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SwitchFip.ItemsSource = new[] { "No button" }.Concat(_fips.Select(f => $"FIP ...{Short(f.Serial)}")).ToList();
        SwitchFip.SelectedIndex = _layoutButton is { } b ? Math.Max(0, _fips.FindIndex(f => f.Serial.Equals(b.Serial, StringComparison.OrdinalIgnoreCase)) + 1) : 0;
        SwitchButton.ItemsSource = LayoutButton.Buttons.Select(ButtonLabel).ToList();
        SwitchButton.SelectedIndex = _layoutButton is { } c ? Math.Max(0, LayoutButton.Buttons.ToList().IndexOf(c.Button)) : 5;
        SwitchButton.IsEnabled = SwitchFip.SelectedIndex > 0;
        _fillingLayoutControls = false;
    }

    private static string ButtonLabel(string button) => "S" + button[^1..];

    private void LayoutButtonChanged()
    {
        if (_fillingLayoutControls) return;
        _layoutButton = SwitchFip.SelectedIndex > 0
            ? new LayoutButton { Serial = _fips[SwitchFip.SelectedIndex - 1].Serial, Button = LayoutButton.Buttons[Math.Max(0, SwitchButton.SelectedIndex)] }
            : null;
        SwitchButton.IsEnabled = _layoutButton is not null;
        Changed(_layoutButton is null
            ? "No FIP button switches layouts now: FIPinator Live shows the first layout."
            : $"FIP ...{Short(_layoutButton.Serial)}'s {ButtonLabel(_layoutButton.Button)} button switches every FIP to the next layout (that button does nothing else on that FIP).");
    }

    private void ShowLayout(int index)
    {
        _layoutIndex = index;
        _fips = _layouts[index].Fips;
        _shownPage.Clear();
        FillLayoutControls();
        DrawDesk();
        Status.Text = $"Showing layout \"{_layouts[index].Name}\". Changes to gauges here only affect this layout." + (_changed ? " Save the panel to keep your changes." : "");
    }

    private void AddLayout()
    {
        if (_settings is null) return;
        var name = AskName($"Layout {_layouts.Count + 1}", "Name for the new layout (e.g. Engines). It starts as a copy of the one you're looking at:", "New layout", "Add");
        if (name is null) return;
        if (_layouts.Any(l => l.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) { Status.Text = $"There's already a layout called \"{name}\"."; return; }
        if (_layouts.Count == 1 && _layouts[0].Name == "Layout 1")
        {
            var first = AskName("Main", "And a name for the layout you have now (e.g. Nav):", "Name the first layout", "OK");
            if (first is not null) _layouts[0] = (first, _layouts[0].Fips);
        }
        _layouts.Add((name, [.. _fips.Select(f => f with { Pages = [.. f.Pages] })]));
        if (_layoutButton is null && _fips.Count > 0) _layoutButton = new LayoutButton { Serial = _fips[0].Serial, Button = "button6" };
        ShowLayout(_layouts.Count - 1);
        Changed($"Layout \"{name}\" added as a copy. Change its gauges, then save. In FIPinator Live, FIP ...{Short(_layoutButton!.Serial)}'s {ButtonLabel(_layoutButton.Button)} switches every FIP to the next layout (change which button below the desk).");
    }

    private void RenameCurrentLayout()
    {
        var name = AskName(_layouts[_layoutIndex].Name, "New name for this layout:", "Rename layout", "Rename");
        if (name is null || name == _layouts[_layoutIndex].Name) return;
        if (_layouts.Any(l => l.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) { Status.Text = $"There's already a layout called \"{name}\"."; return; }
        _layouts[_layoutIndex] = (name, _layouts[_layoutIndex].Fips);
        FillLayoutControls();
        Changed($"Layout renamed to \"{name}\".");
    }

    private void DeleteCurrentLayout()
    {
        if (_layoutIndex == 0 || _layouts.Count < 2) return;
        var name = _layouts[_layoutIndex].Name;
        if (MessageBox.Show(_window, $"Remove the layout \"{name}\" from this panel? (Undo all changes brings it back until you save.)", "Remove layout",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _layouts.RemoveAt(_layoutIndex);
        ShowLayout(Math.Min(_layoutIndex, _layouts.Count - 1));
        Changed($"Layout \"{name}\" removed.");
    }

    private void SetAutoFlip(string serial, int seconds)
    {
        var i = Index(serial);
        if (i < 0) return;
        _fips[i] = _fips[i] with { AutoFlipSeconds = seconds };
        SyncFipSettings();
        Changed(seconds == 0 ? $"FIP ...{Short(serial)} doesn't flip pages by itself." : $"FIP ...{Short(serial)} flips to its next page every {seconds} seconds in FIPinator Live" + (PagesOf(_fips[i]).Count > 1 ? "." : " (add a second page for it to flip to)."));
    }

    private void SetAutoFlipForAll(int seconds)
    {
        for (var i = 0; i < _fips.Count; i++)
            if (_layouts.Any(l => PagesOf(l.Fips.First(f => f.Serial.Equals(_fips[i].Serial, StringComparison.OrdinalIgnoreCase))).Count > 1))
                _fips[i] = _fips[i] with { AutoFlipSeconds = seconds };
        SyncFipSettings();
        Changed(seconds == 0 ? "No FIP flips pages by itself now." : $"Every FIP with more than one page flips every {seconds} seconds.");
    }

    // ---------- Helpers ----------

    private int Index(string serial) => _fips.FindIndex(f => f.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));

    private int Shown(string serial)
    {
        var i = Index(serial);
        var count = i < 0 ? 1 : PagesOf(_fips[i]).Count;
        return Math.Clamp(_shownPage.GetValueOrDefault(serial), 0, count - 1);
    }

    /// <summary>A FIP's gauges in page order, as written in the panel file.</summary>
    private static List<string> PagesOf(FipAssignment fip) => [fip.Gauge, .. fip.Pages.Where(p => !string.IsNullOrWhiteSpace(p))];

    private void SetPages(int i, List<string> pages) => _fips[i] = _fips[i] with { Gauge = pages[0], Pages = pages.Skip(1).ToList() };

    private string Full(string path) => Path.GetFullPath(Path.Combine(Folder, path));

    private static string Short(string serial) => serial.Length > 4 ? serial[^4..] : serial;

    private static string NameOf(string path)
    {
        try { return GaugeDefinition.Load(path).Name; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException) { return Path.GetFileNameWithoutExtension(path) + " (can't be read)"; }
    }

    private BitmapSource? Picture(string path)
    {
        var key = Path.GetFullPath(path);
        if (_pictures.TryGetValue(key, out var cached)) return cached;
        BitmapSource? picture = null;
        try
        {
            using var renderer = GaugeRenderer.FromFile(key);
            using var bitmap = renderer.Render();
            picture = GaugeImages.ToImage(bitmap);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Gauge Maker: can't draw {key}: {ex.Message}");
        }
        _pictures[key] = picture;
        return picture;
    }
}
