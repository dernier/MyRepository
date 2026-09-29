namespace InquadraCasa.Core.Imaging;

/// <summary>Immagine in scala di grigi (luminanza 0–255) per le analisi.</summary>
public sealed class LumaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public LumaImage(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height];
        if (Pixels.Length != width * height) throw new ArgumentException("Dimensione buffer errata", nameof(pixels));
    }

    public byte this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    /// <summary>Luminanza Rec.709 da RGB.</summary>
    public static byte Luma(byte r, byte g, byte b) => (byte)((r * 54 + g * 183 + b * 19) >> 8);

    public int[] Histogram()
    {
        var h = new int[256];
        foreach (var p in Pixels) h[p]++;
        return h;
    }
}
