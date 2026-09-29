using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AppPhotoImmibili.Core.Imaging;

namespace AppPhotoImmibili.Core.Sync;

public enum UploadStatus
{
    Ok,
    /// <summary>Errore di rete o del server: si riprova più tardi.</summary>
    Temporaneo,
    /// <summary>Richiesta rifiutata (autorizzazione, dati non validi): riprovare non serve.</summary>
    Definitivo,
}

/// <param name="StopQueue">L'errore vale per tutte le foto (es. token non valido): inutile proseguire con la coda.</param>
public sealed record UploadOutcome(UploadStatus Status, string Message = "", bool StopQueue = false);

public sealed record PhotoUpload(PhotoMetadata Metadata, string FileName, byte[] Jpeg);

public interface IPhotoUploader
{
    Task<UploadOutcome> UploadAsync(PhotoUpload upload, CancellationToken ct = default);
}

public sealed class AgencyUploaderOptions
{
    /// <summary>Indirizzo base del gestionale dell'agenzia, es. https://gestionale.agenzia.it/api.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Token di accesso dell'agente (Bearer).</summary>
    public string Token { get; set; } = "";

    public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// Caricamento sul server dell'agenzia: <c>POST {BaseUrl}/immobili/{codice}/foto</c> multipart con i campi
/// <c>metadati</c> (JSON, lo stesso scritto nell'EXIF) e <c>foto</c> (JPEG per il web).
/// L'intestazione <c>Idempotency-Key</c> è l'identificativo della foto: il server può ignorare i doppioni
/// (risposta 409 considerata come già caricata).
/// </summary>
public sealed class AgencyUploader(HttpClient http, AgencyUploaderOptions options) : IPhotoUploader
{
    public async Task<UploadOutcome> UploadAsync(PhotoUpload upload, CancellationToken ct = default)
    {
        if (!options.IsConfigured)
            return new(UploadStatus.Definitivo, "Server dell'agenzia non configurato (serve un indirizzo https).", StopQueue: true);

        var url = $"{options.BaseUrl.TrimEnd('/')}/immobili/{Uri.EscapeDataString(upload.Metadata.PropertyCode)}/foto";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(ExifWriter.ToJson(upload.Metadata), Encoding.UTF8, "application/json"), "metadati");
        var file = new ByteArrayContent(upload.Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "foto", upload.FileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", upload.Metadata.Uid);
        if (!string.IsNullOrWhiteSpace(options.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
                return new(UploadStatus.Ok);
            return Classify(response.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new(UploadStatus.Temporaneo, "Connessione non disponibile.");
        }
    }

    internal static UploadOutcome Classify(HttpStatusCode code) => (int)code switch
    {
        401 or 403 => new(UploadStatus.Definitivo, "Accesso negato: controlla il token nelle impostazioni.", StopQueue: true),
        404 => new(UploadStatus.Definitivo, "Immobile o indirizzo del server non trovato."),
        413 => new(UploadStatus.Definitivo, "Foto troppo grande per il server."),
        400 or 422 => new(UploadStatus.Definitivo, "Dati rifiutati dal server."),
        408 or 429 => new(UploadStatus.Temporaneo, "Server occupato: nuovo tentativo più tardi."),
        _ => new(UploadStatus.Temporaneo, $"Errore del server ({(int)code})."),
    };
}
