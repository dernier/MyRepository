using AppPhotoImmibili.Core.Ai;
using Microsoft.ML.OnnxRuntime;

namespace AppPhotoImmibili.App.Services;

/// <summary>
/// Modello ONNX per il rilevamento degli oggetti di disturbo sul telefono.
/// Si cerca prima un modello importato dall'agente (cartella dati dell'app), poi uno incluso nel pacchetto
/// (<c>Resources/Raw/distrattori.onnx</c>). Senza modello il controllo sul telefono è semplicemente saltato.
/// </summary>
public sealed class OnDeviceModelService : IDisposable
{
    public const string FileName = "distrattori.onnx";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private OnDeviceDistractorDetector? _detector;
    private bool _packageChecked;

    public static string ModelPath => Path.Combine(FileSystem.AppDataDirectory, "Modelli", FileName);

    public bool IsInstalled => File.Exists(ModelPath);

    public string Description => _detector is { } d
        ? $"Modello attivo: {d.ClassNames.Count} classi riconosciute."
        : IsInstalled ? "Modello installato (caricato al primo scatto)." : "Nessun modello: il controllo sul telefono è disattivato.";

    /// <summary>Carica il modello alla prima richiesta. Restituisce null se non è disponibile o non è valido.</summary>
    public async Task<OnDeviceDistractorDetector?> GetAsync()
    {
        if (_detector is not null) return _detector;
        await _lock.WaitAsync();
        try
        {
            if (_detector is not null) return _detector;
            if (!IsInstalled && !_packageChecked)
            {
                _packageChecked = true;
                await TryCopyFromPackageAsync();
            }
            if (!IsInstalled) return null;
            _detector = await Task.Run(() => new OnDeviceDistractorDetector(ModelPath, new OnDeviceDetectorOptions
            {
                SessionOptions = CreateSessionOptions(),
            }));
            return _detector;
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or InvalidDataException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Modello ONNX non caricato: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Importa un modello scelto dall'agente (es. YOLO esportato in ONNX) e verifica che si carichi.</summary>
    public async Task<string?> ImportAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Scegli il modello .onnx" });
        if (file is null) return null;
        if (!file.FileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Scegli un file con estensione .onnx");

        var temp = ModelPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        await using (var src = await file.OpenReadAsync())
        await using (var dst = File.Create(temp))
            await src.CopyToAsync(dst);

        try
        {
            // Verifica: il modello deve caricarsi e avere un'uscita compatibile.
            using var test = new OnDeviceDistractorDetector(temp);
            using var probe = new SkiaSharp.SKBitmap(64, 64);
            test.Detect(probe);
        }
        catch (Exception ex)
        {
            File.Delete(temp);
            throw new InvalidDataException($"Modello non compatibile: {ex.Message}", ex);
        }

        await _lock.WaitAsync();
        try
        {
            _detector?.Dispose();
            _detector = null;
            File.Move(temp, ModelPath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
        return (await GetAsync())?.ClassNames.Count is { } n ? $"Modello importato: {n} classi." : "Modello importato.";
    }

    public async Task RemoveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _detector?.Dispose();
            _detector = null;
            if (IsInstalled) File.Delete(ModelPath);
            _packageChecked = true; // non ricopiare quello del pacchetto finché l'app è aperta
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task TryCopyFromPackageAsync()
    {
        try
        {
            await using var src = await FileSystem.OpenAppPackageFileAsync(FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            await using var dst = File.Create(ModelPath);
            await src.CopyToAsync(dst);
        }
        catch (Exception)
        {
            // Nessun modello incluso nel pacchetto (su Android l'errore è un'eccezione Java, non System.IO).
            try { File.Delete(ModelPath); } catch { /* copia parziale */ }
        }
    }

    /// <summary>Accelerazione hardware: NNAPI su Android, CoreML (Neural Engine) su iOS; in mancanza la CPU.</summary>
    private static SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        try
        {
#if ANDROID
            options.AppendExecutionProvider_Nnapi();
#elif IOS
            options.AppendExecutionProvider_CoreML();
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Accelerazione non disponibile, uso la CPU: {ex.Message}");
        }
        return options;
    }

    public void Dispose() => _detector?.Dispose();
}
