using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using InquadraCasa.Core.Models;

namespace InquadraCasa.Core.Catalog;

/// <summary>Crea un archivio ZIP dell'immobile, pronto da inviare all'ufficio, con un manifest JSON.</summary>
public sealed class ExportService(PhotoCatalog catalog)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed record ManifestPhoto(string File, string Room, ShotKind Kind, int Sequence, string Label,
        DateTime CapturedAt, double PitchDegrees, double RollDegrees, double? HeightCm, bool IsSharp,
        bool BacklightDetected, bool PerspectiveCorrected);

    public sealed record Manifest(string Code, string Address, string City, string Template, DateTime ExportedAt,
        IReadOnlyList<string> Rooms, IReadOnlyList<ManifestPhoto> Photos);

    /// <param name="includeOriginals">Se vero include anche gli scatti non elaborati.</param>
    public async Task<string> ExportAsync(Property property, IReadOnlyList<Room> rooms, IReadOnlyList<Photo> photos,
        string outputDirectory, bool includeOriginals = false, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var zipPath = Path.Combine(outputDirectory, $"{property.FolderName}_{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        var roomById = rooms.ToDictionary(r => r.Id);
        var prefix = $"{PhotoCatalog.PropertiesFolder}/{property.FolderName}/";
        var entries = new List<ManifestPhoto>();

        await using (var fs = File.Create(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var photo in photos.OrderBy(p => roomById.GetValueOrDefault(p.RoomId)?.Order ?? 999)
                                        .ThenBy(p => p.Kind).ThenBy(p => p.Sequence))
            {
                ct.ThrowIfCancellationRequested();
                var entryName = Strip(photo.RelativePath, prefix);
                await AddFileAsync(zip, catalog.Absolute(photo.RelativePath), entryName, ct);
                if (includeOriginals && !string.IsNullOrEmpty(photo.OriginalRelativePath)
                    && photo.OriginalRelativePath != photo.RelativePath)
                    await AddFileAsync(zip, catalog.Absolute(photo.OriginalRelativePath), Strip(photo.OriginalRelativePath, prefix), ct);

                entries.Add(new ManifestPhoto(entryName, roomById.GetValueOrDefault(photo.RoomId)?.Name ?? "", photo.Kind,
                    photo.Sequence, photo.Label, photo.CapturedAt, Math.Round(photo.PitchDegrees, 2),
                    Math.Round(photo.RollDegrees, 2), photo.HeightCm is { } h ? Math.Round(h) : null, photo.IsSharp,
                    photo.BacklightDetected, photo.PerspectiveCorrected));
            }

            var manifest = new Manifest(property.Code, property.Address, property.City, property.TemplateId, DateTime.UtcNow,
                rooms.OrderBy(r => r.Order).Select(r => r.Name).ToList(), entries);
            var entry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var es = entry.Open();
            await JsonSerializer.SerializeAsync(es, manifest, JsonOptions, ct);
        }
        return zipPath;
    }

    private static string Strip(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : Path.GetFileName(path);

    private static async Task AddFileAsync(ZipArchive zip, string path, string entryName, CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        // Le JPEG sono già compresse: nessun guadagno a ricomprimerle.
        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        await using var es = entry.Open();
        await using var src = File.OpenRead(path);
        await src.CopyToAsync(es, ct);
    }
}
