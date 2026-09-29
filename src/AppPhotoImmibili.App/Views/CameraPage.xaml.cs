using CommunityToolkit.Maui.Core;
using AppPhotoImmibili.App.ViewModels;
using AppPhotoImmibili.Core.Analysis;

namespace AppPhotoImmibili.App.Views;

public partial class CameraPage : ContentPage
{
    private readonly CameraViewModel _vm;
    private CancellationTokenSource? _pageCts;
    private bool _capturing;

    public CameraPage(CameraViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        OverlayView.Drawable = vm.Overlay;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _pageCts = new CancellationTokenSource();
        DeviceDisplay.Current.KeepScreenOn = true;

        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Fotocamera", "Senza il permesso della fotocamera non è possibile scattare.", "OK");
            await Shell.Current.GoToAsync("..");
            return;
        }

        await _vm.LoadAsync();
        _vm.Sensors.AttitudeChanged += OnAttitude;
        _vm.Sensors.HeightChanged += OnHeight;
        _vm.Sensors.Start();

        try
        {
            await SelectWidestLensAsync(_pageCts.Token);
            await Camera.StartCameraPreview(_pageCts.Token);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Fotocamera non disponibile", ex.Message, "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pageCts?.Cancel();
        DeviceDisplay.Current.KeepScreenOn = false;
        _vm.Sensors.AttitudeChanged -= OnAttitude;
        _vm.Sensors.HeightChanged -= OnHeight;
        _vm.Sensors.Stop();
        Camera.StopCameraPreview();
        _vm.DiscardPending();
    }

    /// <summary>Negli interni serve il campo più ampio: usa lo zoom minimo (ultra-grandangolo se disponibile).</summary>
    private async Task SelectWidestLensAsync(CancellationToken ct)
    {
        var cameras = await Camera.GetAvailableCameras(ct);
        var rear = cameras.FirstOrDefault(c => c.Position == CameraPosition.Rear);
        if (rear is null) return;
        Camera.SelectedCamera = rear;

        var baseFov = _vm.Settings.HorizontalFovDegrees;
        if (_vm.Settings.UseWidestLens && rear.MinimumZoomFactor < 1)
        {
            Camera.ZoomFactor = rear.MinimumZoomFactor;
            // Con zoom < 1 il campo visivo si allarga: tan(fov/2) scala come 1/zoom.
            var fov = 2 * Math.Atan(Math.Tan(baseFov * Math.PI / 360) / rear.MinimumZoomFactor) * 180 / Math.PI;
            _vm.Overlay.HorizontalFovDegrees = fov;
        }
        else
        {
            Camera.ZoomFactor = Math.Max(1, rear.MinimumZoomFactor);
        }
    }

    private void OnAttitude(object? sender, DeviceAttitude a) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _vm.UpdateAttitude(a);
            OverlayView.Invalidate();
        });

    private void OnHeight(object? sender, double? h) =>
        MainThread.BeginInvokeOnMainThread(() => _vm.UpdateHeight(h));

    private async void OnShutterTapped(object? sender, TappedEventArgs e)
    {
        if (_capturing || _vm.IsReviewing || _pageCts is null) return;
        _capturing = true;
        try
        {
            // Se non è in bolla attende fino a 4 s che lo diventi: evita la maggior parte degli scatti storti.
            var deadline = DateTime.UtcNow.AddSeconds(4);
            if (!_vm.Overlay.Attitude.IsLevel(_vm.Settings.LevelToleranceDegrees))
            {
                _vm.StatusText = "Attendo la bolla…";
                while (DateTime.UtcNow < deadline && !_vm.Overlay.Attitude.IsLevel(_vm.Settings.LevelToleranceDegrees))
                    await Task.Delay(50, _pageCts.Token);
                _vm.StatusText = "";
            }

            await Shutter.ScaleToAsync(0.85, 60);
            var context = _vm.BuildContext();
            var stream = await Camera.CaptureImage(_pageCts.Token);
            await Shutter.ScaleToAsync(1, 60);
            await _vm.ReviewAsync(stream, context);
        }
        catch (OperationCanceledException)
        {
            // pagina chiusa
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Scatto non riuscito", ex.Message, "OK");
        }
        finally
        {
            _capturing = false;
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
