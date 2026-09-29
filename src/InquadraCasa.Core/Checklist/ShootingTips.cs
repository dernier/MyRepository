using InquadraCasa.Core.Models;

namespace InquadraCasa.Core.Checklist;

/// <summary>Consigli di inquadratura mostrati prima di entrare in un ambiente.</summary>
public static class ShootingTips
{
    public static readonly string[] General =
    [
        "Accendi tutte le luci e apri tende e tapparelle.",
        "Posizionati in un angolo della stanza e inquadra le tre pareti principali.",
        "Tieni il telefono in orizzontale e perfettamente in bolla (linee verdi).",
        "Altezza della fotocamera tra 120 e 140 cm da terra.",
        "Non inquadrare la finestra di fronte: tienila di lato o alle spalle.",
    ];

    public static string For(RoomKind kind) => kind switch
    {
        RoomKind.Soggiorno => "Scatta dall'angolo opposto all'ingresso; il secondo scatto dall'angolo diagonale.",
        RoomKind.Cucina => "Chiudi le ante, libera il piano di lavoro e nascondi strofinacci e detersivi.",
        RoomKind.CameraPadronale or RoomKind.Camera or RoomKind.Cameretta =>
            "Letto rifatto e centrato; il letto deve entrare intero nell'inquadratura.",
        RoomKind.Bagno => "Tavoletta abbassata, asciugamani ordinati, via flaconi dai ripiani. Evita il tuo riflesso nello specchio.",
        RoomKind.Balcone or RoomKind.Terrazzo => "Scatta anche verso l'esterno per valorizzare la vista.",
        RoomKind.Giardino or RoomKind.Esterno => "Scatta con il sole alle spalle; evita auto e bidoni nell'inquadratura.",
        RoomKind.Corridoio => "Scatta dal fondo del corridoio, centrato, per sfruttare la prospettiva.",
        RoomKind.Garage or RoomKind.Cantina => "Accendi tutte le luci; inquadra l'accesso e la profondità del locale.",
        _ => "Inquadra l'ambiente dall'angolo che mostra più superficie calpestabile.",
    };
}
