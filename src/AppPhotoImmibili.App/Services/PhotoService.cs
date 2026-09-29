using AppPhotoImmibili.Core.Ai;
using AppPhotoImmibili.Core.Analysis;
using AppPhotoImmibili.Core.Catalog;
using AppPhotoImmibili.Core.Data;
using AppPhotoImmibili.Core.Imaging;
using AppPhotoImmibili.Core.Models;
using SkiaSharp;

namespace AppPhotoImmibili.App.Services;

/// <summary>Scatto in attesa di conferma dopo i controlli qualità.</summary>
public sealed class PendingShot : IDisposable
{
    public required string TempPath { get; init; }
    public required SKBitmap Bitmap { get; init; }
    public required CaptureContext Context { get; init; }
    public required Property Property { get; init; }
    public required Room Room { get; init; }
    public required ShotKind Kind { get; init; }
    public string Label { get; init; } = "";
    public SharpnessResult? Sharpness { get; set; }
    public ExposureResult? Exposure { get; set; }
    /// <summary>Esito del modello ONNX sul telefono.</summary>
    public DistractorReport? OnDeviceDistractors { get; set; }
    /// <summary>Esito dell'analisi nel cloud (Claude).</summary>
    public DistractorReport? CloudDistractors { get; set; }
    public DistractorReport? Distractors => DistractorReport.Merge(OnDeviceDistractors, CloudDistractors);
    public string? DistractorError { get; set; }

    public QualityReport Report => new(Sharpness!, Exposure!, Distractors, DistractorError);

    public void Dispose()
    {
        Bitmap.Dispose();
        try { File.Delete(TempPath); } catch { /* file temporaneo */ }
    }
}

