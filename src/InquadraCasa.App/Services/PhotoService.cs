using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InquadraCasa.Core.Ai;
using InquadraCasa.Core.Analysis;
using InquadraCasa.Core.Catalog;
using InquadraCasa.Core.Data;
using InquadraCasa.Core.Imaging;
using InquadraCasa.Core.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using SkiaSharp;

namespace InquadraCasa.App.Services;

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
    public DistractorReport? Distractors { get; set; }
    public string? DistractorError { get; set; }

    public QualityReport Report => new(Sharpness!, Exposure!, Distractors, DistractorError);

    public void Dispose()
    {
        Bitmap.Dispose();
        try { File.Delete(TempPath); } catch { /* file temporaneo */ }
    }
}

/// <summary>Pipeline di scatto: file temporaneo → controlli → conferma → correzioni → catalogazione.</summary>
public sealed class PhotoService(PropertyRepository repository, PhotoCatalog catalog, AppSettings settings)
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

    /// <summary>Controllo oggetti di disturbo nel cloud. Non blocca il flusso se la rete manca.</summary>
    public async Task CheckDistractorsAsync(PendingShot shot, CancellationToken ct = default)
    {
        if (!settings.CheckDistractors) return;
        var key = await settings.GetApiKeyAsync();
        var detector = new DistractorDetector(new DistractorDetectorOptions
        {
            ApiKey = key,
            BaseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ? null : settings.ApiBaseUrl,
        });
        if (!detector.IsConfigured)
        {
            shot.DistractorError = "Controllo oggetti non attivo: inserisci la chiave API nelle impostazioni.";
            return;
        }
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            shot.DistractorError = "Nessuna connessione: controllo oggetti saltato.";
            return;
        }
        try
        {
            shot.Distractors = await detector.AnalyzeAsync(shot.Bitmap, shot.Room.Name, ct);
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

        var options = new ProcessingOptions(settings.AutoPerspective, settings.AutoTone);
        _processor.Perspective.HorizontalFovDegrees = settings.HorizontalFovDegrees;
        var result = await Task.Run(() => _processor.ProcessAndSave(shot.Bitmap, shot.Context, options, finalAbs), ct);

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
            PerspectiveCorrected = result.PerspectiveApplied,
            ToneCorrected = result.ToneApplied,
        };
        await repository.SavePhotoAsync(photo);
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
