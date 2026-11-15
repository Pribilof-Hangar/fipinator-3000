// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Windows;

namespace Fipinator.GaugeMaker;

/// <summary>Hosts one page at a time: the start screen, the template picker, the editor....</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowStart();
    }

    public void ShowStart()
    {
        if (!LeaveCurrent()) return;
        Title = "FIPinator 3000";
        Page.Content = new StartPage(this);
    }

    public void ShowTemplates()
    {
        if (!LeaveCurrent()) return;
        Title = "FIPinator 3000 - Change how a gauge looks";
        Page.Content = new TemplatePage(this);
    }

    public void ShowAirspeed()
    {
        if (!LeaveCurrent()) return;
        Title = "FIPinator 3000 - Airspeed for your plane";
        Page.Content = new AirspeedPage(this);
    }

    public void ShowCombine()
    {
        if (!LeaveCurrent()) return;
        Title = "FIPinator 3000 - Combine gauges";
        Page.Content = new CombinePage(this);
    }

    /// <param name="panelPath">A panel file to pick (FIPinator Live's "Change this panel in FIPinator 3000"), or null for the one Live runs.</param>
    public void ShowPanels(string? panelPath = null)
    {
        if (!LeaveCurrent()) return;
        Title = "FIPinator 3000 - Set up your panel";
        Page.Content = new PanelPage(this, panelPath);
    }

    public void OpenEditor(string path)
    {
        if (!LeaveCurrent()) return;
        Page.Content = new EditorPage(this, path);
    }

    public void OpenQuickColours(string path)
    {
        if (!LeaveCurrent()) return;
        Title = "FIPinator 3000 - Quick colours";
        Page.Content = new QuickColoursPage(this, path);
    }

    /// <summary>Leaves the page shown now (the editor asks about unsaved changes first). False = stay.</summary>
    private bool LeaveCurrent()
    {
        if (Page.Content is EditorPage editor && !editor.ConfirmLeave()) return false;
        if (Page.Content is QuickColoursPage quick && !quick.ConfirmLeave()) return false;
        if (Page.Content is IDisposable d) d.Dispose();
        Page.Content = null;
        return true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Page.Content is EditorPage editor && !editor.ConfirmLeave()) e.Cancel = true;
        if (Page.Content is QuickColoursPage quick && !quick.ConfirmLeave()) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Page.Content is IDisposable d) d.Dispose();
        base.OnClosed(e);
    }
}
