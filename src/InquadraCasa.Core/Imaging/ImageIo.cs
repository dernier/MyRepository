using System;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace InquadraCasa.Core.Imaging;

/// <summary>Caricamento, orientamento, ridimensionamento e salvataggio con SkiaSharp.</summary>
public static class ImageIo
{
    /// <summary>Decodifica un'immagine applicando l'orientamento EXIF, così i pixel sono "dritti".</summary>
    public static SKBitmap LoadUpright(Stream stream)
    {
        using var managed = new SKManagedStream(stream, disposeManagedStream: false);
        using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("Formato immagine non riconosciuto");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            throw new InvalidDataException($"Decodifica fallita: {result}");
        }
        return ApplyOrigin(decoded, codec.EncodedOrigin);
    }

    public static SKBitmap LoadUpright(string path)
    {
        using var fs = File.OpenRead(path);
        return LoadUpright(fs);
    }

    internal static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft) return src;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        float w = swap ? src.Height : src.Width;
        float h = swap ? src.Width : src.Height;

        // Mappa sorgente → destinazione: x' = a·x + b·y + c, y' = d·x + e·y + f
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var dst = new SKBitmap(new SKImageInfo((int)w, (int)h, src.ColorType, src.AlphaType));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.SetMatrix(m);
            using var image = SKImage.FromBitmap(src);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        src.Dispose();
        return dst;
    }

    /// <summary>Ridimensiona mantenendo le proporzioni in modo che il lato lungo sia al massimo <paramref name="maxSide"/>.</summary>
    public static SKBitmap Downscale(SKBitmap src, int maxSide)
    {
        var scale = Math.Min(1.0, maxSide / (double)Math.Max(src.Width, src.Height));
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        return src.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Ridimensionamento fallito");
    }

    /// <summary>Converte in luminanza, ridimensionando al lato lungo indicato (per analisi veloci).</summary>
    public static LumaImage ToLuma(SKBitmap src, int maxSide = 800)
    {
        using var small = Downscale(src, maxSide);
        var luma = new LumaImage(small.Width, small.Height);
        var span = small.GetPixelSpan();
        for (int i = 0, p = 0; i < luma.Pixels.Length; i++, p += 4)
            luma.Pixels[i] = LumaImage.Luma(span[p], span[p + 1], span[p + 2]);
        return luma;
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 92)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static void SaveJpeg(SKBitmap bitmap, string path, int quality = 92)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, EncodeJpeg(bitmap, quality));
    }
}
