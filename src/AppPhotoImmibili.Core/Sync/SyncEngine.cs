using AppPhotoImmibili.Core.Catalog;
using AppPhotoImmibili.Core.Data;
using AppPhotoImmibili.Core.Imaging;
using AppPhotoImmibili.Core.Models;

namespace AppPhotoImmibili.Core.Sync;

public sealed record SyncRunResult(int Uploaded, int Failed, int Postponed);

/// <summary>
/// Svuota la coda delle foto da caricare: crea la versione per il web con i metadati EXIF e la invia.
/// Dopo un errore temporaneo la foto resta in coda con un'attesa crescente (1, 2, 4… fino a 60 minuti).
/// </summary>
public sealed class SyncEngine(PropertyRepository repository, PhotoCatalog catalog, IPhotoUploader uploader)
{
    public int WebMaxSide { get; set; } = 2048;
    public int WebQuality { get; set; } = 85;
    public string Agent { get; set; } = "";
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempts - 1))));

    public async Task<SyncRunResult> RunOnceAsync(CancellationToken ct = default)
    {
        int uploaded = 0, failed = 0, postponed = 0;
        var properties = new Dictionary<int, Property?>();
        var rooms = new Dictionary<int, Room?>();

        foreach (var photo in await repository.GetPhotosToSyncAsync(UtcNow(), limit: 50))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(photo.Uid)) photo.Uid = Guid.NewGuid().ToString("N");

            if (!properties.TryGetValue(photo.PropertyId, out var property))
                properties[photo.PropertyId] = property = await repository.GetPropertyAsync(photo.PropertyId);
            if (!rooms.TryGetValue(photo.RoomId, out var room))
                rooms[photo.RoomId] = room = await repository.GetRoomAsync(photo.RoomId);

            var path = catalog.Absolute(photo.RelativePath);
            UploadOutcome outcome;
            if (property is null || room is null || !File.Exists(path))
            {
                outcome = new(UploadStatus.Definitivo, "File o scheda dell'immobile non trovati.");
            }
            else
            {
                var metadata = MetadataFactory.Create(property, room, photo, Agent);
                var jpeg = await Task.Run(() => PhotoProcessor.CreateWebVersion(path, metadata, WebMaxSide, WebQuality), ct);
                outcome = await uploader.UploadAsync(new PhotoUpload(metadata, Path.GetFileName(path), jpeg), ct);
            }

            photo.SyncAttempts++;
            switch (outcome.Status)
            {
                case UploadStatus.Ok:
                    photo.SyncState = SyncState.Caricata;
                    photo.UploadedAt = UtcNow();
                    photo.NextSyncAttemptAt = null;
                    photo.SyncError = "";
                    uploaded++;
                    break;
                case UploadStatus.Temporaneo:
                    photo.NextSyncAttemptAt = UtcNow() + Backoff(photo.SyncAttempts);
                    photo.SyncError = outcome.Message;
                    postponed++;
                    break;
                default:
                    photo.SyncState = SyncState.Errore;
                    photo.SyncError = outcome.Message;
                    failed++;
                    break;
            }
            await repository.SavePhotoAsync(photo);

            // Senza rete o con il token errato è inutile provare le foto successive.
            if (outcome.Status == UploadStatus.Temporaneo || outcome.StopQueue) break;
        }
        return new SyncRunResult(uploaded, failed, postponed);
    }
}
