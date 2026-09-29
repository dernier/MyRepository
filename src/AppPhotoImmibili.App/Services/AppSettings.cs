namespace AppPhotoImmibili.App.Services;

/// <summary>Dove eseguire il controllo degli oggetti di disturbo.</summary>
public enum DistractorMode
{
    /// <summary>Solo sul telefono (ONNX Runtime): immediato e senza rete.</summary>
    SulTelefono,
    /// <summary>Solo nel cloud (Claude): riconosce anche disordine, tavoletta alzata, riflessi.</summary>
    Cloud,
    /// <summary>Prima sul telefono, poi nel cloud quando c'è rete.</summary>
    Entrambi,
}

/// <summary>Impostazioni dell'app. La chiave API resta nello storage sicuro del sistema operativo.</summary>
public sealed class AppSettings
{
    private const string ApiKeyName = "anthropic_api_key";
    private const string AgencyTokenName = "agency_token";

    public double LevelToleranceDegrees
    {
        get => Preferences.Get(nameof(LevelToleranceDegrees), 1.0);
        set => Preferences.Set(nameof(LevelToleranceDegrees), value);
    }

    /// <summary>Campo visivo orizzontale del grandangolo principale (lato lungo).</summary>
    public double HorizontalFovDegrees
    {
        get => Preferences.Get(nameof(HorizontalFovDegrees), 69.0);
        set => Preferences.Set(nameof(HorizontalFovDegrees), value);
    }

    public bool UseWidestLens
    {
        get => Preferences.Get(nameof(UseWidestLens), true);
        set => Preferences.Set(nameof(UseWidestLens), value);
    }

    public double HeightMinCm
    {
        get => Preferences.Get(nameof(HeightMinCm), 120.0);
        set => Preferences.Set(nameof(HeightMinCm), value);
    }

    public double HeightMaxCm
    {
        get => Preferences.Get(nameof(HeightMaxCm), 140.0);
        set => Preferences.Set(nameof(HeightMaxCm), value);
    }

    public bool AutoPerspective
    {
        get => Preferences.Get(nameof(AutoPerspective), true);
        set => Preferences.Set(nameof(AutoPerspective), value);
    }

    public bool AutoTone
    {
        get => Preferences.Get(nameof(AutoTone), true);
        set => Preferences.Set(nameof(AutoTone), value);
    }

    public bool CheckDistractors
    {
        get => Preferences.Get(nameof(CheckDistractors), true);
        set => Preferences.Set(nameof(CheckDistractors), value);
    }

    public DistractorMode DistractorMode
    {
        get => (DistractorMode)Preferences.Get(nameof(DistractorMode), (int)DistractorMode.Entrambi);
        set => Preferences.Set(nameof(DistractorMode), (int)value);
    }

    public bool UseOnDeviceDetection => CheckDistractors && DistractorMode != DistractorMode.Cloud;
    public bool UseCloudDetection => CheckDistractors && DistractorMode != DistractorMode.SulTelefono;

    /// <summary>Affina la correzione prospettica con le linee verticali dell'immagine.</summary>
    public bool RefineWithLines
    {
        get => Preferences.Get(nameof(RefineWithLines), true);
        set => Preferences.Set(nameof(RefineWithLines), value);
    }

    /// <summary>Nome dell'agente, scritto nell'EXIF (Artist) e inviato al gestionale.</summary>
    public string AgentName
    {
        get => Preferences.Get(nameof(AgentName), "");
        set => Preferences.Set(nameof(AgentName), value.Trim());
    }

    // Caricamento sul gestionale dell'agenzia
    public bool SyncEnabled
    {
        get => Preferences.Get(nameof(SyncEnabled), false);
        set => Preferences.Set(nameof(SyncEnabled), value);
    }

    public bool SyncWifiOnly
    {
        get => Preferences.Get(nameof(SyncWifiOnly), true);
        set => Preferences.Set(nameof(SyncWifiOnly), value);
    }

    public string AgencyBaseUrl
    {
        get => Preferences.Get(nameof(AgencyBaseUrl), "");
        set => Preferences.Set(nameof(AgencyBaseUrl), value.Trim());
    }

    public bool ShowCornerMask
    {
        get => Preferences.Get(nameof(ShowCornerMask), true);
        set => Preferences.Set(nameof(ShowCornerMask), value);
    }

    public bool IncludeOriginalsInExport
    {
        get => Preferences.Get(nameof(IncludeOriginalsInExport), false);
        set => Preferences.Set(nameof(IncludeOriginalsInExport), value);
    }

    /// <summary>Endpoint alternativo (proxy aziendale). Vuoto = API Anthropic diretta.</summary>
    public string ApiBaseUrl
    {
        get => Preferences.Get(nameof(ApiBaseUrl), "");
        set => Preferences.Set(nameof(ApiBaseUrl), value.Trim());
    }

    // Calibrazione della livella
    public double AccelerometerSign
    {
        get => Preferences.Get(nameof(AccelerometerSign), 1.0);
        set => Preferences.Set(nameof(AccelerometerSign), value);
    }

    public double PitchOffset
    {
        get => Preferences.Get(nameof(PitchOffset), 0.0);
        set => Preferences.Set(nameof(PitchOffset), value);
    }

    public double RollOffset
    {
        get => Preferences.Get(nameof(RollOffset), 0.0);
        set => Preferences.Set(nameof(RollOffset), value);
    }

    public async Task<string> GetApiKeyAsync()
    {
        try { return await SecureStorage.Default.GetAsync(ApiKeyName) ?? ""; }
        catch { return ""; }
    }

    public async Task SetApiKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) SecureStorage.Default.Remove(ApiKeyName);
        else await SecureStorage.Default.SetAsync(ApiKeyName, key.Trim());
    }

    public async Task<string> GetAgencyTokenAsync()
    {
        try { return await SecureStorage.Default.GetAsync(AgencyTokenName) ?? ""; }
        catch { return ""; }
    }

    public async Task SetAgencyTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) SecureStorage.Default.Remove(AgencyTokenName);
        else await SecureStorage.Default.SetAsync(AgencyTokenName, token.Trim());
    }
}
