using InquadraCasa.Core.Models;

namespace InquadraCasa.Core.Catalog;

/// <summary>Percorsi su disco delle foto catalogate (relativi alla cartella dati dell'app).</summary>
public sealed class PhotoCatalog(string rootDirectory)
{
    public const string PropertiesFolder = "Immobili";
    public const string OriginalsFolder = "_originali";

    public string RootDirectory { get; } = rootDirectory;

    public string Absolute(string relativePath) =>
        Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string PropertyDirectory(Property p) =>
        Path.Combine(RootDirectory, PropertiesFolder, p.FolderName);

    /// <summary>Percorsi relativi (con '/') per il file finale e per l'originale.</summary>
    public (string Final, string Original) PhotoPaths(Property p, Room r, ShotKind kind, int sequence, string? label = null)
    {
        var name = NameFormatter.PhotoFileName(p, r, kind, sequence, label);
        var roomDir = $"{PropertiesFolder}/{p.FolderName}/{NameFormatter.RoomFolder(r)}";
        return ($"{roomDir}/{name}", $"{roomDir}/{OriginalsFolder}/{name}");
    }
}
