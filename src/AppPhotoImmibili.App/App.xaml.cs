using AppPhotoImmibili.App.Services;

namespace AppPhotoImmibili.App;

public partial class App : Application
{
    private readonly BackgroundSyncService _sync;

    public App(BackgroundSyncService sync)
    {
        _sync = sync;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        // La coda di caricamento riparte all'avvio e ogni volta che l'agente torna nell'app.
        window.Created += (_, _) => _sync.Start();
        window.Resumed += (_, _) => _sync.Trigger();
        return window;
    }
}
