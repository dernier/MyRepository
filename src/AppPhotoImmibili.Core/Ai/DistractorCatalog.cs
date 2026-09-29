using AppPhotoImmibili.Core.Models;

namespace AppPhotoImmibili.Core.Ai;

/// <summary>Come presentare all'agente una classe riconosciuta dal modello on-device.</summary>
public sealed record DistractorRule(string Item, string Action, Severity Severity);

/// <summary>
/// Classi del modello di object detection considerate elementi di disturbo.
/// Comprende le classi COCO dei modelli YOLO pre-addestrati e le classi tipiche di un modello addestrato
/// sulle foto immobiliari (tavoletta alzata, cavi, disordine…). Le classi non elencate vengono ignorate,
/// a meno che il modello non sia dedicato agli elementi di disturbo (<c>includeUnknown</c>).
/// </summary>
public static class DistractorCatalog
{
    private static readonly Dictionary<string, DistractorRule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        // Classi COCO (yolov8n.onnx, yolo11n.onnx…)
        ["person"] = new("Persona nell'inquadratura", "Chiedi di uscire dalla stanza o controlla i riflessi", Severity.Alta),
        ["cat"] = new("Animale domestico", "Porta l'animale fuori dalla stanza", Severity.Media),
        ["dog"] = new("Animale domestico", "Porta l'animale fuori dalla stanza", Severity.Media),
        ["bottle"] = new("Bottiglie o flaconi", "Togli bottiglie e flaconi da ripiani e piani di lavoro", Severity.Media),
        ["wine glass"] = new("Bicchieri", "Riponi i bicchieri", Severity.Bassa),
        ["cup"] = new("Tazze", "Riponi tazze e stoviglie", Severity.Bassa),
        ["bowl"] = new("Ciotole e stoviglie", "Libera il piano dalle stoviglie", Severity.Bassa),
        ["fork"] = new("Posate", "Riponi le posate", Severity.Bassa),
        ["knife"] = new("Coltelli", "Riponi i coltelli", Severity.Media),
        ["spoon"] = new("Posate", "Riponi le posate", Severity.Bassa),
        ["toothbrush"] = new("Spazzolini", "Riponi spazzolini e prodotti per l'igiene", Severity.Media),
        ["hair drier"] = new("Asciugacapelli", "Riponi l'asciugacapelli e il suo cavo", Severity.Media),
        ["handbag"] = new("Borse", "Togli borse e zaini dalla scena", Severity.Media),
        ["backpack"] = new("Zaino", "Togli borse e zaini dalla scena", Severity.Media),
        ["suitcase"] = new("Valigia", "Sposta la valigia fuori dall'inquadratura", Severity.Media),
        ["umbrella"] = new("Ombrello", "Riponi l'ombrello", Severity.Bassa),
        ["cell phone"] = new("Telefono", "Togli il telefono dai ripiani", Severity.Bassa),
        ["remote"] = new("Telecomando", "Riponi il telecomando", Severity.Bassa),
        ["teddy bear"] = new("Giocattoli", "Raccogli i giocattoli", Severity.Media),
        ["sports ball"] = new("Giocattoli", "Raccogli i giocattoli", Severity.Media),
        ["frisbee"] = new("Giocattoli", "Raccogli i giocattoli", Severity.Bassa),
        ["skateboard"] = new("Skateboard", "Sposta lo skateboard", Severity.Bassa),
        ["banana"] = new("Cibo", "Togli il cibo dal piano", Severity.Bassa),
        ["pizza"] = new("Cibo", "Togli il cibo dal piano", Severity.Media),
        ["sandwich"] = new("Cibo", "Togli il cibo dal piano", Severity.Media),
        ["scissors"] = new("Forbici", "Riponi le forbici", Severity.Bassa),

        // Classi di un modello dedicato alla fotografia immobiliare (nomi in inglese o italiano)
        ["toilet_seat_up"] = new("Tavoletta del WC alzata", "Abbassa tavoletta e coperchio", Severity.Alta),
        ["tavoletta_alzata"] = new("Tavoletta del WC alzata", "Abbassa tavoletta e coperchio", Severity.Alta),
        ["cable"] = new("Cavi a vista", "Nascondi cavi e prolunghe dietro i mobili", Severity.Media),
        ["cavi"] = new("Cavi a vista", "Nascondi cavi e prolunghe dietro i mobili", Severity.Media),
        ["clutter"] = new("Disordine", "Libera ripiani e superfici", Severity.Alta),
        ["disordine"] = new("Disordine", "Libera ripiani e superfici", Severity.Alta),
        ["trash"] = new("Spazzatura o bidone", "Togli bidoni e sacchetti", Severity.Alta),
        ["spazzatura"] = new("Spazzatura o bidone", "Togli bidoni e sacchetti", Severity.Alta),
        ["laundry"] = new("Panni o stendino", "Togli panni e stendibiancheria", Severity.Alta),
        ["panni"] = new("Panni o stendino", "Togli panni e stendibiancheria", Severity.Alta),
        ["towel_messy"] = new("Asciugamani in disordine", "Piega o togli gli asciugamani", Severity.Media),
        ["shoes"] = new("Scarpe", "Riponi le scarpe", Severity.Media),
        ["scarpe"] = new("Scarpe", "Riponi le scarpe", Severity.Media),
        ["detergent"] = new("Detersivi", "Togli detersivi e flaconi", Severity.Media),
        ["unmade_bed"] = new("Letto disfatto", "Rifai il letto", Severity.Alta),
        ["open_door_cabinet"] = new("Ante o cassetti aperti", "Chiudi ante e cassetti", Severity.Media),
        ["photographer_reflection"] = new("Riflesso del fotografo", "Cambia angolazione rispetto a specchi e vetri", Severity.Alta),
    };

    /// <param name="includeUnknown">Segnala anche le classi senza regola (modello addestrato solo su elementi di disturbo).</param>
    public static DistractorRule? RuleFor(string className, bool includeUnknown = false)
    {
        if (Rules.TryGetValue(className.Trim(), out var r)) return r;
        if (Rules.TryGetValue(className.Trim().Replace(' ', '_'), out r)) return r;
        return includeUnknown ? new(className, "Valuta se rimuoverlo prima dello scatto", Severity.Media) : null;
    }

    /// <summary>Posizione leggibile ("in basso a sinistra") dal centro del riquadro, in coordinate normalizzate 0–1.</summary>
    public static string Position(double cx, double cy)
    {
        var col = cx < 1 / 3.0 ? 0 : cx < 2 / 3.0 ? 1 : 2;
        var row = cy < 1 / 3.0 ? 0 : cy < 2 / 3.0 ? 1 : 2;
        if (row == 1 && col == 1) return "al centro";
        var vertical = row switch { 0 => "in alto", 1 => "a metà altezza", _ => "in basso" };
        var horizontal = col switch { 0 => "a sinistra", 1 => "al centro", _ => "a destra" };
        return $"{vertical} {horizontal}";
    }
}
