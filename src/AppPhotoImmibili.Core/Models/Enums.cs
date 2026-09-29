namespace AppPhotoImmibili.Core.Models;

/// <summary>Tipologia di ambiente: guida il numero di scatti e i consigli.</summary>
public enum RoomKind
{
    Ingresso,
    Soggiorno,
    Cucina,
    CameraPadronale,
    Camera,
    Cameretta,
    Bagno,
    Studio,
    Lavanderia,
    Corridoio,
    Balcone,
    Terrazzo,
    Giardino,
    Garage,
    Cantina,
    Ufficio,
    SalaRiunioni,
    AreaVendita,
    Esterno,
    Altro,
}

public enum ShotKind
{
    /// <summary>Vista d'insieme dell'ambiente (angolo, tre pareti).</summary>
    Panoramica,
    /// <summary>Dettaglio di pregio: finiture, vista, particolari architettonici.</summary>
    Dettaglio,
}

public enum Severity
{
    Bassa,
    Media,
    Alta,
}

public enum SyncState
{
    /// <summary>Da caricare (anche dopo un errore temporaneo, con attesa crescente).</summary>
    InCoda,
    Caricata,
    /// <summary>Rifiutata dal server (es. token non valido): serve un intervento, poi "Riprova".</summary>
    Errore,
}
