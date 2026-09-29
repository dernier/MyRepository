using AppPhotoImmibili.Core.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;
using Directory = MetadataExtractor.Directory;

namespace AppPhotoImmibili.Core.Tests;

public class ExifTests
{
    private static byte[] SampleJpeg()
    {
        using var bmp = new SKBitmap(64, 48);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.CornflowerBlue);
        return ImageIo.EncodeJpeg(bmp, 90);
    }

    private static readonly PhotoMetadata Meta = new()
    {
        Uid = "abc123",
        PropertyCode = "RIF-1234",
        Address = "Via Roma 10, Città",
        RoomName = "Cucina",
        ShotKind = "Panoramica",
        Sequence = 2,
        Agent = "Mario Rossi",
        PitchDegrees = 0.4,
        RollDegrees = -0.2,
        HeightCm = 131,
        CapturedAt = new DateTime(2026, 9, 29, 10, 30, 15),
    };

    private static IReadOnlyList<Directory> Read(byte[] jpeg) => ImageMetadataReader.ReadMetadata(new MemoryStream(jpeg));

    [Fact]
    public void Embed_WritesStandardTags()
    {
        var jpeg = ExifWriter.Embed(SampleJpeg(), Meta);
        var dirs = Read(jpeg);
        var ifd0 = dirs.OfType<ExifIfd0Directory>().Single();
        var sub = dirs.OfType<ExifSubIfdDirectory>().Single();

        Assert.Equal("RIF-1234 · Cucina", ifd0.GetString(ExifDirectoryBase.TagImageDescription));
        Assert.Equal(1, ifd0.GetInt32(ExifDirectoryBase.TagOrientation));
        Assert.Equal("AppPhotoImmibili", ifd0.GetString(ExifDirectoryBase.TagSoftware));
        Assert.Equal("Mario Rossi", ifd0.GetString(ExifDirectoryBase.TagArtist));
        Assert.Equal("2026:09:29 10:30:15", sub.GetString(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.Equal("RIF-1234;Cucina;Panoramica", ifd0.GetDescription(ExifDirectoryBase.TagWinKeywords));

        var comment = sub.GetDescription(ExifDirectoryBase.TagUserComment);
        Assert.NotNull(comment);
        var parsed = ExifWriter.FromJson(comment!);
        Assert.NotNull(parsed);
        Assert.Equal("RIF-1234", parsed!.PropertyCode);
        Assert.Equal("Via Roma 10, Città", parsed.Address);
        Assert.Equal(0.4, parsed.PitchDegrees);
        Assert.Equal(131, parsed.HeightCm);
    }

    [Fact]
    public void Embed_KeepsImageDecodable_AndReplacesOldExif()
    {
        var once = ExifWriter.Embed(SampleJpeg(), Meta);
        var twice = ExifWriter.Embed(once, Meta with { RoomName = "Bagno" });

        Assert.Single(Read(twice).OfType<ExifIfd0Directory>());
        Assert.Equal("RIF-1234 · Bagno", Read(twice).OfType<ExifIfd0Directory>().Single().GetString(ExifDirectoryBase.TagImageDescription));

        using var decoded = SKBitmap.Decode(twice);
        Assert.Equal(64, decoded.Width);
        Assert.Equal(48, decoded.Height);
    }

    [Fact]
    public void Embed_RejectsNonJpeg() =>
        Assert.Throws<InvalidDataException>(() => ExifWriter.Embed([1, 2, 3, 4], Meta));
}
