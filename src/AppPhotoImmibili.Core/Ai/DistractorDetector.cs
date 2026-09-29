using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using AppPhotoImmibili.Core.Imaging;
using AppPhotoImmibili.Core.Models;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Ai;

/// <summary>Elemento di disturbo individuato nell'inquadratura.</summary>
public sealed record Distractor(
    [property: JsonPropertyName("elemento")] string Item,
    [property: JsonPropertyName("posizione")] string Position,
    [property: JsonPropertyName("gravita")] Severity Severity,
    [property: JsonPropertyName("azione")] string Action);

public sealed record DistractorReport(
    [property: JsonPropertyName("elementi")] IReadOnlyList<Distractor> Items,
    [property: JsonPropertyName("giudizio")] string Summary)
{
    public static DistractorReport Empty { get; } = new([], "");
    public bool HasBlocking => Items.Any(i => i.Severity >= Severity.Media);
}

public sealed class DistractorDetectorOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Endpoint alternativo (es. un proxy aziendale che custodisce la chiave). Vuoto = API Anthropic.</summary>
    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Lato lungo dell'immagine inviata: 1280 px bastano e riducono tempi e costi.</summary>
    public int MaxImageSide { get; set; } = 1280;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
}

/// <summary>
/// Segnala oggetti sgradevoli nell'inquadratura (tavoletta alzata, cavi a vista, disordine...) con Claude (visione).
/// Il controllo è nel cloud: senza connessione o chiave API l'app continua a funzionare senza questa verifica.
/// </summary>
public sealed class DistractorDetector
{
    private const string SystemPrompt = """
        Sei l'assistente di un agente immobiliare che fotografa gli interni di un immobile in vendita o in affitto.
        Ricevi una foto appena scattata e l'ambiente in cui è stata scattata.
        Individua solo gli elementi che un fotografo immobiliare professionista farebbe rimuovere o sistemare
        prima dello scatto, perché peggiorano l'annuncio. Esempi: tavoletta o coperchio del WC alzati, cavi elettrici
        o prolunghe a vista, disordine su ripiani, tavoli e piani di lavoro, flaconi e detersivi, asciugamani in
        disordine, spazzatura o bidoni, stendibiancheria e panni, scarpe, giocattoli sparsi, oggetti personali e
        foto di famiglia, animali o persone, riflesso del fotografo in specchi o vetri, luci spente, tende chiuse,
        letto disfatto, ante e cassetti aperti.
        Non segnalare arredi, elettrodomestici o complementi normalmente presenti e in ordine, né difetti strutturali
        dell'immobile. Se non c'è nulla da sistemare restituisci un elenco vuoto.
        Per ogni elemento indica la posizione nell'inquadratura (es. "in basso a sinistra") e un'azione breve e concreta.
        Gravità: Alta se salta all'occhio e va sistemato, Media se è consigliato sistemarlo, Bassa se è un dettaglio.
        Il giudizio è una frase breve sullo stato dell'inquadratura. Scrivi in italiano.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DistractorDetectorOptions _options;
    private readonly AnthropicClient _client;

    public DistractorDetector(DistractorDetectorOptions options)
    {
        _options = options;
        _client = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? new AnthropicClient { ApiKey = options.ApiKey, Timeout = options.Timeout }
            : new AnthropicClient { ApiKey = options.ApiKey, BaseUrl = options.BaseUrl, Timeout = options.Timeout };
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey) || !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<DistractorReport> AnalyzeAsync(SKBitmap photo, string roomName, CancellationToken ct = default)
    {
        using var small = ImageIo.Downscale(photo, _options.MaxImageSide);
        var base64 = Convert.ToBase64String(ImageIo.EncodeJpeg(small, 80));

        var response = await _client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 4096,
            // Se i filtri di sicurezza rifiutano la richiesta, l'API la ripete su un modello di riserva.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            // Compito di classificazione: effort basso per risposte rapide sul campo.
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Low,
                Format = new BetaJsonOutputFormat { Schema = Schema },
            },
            System = SystemPrompt,
            Messages =
            [
                new BetaMessageParam
                {
                    Role = Role.User,
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = base64, MediaType = MediaType.ImageJpeg } },
                        new BetaTextBlockParam { Text = $"Ambiente: {roomName}. Quali elementi vanno sistemati prima di riscattare?" },
                    },
                },
            ],
        }, ct);

        if (response.StopReason == "refusal")
            return new DistractorReport([], "Analisi non disponibile per questa foto.");

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        return Parse(json);
    }

    internal static DistractorReport Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return DistractorReport.Empty;
        return JsonSerializer.Deserialize<DistractorReport>(json, JsonOptions) ?? DistractorReport.Empty;
    }

    public static string Serialize(DistractorReport report) => JsonSerializer.Serialize(report, JsonOptions);

    /// <summary>Messaggio leggibile per errori di rete o di configurazione.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => "Chiave API non valida: controlla le impostazioni.",
        AnthropicRateLimitException => "Troppe richieste: riprova tra qualche secondo.",
        Anthropic5xxException => "Servizio di analisi momentaneamente non disponibile.",
        AnthropicApiException api => $"Errore del servizio di analisi ({api.Message}).",
        HttpRequestException or TaskCanceledException => "Nessuna connessione: il controllo oggetti verrà saltato.",
        _ => $"Analisi non riuscita: {ex.Message}",
    };

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "elementi", "giudizio" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            elementi = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "elemento", "posizione", "gravita", "azione" },
                    properties = new
                    {
                        elemento = new { type = "string" },
                        posizione = new { type = "string" },
                        gravita = new { type = "string", @enum = new[] { "Bassa", "Media", "Alta" } },
                        azione = new { type = "string" },
                    },
                },
            },
            giudizio = new { type = "string" },
        }),
    };
}
