// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Fipinator.Core;
using Fipinator.Sim;

namespace Fipinator.GaugeMaker;

/// <summary>
/// "For the plane you're flying": reads the aircraft's design speeds from the sim (read-only),
/// builds an airspeed gauge from them, shows it live, and saves it as the user's own gauge.
/// Works without the sim too: type the speeds in by hand.
/// </summary>
public partial class AirspeedPage : UserControl, IDisposable
{
    // Order matters: the first four are the speeds, the last is the live needle.
    private static readonly SimVariable[] Variables =
    [
        new("DESIGN SPEED VS0", "knots"),
        new("DESIGN SPEED VS1", "knots"),
        new("DESIGN SPEED VC", "knots"),
        new("AIRSPEED BARBER POLE", "knots"),
        new("AIRSPEED INDICATED", "knots"),
    ];

    private readonly MainWindow _window;
    private readonly SimConnection? _sim;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private GaugeRenderer? _renderer;
    private string? _json;
    private bool _filledFromSim, _filling;
    private double? _lastIas;
    private SimState? _lastState;

    public AirspeedPage(MainWindow window)
    {
        InitializeComponent();
        RefPicture.Attach(Reference);
        _window = window;

        Back.Click += (_, _) => window.ShowStart();
        Save.Click += (_, _) => SaveGauge();
        ReadAgain.Click += (_, _) => { _filledFromSim = false; Tick(); };
        foreach (var box in new[] { Vs0, Vfe, Vs1, Vc, Vne, Vmc, Vyse })
            box.TextChanged += (_, _) => { if (!_filling) Rebuild(); };

        try
        {
            _sim = new SimConnection(Variables, "FIPinator 3000");
            _sim.Start();
        }
        catch (Exception ex)
        {
            Log.Warn($"Gauge Maker: couldn't start the sim connection: {ex.Message}");
        }

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
        Rebuild();
    }

    private void Tick()
    {
        var state = _sim?.State ?? SimState.Failed;
        var values = _sim?.GetValues() ?? new double?[Variables.Length];
        var speeds = values[..4];
        var haveSpeeds = state == SimState.Connected && speeds[1] > 0 && speeds[3] > 0;

        if (state != _lastState || (haveSpeeds && !_filledFromSim))
        {
            if (state != _lastState) Log.Info($"Gauge Maker: airspeed page, sim {state} ({_sim?.StatusMessage}).");
            _lastState = state;
            (string glyph, string brush, string text) = state switch
            {
                SimState.Connected when haveSpeeds => ("", "SystemFillColorSuccessBrush", "Connected to the sim. Speeds read from your aircraft."),
                SimState.Connected => ("", "SystemFillColorCautionBrush", "Connected to the sim, waiting for the aircraft's speeds..."),
                SimState.Waiting => ("", "SystemFillColorCautionBrush", "The sim isn't running (or is still loading). Start a flight to fill in the speeds, or type them in yourself."),
                _ => ("", "SystemFillColorCriticalBrush", "Can't reach the sim from this PC (SimConnect wasn't found). You can still type the speeds in yourself."),
            };
            SimIcon.Text = glyph;
            SimIcon.Foreground = (Brush)FindResource(brush);
            SimStatus.Text = text;
        }
        ReadAgain.IsEnabled = haveSpeeds;

        if (haveSpeeds && !_filledFromSim)
        {
            _filledFromSim = true;
            _filling = true;
            Vs0.Text = Format(speeds[0]);
            Vs1.Text = Format(speeds[1]);
            Vc.Text = Format(speeds[2]);
            Vne.Text = Format(speeds[3]);
            VcHint.Visibility = Visibility.Visible;
            _filling = false;
            Log.Info($"Gauge Maker: aircraft speeds from the sim: VS0 {Vs0.Text}, VS1 {Vs1.Text}, VC {Vc.Text}, barber pole {Vne.Text} kt.");
            Rebuild();
        }

        var ias = state == SimState.Connected ? values[4] : null;
        if (ias != _lastIas)
        {
            _lastIas = ias;
            Draw();
        }
    }

    private static string Format(double? v) => v is > 0 ? Math.Round(v.Value).ToString(CultureInfo.CurrentCulture) : "";

    private static double Read(TextBox box) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ||
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? Math.Max(0, v) : 0;

    /// <summary>Builds the gauge from the numbers in the boxes.</summary>
    private void Rebuild()
    {
        var speeds = new AircraftSpeeds(Read(Vs0), Read(Vs1), Read(Vc), Read(Vne), Read(Vfe), Read(Vmc), Read(Vyse));
        if (!speeds.Usable)
        {
            Error.Text = "Enter at least the clean stall speed and the never-exceed speed (the never-exceed speed must be the higher one).";
            Save.IsEnabled = false;
            return;
        }
        Error.Text = speeds.StallFlapsDown > 0 && speeds.FlapsExtended <= speeds.StallFlapsDown
            ? "No white arc: the top flap speed isn't known. Look it up in the aircraft's handbook if you'd like one."
            : "";

        try
        {
            _json = AirspeedGenerator.Create(speeds, NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "Airspeed");
            var renderer = new GaugeRenderer(GaugeDefinition.Parse(_json), AppContext.BaseDirectory);
            _renderer?.Dispose();
            _renderer = renderer;
            Save.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Gauge Maker: airspeed gauge build failed: {ex}");
            Error.Text = "Something went wrong drawing this gauge. Details are in the log.";
            Save.IsEnabled = false;
            return;
        }
        Draw();
    }

    private void Draw()
    {
        if (_renderer is null) return;
        var values = _lastIas is { } ias ? new Dictionary<string, double> { ["ias"] = ias } : null;
        using var bitmap = values is null ? _renderer.Render() : _renderer.Render(values);
        Preview.Source = GaugeImages.ToImage(bitmap);
        Live.Text = _lastIas is { } v
            ? $"Live from the sim: {Math.Round(v)} knots. Check the needle matches your cockpit."
            : "The needle shows a sample speed. With the sim running it follows your aircraft.";
    }

    private void SaveGauge()
    {
        SaveError.Text = "";
        Rebuild(); // make sure the saved file has the current name and numbers
        if (_json is null) return;
        try
        {
            var path = GaugeFiles.SaveNew(_json, NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "Airspeed");
            Log.Info($"Gauge Maker: saved airspeed gauge {path}.");
            _window.OpenEditor(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Gauge Maker: couldn't save the airspeed gauge: {ex.Message}");
            SaveError.Text = $"Couldn't save in {GaugeFiles.MyGaugesFolder}: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _sim?.Dispose();
        _renderer?.Dispose();
    }
}
