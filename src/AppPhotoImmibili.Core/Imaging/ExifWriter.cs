using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppPhotoImmibili.Core.Imaging;

/// <summary>Metadati scritti nell'EXIF della foto finale: immobile, ambiente e assetto al momento dello scatto.</summary>
public sealed record PhotoMetadata
{
    [JsonPropertyName("app")] public string App { get; init; } = "AppPhotoImmibili";
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";
    [JsonPropertyName("immobile")] public string PropertyCode { get; init; } = "";
    [JsonPropertyName("indirizzo")] public string Address { get; init; } = "";
    [JsonPropertyName("ambiente")] public string RoomName { get; init; } = "";
    [JsonPropertyName("tipo")] public string ShotKind { get; init; } = "";
    [JsonPropertyName("progressivo")] public int Sequence { get; init; }
    [JsonPropertyName("etichetta")] public string Label { get; init; } = "";
    [JsonPropertyName("agente")] public string Agent { get; init; } = "";
    [JsonPropertyName("inclinazione")] public double PitchDegrees { get; init; }
    [JsonPropertyName("rollio")] public double RollDegrees { get; init; }
    [JsonPropertyName("altezzaCm")] public double? HeightCm { get; init; }
    [JsonPropertyName("nitidezza")] public double SharpnessScore { get; init; }
    [JsonPropertyName("prospettivaCorretta")] public bool PerspectiveCorrected { get; init; }
    [JsonPropertyName("correzioneDaLinee")] public bool CorrectedFromLines { get; init; }
    [JsonIgnore] public DateTime CapturedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Inserisce un segmento APP1 Exif in un JPEG (SkiaSharp non scrive metadati).
/// Tag scritti: ImageDescription, Orientation = 1 (i pixel sono già dritti), Software, DateTime, Artist,
/// XPComment/XPKeywords (visibili in Esplora risorse), DateTimeOriginal e UserComment con i metadati in JSON.
/// </summary>
public static class ExifWriter
{
    private const ushort Ascii = 2, Short = 3, Long = 4, Undefined = 7, Byte = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(PhotoMetadata m) => JsonSerializer.Serialize(m, JsonOptions);

    public static PhotoMetadata? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<PhotoMetadata>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Restituisce una copia del JPEG con il segmento Exif (sostituisce un eventuale Exif già presente).</summary>
    public static byte[] Embed(byte[] jpeg, PhotoMetadata metadata)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new InvalidDataException("Il file non è un JPEG");

        var app1 = BuildApp1(metadata);
        using var output = new MemoryStream(jpeg.Length + app1.Length);
        output.Write(jpeg, 0, 2);

