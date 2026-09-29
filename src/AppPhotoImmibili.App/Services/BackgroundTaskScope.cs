namespace AppPhotoImmibili.App.Services;

/// <summary>
/// Chiede al sistema di completare il caricamento in corso anche se l'agente chiude l'app.
/// iOS concede circa 30 secondi con <c>BeginBackgroundTask</c>; su Android il processo resta attivo
/// in secondo piano e il caricamento riprende comunque alla successiva apertura.
/// </summary>
public sealed class BackgroundTaskScope : IDisposable
{
#if IOS
    private nint _taskId;

    private BackgroundTaskScope(string name)
    {
        _taskId = UIKit.UIApplication.SharedApplication.BeginBackgroundTask(name, Dispose);
    }

    public void Dispose()
    {
        var id = Interlocked.Exchange(ref _taskId, UIKit.UIApplication.BackgroundTaskInvalid);
        if (id != UIKit.UIApplication.BackgroundTaskInvalid)
            UIKit.UIApplication.SharedApplication.EndBackgroundTask(id);
    }
#else
    private BackgroundTaskScope(string name) { }

    public void Dispose() { }
#endif

    public static BackgroundTaskScope Begin(string name) => new(name);
}
