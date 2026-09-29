using SQLite;

namespace AppPhotoImmibili.Core.Models;

/// <summary>Fotografia catalogata con i metadati di scatto e l'esito dei controlli qualità.</summary>
[Table("photos")]
public class Photo
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Identificativo globale: chiave di idempotenza per il caricamento sul server dell'agenzia.</summary>
    [Indexed]
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");

    [Indexed]
    public int PropertyId { get; set; }

    [Indexed]
    public int RoomId { get; set; }

    public ShotKind Kind { get; set; }

    /// <summary>Progressivo per ambiente e tipo di scatto (1, 2, ...).</summary>
    public int Sequence { get; set; }

    /// <summary>Etichetta libera per i dettagli (es. "Vista dal balcone").</summary>
    public string Label { get; set; } = "";

    /// <summary>File finale (corretto se la correzione è attiva), relativo alla cartella dati.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Originale non elaborato, relativo alla cartella dati.</summary>
    public string OriginalRelativePath { get; set; } = "";

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    // Assetto al momento dello scatto
    public double PitchDegrees { get; set; }
    public double RollDegrees { get; set; }
    public double? HeightCm { get; set; }
    public double MaxAngularVelocity { get; set; }

    // Controllo qualità
    public double SharpnessScore { get; set; }
    public bool IsSharp { get; set; }
    public double ClippedHighlightsPercent { get; set; }
    public double ClippedShadowsPercent { get; set; }
    public bool BacklightDetected { get; set; }
    public string DistractorsJson { get; set; } = "";
    public bool PerspectiveCorrected { get; set; }
    public bool ToneCorrected { get; set; }
    /// <summary>Vero se la correzione prospettica usa l'assetto stimato dalle linee verticali.</summary>
    public bool CorrectedFromLines { get; set; }

    // Sincronizzazione con il server dell'agenzia
    [Indexed]
    public SyncState SyncState { get; set; } = SyncState.InCoda;
    public int SyncAttempts { get; set; }
    public DateTime? NextSyncAttemptAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string SyncError { get; set; } = "";
}
