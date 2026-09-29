using AppPhotoImmibili.Core.Catalog;
using AppPhotoImmibili.Core.Data;
using AppPhotoImmibili.Core.Sync;

namespace AppPhotoImmibili.App.Services;

/// <summary>
/// Carica in secondo piano le foto confermate sul gestionale dell'agenzia, senza bloccare l'agente durante la visita.
/// Riparte dopo ogni scatto, quando torna la rete e comunque ogni pochi minuti; la coda è nel database locale,
/// quindi sopravvive alla chiusura dell'app.
/// </summary>
public sealed class BackgroundSyncService(PropertyRepository repository, PhotoCatalog catalog, AppSettings settings)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _running = new(1, 1);
    private Task? _loop;

    public string Status { get; private set; } = "";

    /// <summary>Sollevato dopo ogni passaggio (anche da thread in background).</summary>
    public event EventHandler? StatusChanged;

    public void Start()
    {
        if (_loop is not null) return;
        Connectivity.Current.ConnectivityChanged += (_, _) => Trigger();
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>Chiede un passaggio appena possibile (es. dopo aver salvato una foto).</summary>
    public void Trigger()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { /* già richiesto */ }
        }
    }

    private async Task LoopAsync()
    {
        while (true)
        {
            await _signal.WaitAsync(Interval);
            try
            {
                await RunAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sincronizzazione non riuscita: {ex}");
            }
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!await _running.WaitAsync(0, ct)) return;
        try
        {
            if (!settings.SyncEnabled)
            {
                await UpdateStatusAsync(null);
                return;
            }
            var options = new AgencyUploaderOptions { BaseUrl = settings.AgencyBaseUrl, Token = await settings.GetAgencyTokenAsync() };
            if (!options.IsConfigured)
            {
                await UpdateStatusAsync("Indica l'indirizzo https del gestionale nelle impostazioni.");
                return;
            }
            if (!CanUpload(out var reason))
            {
                await UpdateStatusAsync(reason);
                return;
            }

            using var scope = BackgroundTaskScope.Begin("caricamento-foto");
            var engine = new SyncEngine(repository, catalog, new AgencyUploader(Http, options)) { Agent = settings.AgentName };
            string? lastError = null;
            while (true)
            {
                var result = await engine.RunOnceAsync(ct);
                if (result.Postponed > 0 || result.Failed > 0)
                    lastError = await repository.GetLastSyncErrorAsync();
                if (result.Uploaded == 0 || result.Postponed > 0) break;
                await UpdateStatusAsync(null);
            }
            await UpdateStatusAsync(lastError);
        }
        finally
        {
            _running.Release();
        }
    }

    private bool CanUpload(out string reason)
    {
        reason = "";
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            reason = "In attesa di connessione: le foto restano in coda.";
            return false;
        }
        if (settings.SyncWifiOnly && !Connectivity.Current.ConnectionProfiles.Any(p => p is ConnectionProfile.WiFi or ConnectionProfile.Ethernet))
        {
            reason = "In attesa del Wi-Fi: le foto restano in coda.";
            return false;
        }
        return true;
    }

    private async Task UpdateStatusAsync(string? note)
    {
        if (!settings.SyncEnabled)
        {
            Status = "";
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var (queued, uploaded, failed) = await repository.GetSyncSummaryAsync();
        var parts = new List<string>();
        if (queued > 0) parts.Add($"{queued} in coda");
        if (uploaded > 0) parts.Add($"{uploaded} caricate");
        if (failed > 0) parts.Add($"{failed} con errore");
        var counts = parts.Count == 0 ? "Nessuna foto da caricare." : "Foto: " + string.Join(" · ", parts) + ".";
        Status = string.IsNullOrWhiteSpace(note) ? counts : $"{counts} {note}";
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task RefreshStatusAsync() => UpdateStatusAsync(null);

    /// <summary>Rimette in coda le foto in errore (dopo aver corretto indirizzo o token) e riparte.</summary>
    public async Task RetryAsync()
    {
        await repository.RetrySyncAsync();
        Trigger();
    }
}
