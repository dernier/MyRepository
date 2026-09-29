using System;

namespace InquadraCasa.Core.Models;

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