        var pos = 2;
        var inserted = false;
        // Copia i segmenti APPn iniziali: JFIF (APP0) resta prima dell'Exif, un vecchio Exif viene scartato.
        while (pos + 4 <= jpeg.Length && jpeg[pos] == 0xFF && jpeg[pos + 1] is >= 0xE0 and <= 0xEF)
        {
            var marker = jpeg[pos + 1];
            var len = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 2));
            var isExif = marker == 0xE1 && pos + 10 <= jpeg.Length && jpeg.AsSpan(pos + 4, 6).SequenceEqual("Exif\0\0"u8);
            if (marker != 0xE0 && !inserted)
            {
                output.Write(app1);
                inserted = true;
            }
            if (!isExif) output.Write(jpeg, pos, len + 2);
            pos += len + 2;
        }
        if (!inserted) output.Write(app1);
        output.Write(jpeg, pos, jpeg.Length - pos);
        return output.ToArray();
    }

    public static void EmbedInFile(string path, PhotoMetadata metadata) =>
        File.WriteAllBytes(path, Embed(File.ReadAllBytes(path), metadata));

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Data);

    private static byte[] BuildApp1(PhotoMetadata m)
    {
        var date = Ascii0(m.CapturedAt.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
        var description = string.Join(" · ", new[] { m.PropertyCode, m.RoomName, m.Label }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var keywords = string.Join(';', new[] { m.PropertyCode, m.RoomName, m.ShotKind }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var json = ToJson(m);

        var ifd0 = new List<Entry>
        {
            new(0x010E, Ascii, 0, Ascii0(description)),
            new(0x0112, Short, 1, U16(1)),
            new(0x0131, Ascii, 0, Ascii0(m.App)),
            new(0x0132, Ascii, 0, date),
        };
        if (!string.IsNullOrWhiteSpace(m.Agent)) ifd0.Add(new(0x013B, Ascii, 0, Ascii0(m.Agent)));
        ifd0.Add(new(0x8769, Long, 1, U32(0))); // puntatore all'IFD Exif, calcolato sotto
        ifd0.Add(new(0x9C9C, Byte, 0, Ucs2(string.IsNullOrWhiteSpace(m.Address) ? description : $"{description} — {m.Address}")));
        ifd0.Add(new(0x9C9E, Byte, 0, Ucs2(keywords)));

        // UserComment: 8 byte di codifica + testo. Il JSON è ASCII perché System.Text.Json codifica i non-ASCII come \uXXXX.
        var comment = "ASCII\0\0\0"u8.ToArray().Concat(Encoding.ASCII.GetBytes(json)).ToArray();
        var exif = new List<Entry>
        {
            new(0x9003, Ascii, 0, date),
            new(0x9286, Undefined, 0, comment),
        };

        // Layout TIFF: header (8) | IFD0 | dati IFD0 | IFD Exif | dati IFD Exif
        var ifd0Size = IfdSize(ifd0);
        var exifOffset = 8 + ifd0Size;
        var ptr = ifd0.FindIndex(e => e.Tag == 0x8769);
        ifd0[ptr] = ifd0[ptr] with { Data = U32((uint)exifOffset) };

        using var tiff = new MemoryStream();
        tiff.Write("II*\0"u8);
        tiff.Write(U32(8));
        WriteIfd(tiff, ifd0, 8);
        WriteIfd(tiff, exif, exifOffset);

        var body = tiff.ToArray();
        var segmentLength = 2 + 6 + body.Length;
        if (segmentLength > ushort.MaxValue) throw new InvalidOperationException("Metadati troppo lunghi per il segmento Exif");

        using var app1 = new MemoryStream();
        app1.Write([0xFF, 0xE1, (byte)(segmentLength >> 8), (byte)segmentLength]);
        app1.Write("Exif\0\0"u8);
        app1.Write(body);
        return app1.ToArray();
    }

    private static int DataLength(Entry e) => e.Data.Length > 4 ? e.Data.Length + (e.Data.Length & 1) : 0;

    private static int IfdSize(List<Entry> entries) => 2 + entries.Count * 12 + 4 + entries.Sum(DataLength);

    private static void WriteIfd(Stream s, List<Entry> entries, int ifdOffset)
    {
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        var dataOffset = ifdOffset + 2 + entries.Count * 12 + 4;
        s.Write(U16((ushort)entries.Count));
        foreach (var e in entries)
        {
            var count = e.Count != 0 ? e.Count : (uint)e.Data.Length;
            s.Write(U16(e.Tag));
            s.Write(U16(e.Type));
            s.Write(U32(count));
            if (e.Data.Length <= 4)
            {
                var inline = new byte[4];
                e.Data.CopyTo(inline, 0);
                s.Write(inline);
            }
            else
            {
                s.Write(U32((uint)dataOffset));
                dataOffset += DataLength(e);
            }
        }
        s.Write(U32(0)); // nessun IFD successivo
        foreach (var e in entries.Where(e => e.Data.Length > 4))
        {
            s.Write(e.Data);
            if ((e.Data.Length & 1) == 1) s.WriteByte(0);
        }
    }

    // UTF-8: lo standard prevede ASCII, ma i lettori comuni mostrano correttamente anche gli accenti.
    private static byte[] Ascii0(string s) => [.. Encoding.UTF8.GetBytes(s), 0];

    private static byte[] Ucs2(string s) => [.. Encoding.Unicode.GetBytes(s), 0, 0];

    private static byte[] U16(ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return b;
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}
