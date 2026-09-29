using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppPhotoImmibili.App.Services;

namespace AppPhotoImmibili.App.ViewModels;

public sealed record LensOption(string Name, double FovDegrees)
{
    public override string ToString() => Name;
}

public sealed record DistractorModeOption(string Name, DistractorMode Mode)
{
    public override string ToString() => Name;
}

public partial class SettingsViewModel(
    AppSettings settings,
    SensorService sensors,
    OnDeviceModelService onDeviceModel,
    BackgroundSyncService sync) : ObservableObject
{
    public IReadOnlyList<DistractorModeOption> DistractorModes { get; } =
    [
        new("Sul telefono e nel cloud", DistractorMode.Entrambi),
        new("Solo sul telefono (senza rete)", DistractorMode.SulTelefono),
        new("Solo nel cloud (Claude)", DistractorMode.Cloud),
    ];

    public IReadOnlyList<LensOption> Lenses { get; } =
    [
        new("Grandangolo principale (~26 mm, 69°)", 69),
        new("Grandangolo spinto (~24 mm, 74°)", 74),
        new("Ultra-grandangolo (~13 mm, 108°)", 108),
    ];

    [ObservableProperty] public partial string ApiKey { get; set; } = "";
    [ObservableProperty] public partial string ApiBaseUrl { get; set; } = "";
    [ObservableProperty] public partial bool CheckDistractors { get; set; }
    [ObservableProperty] public partial bool AutoPerspective { get; set; }
    [ObservableProperty] public partial bool AutoTone { get; set; }
    [ObservableProperty] public partial bool UseWidestLens { get; set; }
    [ObservableProperty] public partial bool ShowCornerMask { get; set; }
    [ObservableProperty] public partial bool IncludeOriginals { get; set; }
    [ObservableProperty] public partial double Tolerance { get; set; }
    [ObservableProperty] public partial double HeightMin { get; set; }
    [ObservableProperty] public partial double HeightMax { get; set; }
    [ObservableProperty] public partial LensOption? SelectedLens { get; set; }
    [ObservableProperty] public partial string CalibrationText { get; set; } = "";
    [ObservableProperty] public partial DistractorModeOption? SelectedDistractorMode { get; set; }
    [ObservableProperty] public partial string ModelText { get; set; } = "";
    [ObservableProperty] public partial bool HasModel { get; set; }
    [ObservableProperty] public partial bool RefineWithLines { get; set; }
    [ObservableProperty] public partial string AgentName { get; set; } = "";
    [ObservableProperty] public partial bool SyncEnabled { get; set; }
    [ObservableProperty] public partial bool SyncWifiOnly { get; set; }
    [ObservableProperty] public partial string AgencyBaseUrl { get; set; } = "";
    [ObservableProperty] public partial string AgencyToken { get; set; } = "";
    [ObservableProperty] public partial string SyncStatus { get; set; } = "";

    public string ToleranceText => $"Tolleranza livella: ±{Tolerance:0.0}°";
    public string HeightText => $"Altezza di scatto: {HeightMin:0}–{HeightMax:0} cm";

    partial void OnToleranceChanged(double value) => OnPropertyChanged(nameof(ToleranceText));
    partial void OnHeightMinChanged(double value) => OnPropertyChanged(nameof(HeightText));
    partial void OnHeightMaxChanged(double value) => OnPropertyChanged(nameof(HeightText));

    public async Task LoadAsync()
    {
        ApiKey = await settings.GetApiKeyAsync();
        ApiBaseUrl = settings.ApiBaseUrl;
        CheckDistractors = settings.CheckDistractors;
        AutoPerspective = settings.AutoPerspective;
        AutoTone = settings.AutoTone;
        UseWidestLens = settings.UseWidestLens;
        ShowCornerMask = settings.ShowCornerMask;
        IncludeOriginals = settings.IncludeOriginalsInExport;
        Tolerance = settings.LevelToleranceDegrees;
        HeightMin = settings.HeightMinCm;
        HeightMax = settings.HeightMaxCm;
        SelectedLens = Lenses.MinBy(l => Math.Abs(l.FovDegrees - settings.HorizontalFovDegrees));
        SelectedDistractorMode = DistractorModes.FirstOrDefault(m => m.Mode == settings.DistractorMode) ?? DistractorModes[0];
        RefineWithLines = settings.RefineWithLines;
        AgentName = settings.AgentName;
        SyncEnabled = settings.SyncEnabled;
        SyncWifiOnly = settings.SyncWifiOnly;
        AgencyBaseUrl = settings.AgencyBaseUrl;
        AgencyToken = await settings.GetAgencyTokenAsync();
        RefreshModel();
        await sync.RefreshStatusAsync();
        SyncStatus = sync.Status;
        CalibrationText = $"Segno {settings.AccelerometerSign:+0;-0}, correzione {settings.PitchOffset:+0.0;-0.0}° / {settings.RollOffset:+0.0;-0.0}°";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await settings.SetApiKeyAsync(ApiKey);
        settings.ApiBaseUrl = ApiBaseUrl;
        settings.CheckDistractors = CheckDistractors;
        settings.AutoPerspective = AutoPerspective;
        settings.AutoTone = AutoTone;
        settings.UseWidestLens = UseWidestLens;
        settings.ShowCornerMask = ShowCornerMask;
        settings.IncludeOriginalsInExport = IncludeOriginals;
        settings.LevelToleranceDegrees = Math.Round(Tolerance, 1);
        settings.HeightMinCm = Math.Round(Math.Min(HeightMin, HeightMax));
        settings.HeightMaxCm = Math.Round(Math.Max(HeightMin, HeightMax));
        if (SelectedLens is not null) settings.HorizontalFovDegrees = SelectedLens.FovDegrees;
        if (SelectedDistractorMode is not null) settings.DistractorMode = SelectedDistractorMode.Mode;
        settings.RefineWithLines = RefineWithLines;
        settings.AgentName = AgentName;
        settings.SyncWifiOnly = SyncWifiOnly;
        settings.AgencyBaseUrl = AgencyBaseUrl;
        await settings.SetAgencyTokenAsync(AgencyToken);
        if (SyncEnabled && !new Core.Sync.AgencyUploaderOptions { BaseUrl = AgencyBaseUrl }.IsConfigured)
        {
            SyncEnabled = false;
            await Shell.Current.DisplayAlertAsync("Caricamento", "Per attivare il caricamento serve un indirizzo che inizi con https://.", "OK");
        }
        settings.SyncEnabled = SyncEnabled;
        sync.Trigger();
        await Shell.Current.DisplayAlertAsync("Impostazioni", "Impostazioni salvate.", "OK");
    }

    private void RefreshModel()
    {
        HasModel = onDeviceModel.IsInstalled;
        ModelText = onDeviceModel.Description;
    }

    [RelayCommand]
    private async Task ImportModelAsync()
    {
        try
        {
            var message = await onDeviceModel.ImportAsync();
            if (message is not null) await Shell.Current.DisplayAlertAsync("Modello", message, "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Modello", ex.Message, "OK");
        }
        RefreshModel();
    }

    [RelayCommand]
    private async Task RemoveModelAsync()
    {
        if (!await Shell.Current.DisplayAlertAsync("Modello", "Rimuovere il modello dal telefono?", "Rimuovi", "Annulla")) return;
        await onDeviceModel.RemoveAsync();
        RefreshModel();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        SyncStatus = "Caricamento in corso…";
        await sync.RetryAsync();
        await sync.RunAsync();
        SyncStatus = sync.Status;
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        sensors.Start();
        try
        {
            if (!await Shell.Current.DisplayAlertAsync("Calibrazione 1/2",
                    "Appoggia il telefono su un tavolo, con lo schermo verso l'alto, e premi Avanti.", "Avanti", "Annulla"))
                return;
            await Task.Delay(800);
            if (!sensors.CalibrateSignFlat())
            {
                await Shell.Current.DisplayAlertAsync("Calibrazione", "Il telefono non sembra appoggiato in piano. Riprova.", "OK");
                return;
            }

            if (await Shell.Current.DisplayAlertAsync("Calibrazione 2/2",
                    "Ora appoggia il retro del telefono in orizzontale contro uno stipite o un muro ben verticale e premi Avanti.",
                    "Avanti", "Salta"))
            {
                await Task.Delay(800);
                if (!sensors.CalibrateZeroAgainstWall())
                    await Shell.Current.DisplayAlertAsync("Calibrazione", "Il telefono è troppo inclinato: appoggialo bene al muro e riprova.", "OK");
            }
            await LoadAsync();
            await Shell.Current.DisplayAlertAsync("Calibrazione", "Livella calibrata.", "OK");
        }
        finally
        {
            sensors.Stop();
        }
    }
}