/// <summary>Pipeline di scatto: file temporaneo → controlli → conferma → correzioni → catalogazione.</summary>
public sealed class PhotoService(
    PropertyRepository repository,
    PhotoCatalog catalog,
    AppSettings settings,
    OnDeviceModelService onDeviceModel,
    BackgroundSyncService sync)
{
    private readonly PhotoProcessor _processor = new();

    public async Task<PendingShot> PrepareAsync(Stream capture, CaptureContext context, Property property, Room room,
        ShotKind kind, string label, CancellationToken ct = default)
    {
        var temp = Path.Combine(FileSystem.CacheDirectory, $"scatto_{Guid.NewGuid():N}.jpg");
        await using (var fs = File.Create(temp))
            await capture.CopyToAsync(fs, ct);

        var bitmap = await Task.Run(() => ImageIo.LoadUpright(temp), ct);
        var shot = new PendingShot
        {
            TempPath = temp, Bitmap = bitmap, Context = context,
            Property = property, Room = room, Kind = kind, Label = label,
        };
        var (sharpness, exposure) = await Task.Run(() => _processor.AnalyzeLocal(bitmap, context), ct);
        shot.Sharpness = sharpness;
        shot.Exposure = exposure;
        return shot;
    }

    /// <summary>Controllo oggetti di disturbo sul telefono (ONNX): immediato, senza rete. Falso se il modello manca.</summary>
    public async Task<bool> CheckOnDeviceAsync(PendingShot shot, CancellationToken ct = default)
    {
        if (!settings.UseOnDeviceDetection) return false;
        var detector = await onDeviceModel.GetAsync();
        if (detector is null) return false;
        try
        {
            shot.OnDeviceDistractors = await detector.AnalyzeAsync(shot.Bitmap, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            shot.DistractorError = $"Controllo sul telefono non riuscito: {ex.Message}";
            return false;
        }
    }

    /// <summary>Controllo oggetti di disturbo nel cloud. Non blocca il flusso se la rete manca.</summary>
    public async Task CheckDistractorsAsync(PendingShot shot, CancellationToken ct = default)
    {
        if (!settings.UseCloudDetection) return;
        var key = await settings.GetApiKeyAsync();
        var detector = new DistractorDetector(new DistractorDetectorOptions
        {
            ApiKey = key,
            BaseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ? null : settings.ApiBaseUrl,
        });
        var local = shot.OnDeviceDistractors is not null;
        if (!detector.IsConfigured)
        {
            if (!local) shot.DistractorError = "Controllo oggetti non attivo: importa un modello ONNX o inserisci la chiave API nelle impostazioni.";
            return;
        }
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            shot.DistractorError = local
                ? "Senza rete: eseguito solo il controllo sul telefono."
                : "Nessuna connessione: controllo oggetti saltato.";
            return;
        }
        try
        {
            shot.CloudDistractors = await detector.AnalyzeAsync(shot.Bitmap, shot.Room.Name, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            shot.DistractorError = DistractorDetector.Describe(ex);
        }
    }

    /// <summary>Salva originale e versione corretta nella cartella dell'immobile e registra la foto.</summary>
    public async Task<Photo> CommitAsync(PendingShot shot, CancellationToken ct = default)
    {
        var sequence = await repository.NextSequenceAsync(shot.Room.Id, shot.Kind);
        var (finalRel, originalRel) = catalog.PhotoPaths(shot.Property, shot.Room, shot.Kind, sequence, shot.Label);
        var finalAbs = catalog.Absolute(finalRel);
        var originalAbs = catalog.Absolute(originalRel);

        Directory.CreateDirectory(Path.GetDirectoryName(originalAbs)!);
        File.Copy(shot.TempPath, originalAbs, overwrite: true);

        var photo = new Photo
        {
            PropertyId = shot.Property.Id,
            RoomId = shot.Room.Id,
            Kind = shot.Kind,
            Sequence = sequence,
            Label = shot.Label,
            RelativePath = finalRel,
            OriginalRelativePath = originalRel,
            CapturedAt = DateTime.UtcNow,
            PitchDegrees = shot.Context.PitchDegrees,
            RollDegrees = shot.Context.RollDegrees,
            HeightCm = shot.Context.HeightCm,
            MaxAngularVelocity = shot.Context.MaxAngularVelocity,
            SharpnessScore = shot.Sharpness?.Score ?? 0,
            IsSharp = shot.Sharpness?.IsSharp ?? false,
            ClippedHighlightsPercent = shot.Exposure?.ClippedHighlightsPercent ?? 0,
            ClippedShadowsPercent = shot.Exposure?.ClippedShadowsPercent ?? 0,
            BacklightDetected = shot.Exposure?.BacklightDetected ?? false,
            DistractorsJson = shot.Distractors is { } d ? DistractorDetector.Serialize(d) : "",
        };

        // Correzioni automatiche e metadati EXIF (immobile, ambiente, assetto) nel JPEG finale.
        var options = new ProcessingOptions(settings.AutoPerspective, settings.AutoTone, RefineWithLines: settings.RefineWithLines);
        _processor.Perspective.HorizontalFovDegrees = settings.HorizontalFovDegrees;
        var metadata = MetadataFactory.Create(shot.Property, shot.Room, photo, settings.AgentName);
        var result = await Task.Run(() => _processor.ProcessAndSave(shot.Bitmap, shot.Context, options, finalAbs, metadata), ct);
        photo.PerspectiveCorrected = result.PerspectiveApplied;
        photo.CorrectedFromLines = result.PerspectiveApplied && result.FromLines;
        photo.ToneCorrected = result.ToneApplied;

        await repository.SavePhotoAsync(photo);
        sync.Trigger();
        return photo;
    }

    public async Task DeletePhotoAsync(Photo photo)
    {
        foreach (var rel in new[] { photo.RelativePath, photo.OriginalRelativePath })
        {
            if (string.IsNullOrEmpty(rel)) continue;
            try { File.Delete(catalog.Absolute(rel)); } catch { /* già rimosso */ }
        }
        await repository.DeletePhotoAsync(photo);
    }

    public string AbsolutePath(Photo photo) => catalog.Absolute(photo.RelativePath);
}
