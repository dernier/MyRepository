using AppPhotoImmibili.Core.Imaging;
using AppPhotoImmibili.Core.Models;

namespace AppPhotoImmibili.Core.Catalog;

/// <summary>Metadati EXIF di una foto catalogata, gli stessi inviati al server dell'agenzia.</summary>
public static class MetadataFactory
{
    public static PhotoMetadata Create(Property property, Room room, Photo photo, string agent = "") => new()
    {
        Uid = photo.Uid,
        PropertyCode = property.Code,
        Address = string.Join(", ", new[] { property.Address, property.City }.Where(s => !string.IsNullOrWhiteSpace(s))),
        RoomName = room.Name,
        ShotKind = photo.Kind.ToString(),
        Sequence = photo.Sequence,
        Label = photo.Label,
        Agent = agent,
        PitchDegrees = Math.Round(photo.PitchDegrees, 2),
        RollDegrees = Math.Round(photo.RollDegrees, 2),
        HeightCm = photo.HeightCm is { } h ? Math.Round(h) : null,
        SharpnessScore = Math.Round(photo.SharpnessScore, 1),
        PerspectiveCorrected = photo.PerspectiveCorrected,
        CorrectedFromLines = photo.CorrectedFromLines,
        CapturedAt = photo.CapturedAt.ToLocalTime(),
    };
}
