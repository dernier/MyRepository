using System.Text.RegularExpressions;
using AppPhotoImmibili.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Ai;

/// <summary>Oggetto rilevato, con il riquadro in coordinate normalizzate (0–1) dell'immagine originale.</summary>
public sealed record Detection(string ClassName, float Confidence, float Left, float Top, float Right, float Bottom)
{
    public float Area => Math.Max(0, Right - Left) * Math.Max(0, Bottom - Top);
}

public sealed class OnDeviceDetectorOptions
{
    public float ConfidenceThreshold { get; set; } = 0.4f;
    public float IouThreshold { get; set; } = 0.45f;

    /// <summary>Riquadri più piccoli di questa frazione dell'immagine sono ignorati (non si notano nella foto).</summary>
    public float MinArea { get; set; } = 0.0015f;

    /// <summary>Segnala anche le classi senza regola in <see cref="DistractorCatalog"/>.</summary>
    public bool IncludeUnknownClasses { get; set; }

    /// <summary>Opzioni della sessione ONNX (es. provider NNAPI su Android o CoreML su iOS).</summary>
    public SessionOptions? SessionOptions { get; set; }
}

/// <summary>
/// Rilevamento degli elementi di disturbo direttamente sul telefono con ONNX Runtime, senza rete.
/// Supporta i modelli YOLOv8/YOLO11 esportati in ONNX (<c>yolo export format=onnx</c>): ingresso [1,3,H,W]
/// RGB 0–1 e uscita [1, 4 + classi, N] con riquadri (cx, cy, w, h) in pixel dell'ingresso.
/// I nomi delle classi sono letti dai metadati <c>names</c> scritti da Ultralytics; in mancanza si usano le classi COCO.
/// </summary>
public sealed class OnDeviceDistractorDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly OnDeviceDetectorOptions _options;
    private readonly string _inputName;
    private readonly int _inputWidth, _inputHeight;

    public IReadOnlyList<string> ClassNames { get; }

    public OnDeviceDistractorDetector(string modelPath, OnDeviceDetectorOptions? options = null)
        : this(options ?? new(), o => o.SessionOptions is null ? new InferenceSession(modelPath) : new InferenceSession(modelPath, o.SessionOptions)) { }

    public OnDeviceDistractorDetector(byte[] model, OnDeviceDetectorOptions? options = null)
        : this(options ?? new(), o => o.SessionOptions is null ? new InferenceSession(model) : new InferenceSession(model, o.SessionOptions)) { }

    private OnDeviceDistractorDetector(OnDeviceDetectorOptions options, Func<OnDeviceDetectorOptions, InferenceSession> factory)
    {
        _options = options;
        _session = factory(options);
        var input = _session.InputMetadata.First();
        _inputName = input.Key;
        var dims = input.Value.Dimensions;
        // Dimensioni dinamiche (-1): si usa 640, la dimensione standard dei modelli YOLO.
        _inputHeight = dims.Length == 4 && dims[2] > 0 ? dims[2] : 640;
        _inputWidth = dims.Length == 4 && dims[3] > 0 ? dims[3] : 640;
        ClassNames = _session.ModelMetadata.CustomMetadataMap.TryGetValue("names", out var names) && ParseNames(names) is { Count: > 0 } parsed
            ? parsed
            : CocoClasses;
    }

    public DistractorReport Analyze(SKBitmap photo)
    {
        var detections = Detect(photo);
        return ToReport(detections, _options.IncludeUnknownClasses);
    }

    public Task<DistractorReport> AnalyzeAsync(SKBitmap photo, CancellationToken ct = default) =>
        Task.Run(() => Analyze(photo), ct);

    public IReadOnlyList<Detection> Detect(SKBitmap photo)
    {
        var (tensor, letterbox) = Preprocess(photo, _inputWidth, _inputHeight);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions.ToArray();
        return Postprocess(output.ToArray(), dims, ClassNames, letterbox, _options);
    }

    /// <summary>Parametri del ridimensionamento con bande (letterbox) per riportare i riquadri all'immagine originale.</summary>
    public readonly record struct Letterbox(float Scale, float PadX, float PadY, int SourceWidth, int SourceHeight);

    /// <summary>Ridimensiona mantenendo le proporzioni, aggiunge bande grigie e converte in tensore NCHW RGB 0–1.</summary>
    public static (DenseTensor<float> Tensor, Letterbox Letterbox) Preprocess(SKBitmap photo, int width, int height)
    {
        var scale = Math.Min(width / (float)photo.Width, height / (float)photo.Height);
        var w = Math.Max(1, (int)Math.Round(photo.Width * scale));
        var h = Math.Max(1, (int)Math.Round(photo.Height * scale));
        var padX = (width - w) / 2f;
        var padY = (height - h) / 2f;

        using var canvasBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(canvasBitmap))
        {
            canvas.Clear(new SKColor(114, 114, 114));
            using var image = SKImage.FromBitmap(photo);
            canvas.DrawImage(image, SKRect.Create(padX, padY, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var tensor = new DenseTensor<float>([1, 3, height, width]);
        var span = canvasBitmap.GetPixelSpan();
        var plane = width * height;
        var buffer = tensor.Buffer.Span;
        for (int i = 0, p = 0; i < plane; i++, p += 4)
        {
            buffer[i] = span[p] / 255f;
            buffer[plane + i] = span[p + 1] / 255f;
            buffer[2 * plane + i] = span[p + 2] / 255f;
        }
        return (tensor, new Letterbox(scale, padX, padY, photo.Width, photo.Height));
    }

    /// <summary>Decodifica l'uscita YOLOv8/11 ([1, 4+C, N] o trasposta [1, N, 4+C]) e applica la NMS per classe.</summary>
    public static IReadOnlyList<Detection> Postprocess(float[] data, int[] dims, IReadOnlyList<string> classNames,
        Letterbox lb, OnDeviceDetectorOptions options)
    {
        if (dims.Length != 3) throw new InvalidDataException($"Uscita del modello non supportata: [{string.Join(',', dims)}]");
        var nc = classNames.Count;
        bool transposed;
        int attrs, count;
        if (dims[1] == 4 + nc) { transposed = false; attrs = dims[1]; count = dims[2]; }
        else if (dims[2] == 4 + nc) { transposed = true; attrs = dims[2]; count = dims[1]; }
        else throw new InvalidDataException($"L'uscita [{string.Join(',', dims)}] non corrisponde a {nc} classi");

        float At(int attr, int i) => transposed ? data[i * attrs + attr] : data[attr * count + i];

        var candidates = new List<(int Cls, float Conf, float X1, float Y1, float X2, float Y2)>();
        for (var i = 0; i < count; i++)
        {
            var best = -1; var bestConf = options.ConfidenceThreshold;
            for (var c = 0; c < nc; c++)
            {
                var conf = At(4 + c, i);
                if (conf >= bestConf) { bestConf = conf; best = c; }
            }
            if (best < 0) continue;
            float cx = At(0, i), cy = At(1, i), bw = At(2, i), bh = At(3, i);
            candidates.Add((best, bestConf, cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2));
        }

        var kept = new List<Detection>();
        foreach (var group in candidates.GroupBy(c => c.Cls))
        {
            var sorted = group.OrderByDescending(c => c.Conf).ToList();
            var accepted = new List<(float X1, float Y1, float X2, float Y2)>();
            foreach (var c in sorted)
            {
                if (accepted.Any(a => Iou(a, (c.X1, c.Y1, c.X2, c.Y2)) > options.IouThreshold)) continue;
                accepted.Add((c.X1, c.Y1, c.X2, c.Y2));
                // Da pixel dell'ingresso a coordinate normalizzate dell'immagine originale.
                float Nx(float x) => Math.Clamp((x - lb.PadX) / lb.Scale / lb.SourceWidth, 0, 1);
                float Ny(float y) => Math.Clamp((y - lb.PadY) / lb.Scale / lb.SourceHeight, 0, 1);
                var d = new Detection(classNames[c.Cls], c.Conf, Nx(c.X1), Ny(c.Y1), Nx(c.X2), Ny(c.Y2));
                if (d.Area >= options.MinArea) kept.Add(d);
            }
        }
        return kept.OrderByDescending(d => d.Confidence).ToList();
    }

    /// <summary>Raggruppa i rilevamenti per tipo di elemento e li traduce in indicazioni per l'agente.</summary>
    public static DistractorReport ToReport(IEnumerable<Detection> detections, bool includeUnknown = false)
    {
        var items = new List<Distractor>();
        foreach (var group in detections
                     .Select(d => (Detection: d, Rule: DistractorCatalog.RuleFor(d.ClassName, includeUnknown)))
                     .Where(x => x.Rule is not null)
                     .GroupBy(x => x.Rule!.Item))
        {
            var rule = group.First().Rule!;
            var main = group.OrderByDescending(x => x.Detection.Area).First().Detection;
            var n = group.Count();
            var position = DistractorCatalog.Position((main.Left + main.Right) / 2, (main.Top + main.Bottom) / 2);
            if (n > 1) position += $" e altri {n - 1}";
            items.Add(new Distractor(n > 1 ? $"{rule.Item} ({n})" : rule.Item, position, rule.Severity, rule.Action));
        }
        items.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        var summary = items.Count == 0 ? "Nessun elemento di disturbo rilevato sul telefono."
            : items.Any(i => i.Severity == Severity.Alta) ? "Da sistemare prima di riscattare."
            : "Qualche dettaglio da sistemare.";
        return new DistractorReport(items, summary);
    }

    private static float Iou((float X1, float Y1, float X2, float Y2) a, (float X1, float Y1, float X2, float Y2) b)
    {
        var ix = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var iy = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var inter = ix * iy;
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>Legge i nomi delle classi scritti da Ultralytics, es. <c>{0: 'person', 1: 'bicycle'}</c>.</summary>
    public static IReadOnlyList<string> ParseNames(string metadata)
    {
        var matches = Regex.Matches(metadata, @"(\d+)\s*:\s*(?:'([^']*)'|""([^""]*)"")");
        var pairs = matches.Select(m => (Index: int.Parse(m.Groups[1].Value),
            Name: m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)).ToList();
        if (pairs.Count == 0) return [];
        var names = new string[pairs.Max(p => p.Index) + 1];
        foreach (var (index, name) in pairs) names[index] = name;
        return names.Select((n, i) => n ?? $"classe_{i}").ToArray();
    }

    public void Dispose() => _session.Dispose();

    public static readonly IReadOnlyList<string> CocoClasses =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
        "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
        "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
        "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
        "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone",
        "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
        "hair drier", "toothbrush",
    ];
}
