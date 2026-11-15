// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fipinator.Core;
using Microsoft.Win32;

namespace Fipinator.GaugeMaker;

/// <summary>
/// The "Reference picture" panel: take a snapshot of the sim or open a picture, then see it over the
/// gauge preview, faded, to line the gauge up. Drag to move, scroll to zoom. The picture lives in memory
/// for this session only (shared between pages); nothing is written to disk.
/// </summary>
public partial class ReferencePicture : UserControl
{
    private static BitmapSource? _picture;
    private static event Action? PictureChanged;

    private Image? _overlay;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _move = new();
    private Point? _dragFrom;

    public ReferencePicture()
    {
        InitializeComponent();
        Snapshot.Click += async (_, _) => await TakeSnapshot();
        Open.Click += (_, _) => OpenPicture();
        Clear.Click += (_, _) => SetPicture(null, "Picture cleared.");
        ResetFit.Click += (_, _) => Fit();
        Fade.ValueChanged += (_, _) => { if (_overlay is not null) _overlay.Opacity = Fade.Value; };
        Loaded += (_, _) => { PictureChanged += Show; Show(); };
        Unloaded += (_, _) => PictureChanged -= Show;
    }

    /// <summary>
    /// Puts the picture in <paramref name="overlay"/>, an Image lying on top of the gauge preview in the
    /// same grid cell (same size). The picture can be dragged and zoomed inside it.
    /// </summary>
    public void Attach(Image overlay)
    {
        _overlay = overlay;
        overlay.Stretch = Stretch.Uniform;
        overlay.RenderTransformOrigin = new Point(0.5, 0.5);
        overlay.RenderTransform = new TransformGroup { Children = { _scale, _move } };
        overlay.Cursor = Cursors.SizeAll;
        overlay.MouseLeftButtonDown += (_, e) => { _dragFrom = e.GetPosition(overlay.Parent as IInputElement); overlay.CaptureMouse(); };
        overlay.MouseLeftButtonUp += (_, _) => { _dragFrom = null; overlay.ReleaseMouseCapture(); };
        overlay.MouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from) return;
            var now = e.GetPosition(overlay.Parent as IInputElement);
            _move.X += now.X - from.X;
            _move.Y += now.Y - from.Y;
            _dragFrom = now;
        };
        overlay.MouseWheel += (_, e) =>
        {
            var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            _scale.ScaleX = _scale.ScaleY = Math.Clamp(_scale.ScaleX * factor, 0.2, 20);
            e.Handled = true;
        };
        Show();
    }

    private void Show()
    {
        Clear.IsEnabled = SeeThroughRow.IsEnabled = _picture is not null;
        if (_overlay is null) return;
        _overlay.Source = _picture;
        _overlay.Opacity = Fade.Value;
        _overlay.Visibility = _picture is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Fit()
    {
        _scale.ScaleX = _scale.ScaleY = 1;
        _move.X = _move.Y = 0;
    }

    private static void SetPicture(BitmapSource? picture, string message)
    {
        _picture = picture;
        PictureChanged?.Invoke();
        foreach (var panel in Application.Current.Windows.OfType<Window>().SelectMany(w => Find<ReferencePicture>(w)))
            panel.Status.Text = message;
    }

    private async Task TakeSnapshot()
    {
        var window = Window.GetWindow(this);
        Snapshot.IsEnabled = false;
        Status.Text = "Taking a snapshot of the sim in 3 seconds...";
        try
        {
            if (window is not null) window.WindowState = WindowState.Minimized; // out of the sim's way
            await Task.Delay(3000);
            var picture = ScreenCapture.CaptureSim(out var problem);
            if (window is not null) { window.WindowState = WindowState.Normal; window.Activate(); }
            if (picture is null)
            {
                Status.Text = problem;
                Log.Warn($"Reference picture: no snapshot: {problem}");
                return;
            }
            Fit();
            SetPicture(picture, $"Snapshot taken ({picture.PixelWidth} x {picture.PixelHeight}). Drag and scroll until the sim's gauge lines up with yours.");
            Log.Info($"Reference picture: sim snapshot {picture.PixelWidth}x{picture.PixelHeight} (kept in memory only).");
        }
        finally
        {
            if (window is not null && window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            Snapshot.IsEnabled = true;
        }
    }

    private void OpenPicture()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a reference picture",
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // read it now; the file isn't kept open
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            image.Freeze();
            Fit();
            SetPicture(image, $"Opened {Path.GetFileName(dialog.FileName)}. Drag and scroll until it lines up with your gauge.");
            Log.Info("Reference picture: opened a picture (kept in memory only).");
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            Status.Text = $"Couldn't open that picture: {ex.Message}";
            Log.Warn($"Reference picture: couldn't open a picture: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var deeper in Find<T>(child)) yield return deeper;
        }
    }
}
