// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Fipinator.Core;

namespace Fipinator.GaugeMaker;

public partial class App : Application
{
    private bool _showingError;
    private DateTime _lastErrorShown = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnexpectedError;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error($"Gauge Maker: unexpected error on a background thread: {args.ExceptionObject}");
        Log.Info($"Gauge Maker {typeof(App).Assembly.GetName().Version} started.");

        // Run by the installer: SPAD.neXt users get the SPAD versions of the built-in gauges, then it quits.
        if (e.Args.FirstOrDefault() == "--make-spad-gauges")
        {
            var code = 0;
            try
            {
                if (SpadExport.SpadInstalled())
                {
                    var saved = SpadExport.MakeReadyMade(GaugeFiles.TemplatesFolder);
                    Log.Info($"Gauge Maker: {saved} built-in gauges saved for SPAD.neXt in {SpadExport.ReadyMadeFolder}.");
                }
                else Log.Info("Gauge Maker: SPAD.neXt isn't on this PC; no SPAD gauges made.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Gauge Maker: SPAD gauges not made: {ex.Message}");
                code = 1;
            }
            Shutdown(code);
            return;
        }

        var window = new MainWindow();
        if (e.Args.FirstOrDefault() is { } file && File.Exists(file))
        {
            // Opened with a gauge file: straight to the editor, no opening screen.
            window.Show();
            window.OpenEditor(file);
            return;
        }
        // Started from FIPinator Live with a panel: open "Set up your panel" on it, after the opening screen.
        var panel = e.Args.Length >= 2 && e.Args[0] == "--panel" && File.Exists(e.Args[1]) ? e.Args[1] : null;
        // The opening screen first (a DC-6 writes the name in smoke), then the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new SplashWindow();
        splash.Closed += (_, _) =>
        {
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            window.Activate();
            if (panel is not null) window.ShowPanels(panel);
        };
        splash.Show();
    }

    /// <summary>
    /// Logs the error and tells the user once. Never opens a second message while one is showing:
    /// the message box keeps the screen redrawing, and an error that happens on every redraw used
    /// to open box after box inside each other until the program crashed (stack overflow).
    /// </summary>
    private void OnUnexpectedError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log.Error($"Gauge Maker: unexpected error: {e.Exception}");
        if (_showingError || DateTime.UtcNow - _lastErrorShown < TimeSpan.FromSeconds(30)) return;
        _showingError = true;
        try
        {
            MessageBox.Show("Something unexpected went wrong. FIPinator 3000 keeps running; details are in the log folder.",
                "FIPinator 3000", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _showingError = false;
            _lastErrorShown = DateTime.UtcNow;
        }
    }
}
