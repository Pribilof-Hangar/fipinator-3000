// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

/// <summary>
/// A colour button for the editor: shows the colour; a click opens cockpit colours to pick from, plus a box
/// for any colour as #RRGGBB (or #AARRGGBB, see-through). Optional colours (an outline) can be set to none.
/// </summary>
internal sealed class ColourButton : Button
{
    private static readonly (string Hex, string Name)[] Palette =
    [
        ("#F2F2F2", "White"), (GaugeParts.Ink, "Instrument white"), ("#EFE6CF", "Cream"), ("#B0B0B0", "Light grey"),
        ("#6E6E6E", "Grey"), ("#3A3A3A", "Dark grey"), ("#1E1E1E", "Dial black"), ("#000000", "Black"),
        (GaugeParts.Red, "Red"), (GaugeParts.Green, "Green"), (GaugeParts.Yellow, "Yellow"), ("#E8892B", "Orange"),
        ("#3C7DD9", "Blue"), ("#4CC2FF", "Light blue"), ("#D04AC8", "Magenta"), ("#8D908E", "Panel grey"),
    ];

    private readonly Action<string?> _picked;

    public ColourButton(string? hex, bool canBeNone, Action<string?> picked)
    {
        _picked = picked;
        MinWidth = 120;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Swatch(hex, 26, 16));
        row.Children.Add(new TextBlock { Text = hex ?? "None", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Content = row;
        Click += (_, _) => Open(hex, canBeNone);
    }

    private void Open(string? hex, bool canBeNone)
    {
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var grid = new UniformGrid { Columns = 8 };
        foreach (var (h, name) in Palette)
        {
            var b = new Button { Padding = new Thickness(3), Margin = new Thickness(2), ToolTip = $"{name} {h}", Content = Swatch(h, 22, 22) };
            b.Click += (_, _) => { popup.IsOpen = false; _picked(h); };
            grid.Children.Add(b);
        }
        var box = new TextBox { Text = hex ?? "#", Width = 110, Margin = new Thickness(0, 0, 6, 0) };
        var use = new Button { Content = "Use" };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        void UseTyped()
        {
            var text = box.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (!GaugeEditor.IsColour(text)) { error.Text = "Type a colour like #FFFFFF (white) or #D3362B (red)."; error.Visibility = Visibility.Visible; return; }
            popup.IsOpen = false;
            _picked(text.ToUpperInvariant());
        }
        use.Click += (_, _) => UseTyped();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) UseTyped(); };
        var typed = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 0, 0) };
        typed.Children.Add(box);
        typed.Children.Add(use);
        if (canBeNone)
        {
            var none = new Button { Content = "None", Margin = new Thickness(6, 0, 0, 0) };
            none.Click += (_, _) => { popup.IsOpen = false; _picked(null); };
            typed.Children.Add(none);
        }
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(typed);
        panel.Children.Add(error);
        popup.Child = new Border
        {
            Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? SystemColors.WindowBrush,
            BorderBrush = TryFindResource("CardStrokeColorDefaultBrush") as Brush ?? Brushes.Gray,
            Child = panel,
        };
        popup.IsOpen = true;
    }

    public static Border Swatch(string? hex, double w, double h)
    {
        Brush fill = Brushes.Transparent;
        try { if (hex is not null) fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { }
        return new Border { Width = w, Height = h, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Background = fill };
    }
}

/// <summary>Picks a sim value from the ones FIPinator's own gauges use (known to work in MSFS 2024), with a search box.</summary>
internal static class SimValuePicker
{
    public static (string Sim, string Unit)? Pick(Window owner, IReadOnlyList<(string Sim, string Unit, string Label)> known)
    {
        var window = new Window
        {
            Title = "What should it show?", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 520, Height = 560, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize,
        };
        var search = new TextBox { Margin = new Thickness(0, 8, 0, 8) };
        var list = new ListBox();
        var ok = new Button { Content = "Use this", Style = (Style)Application.Current.FindResource("AccentButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel" };
        (string, string)? result = null;

        void Fill()
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Items.Clear();
            foreach (var k in known)
                if (words.All(w => k.Label.Contains(w, StringComparison.OrdinalIgnoreCase) || k.Sim.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    list.Items.Add(new ListBoxItem { Content = k.Label, Tag = (k.Sim, k.Unit), ToolTip = $"{k.Sim} ({k.Unit})" });
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        void Choose()
        {
            if (list.SelectedItem is ListBoxItem { Tag: (string sim, string unit) }) { result = (sim, unit); window.Close(); }
        }
        search.TextChanged += (_, _) => Fill();
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter) Choose(); if (e.Key == Key.Down && list.Items.Count > 0) list.Focus(); };
        list.MouseDoubleClick += (_, _) => Choose();
        ok.Click += (_, _) => Choose();
        cancel.Click += (_, _) => window.Close();

        var panel = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "Values FIPinator's own gauges read from MSFS 2024. Type to search, for example \"fuel\" or \"rpm\".", TextWrapping = TextWrapping.Wrap });
        top.Children.Add(search);
        DockPanel.SetDock(top, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(top);
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        window.Content = panel;
        Fill();
        window.Loaded += (_, _) => search.Focus();
        window.ShowDialog();
        return result;
    }
}
