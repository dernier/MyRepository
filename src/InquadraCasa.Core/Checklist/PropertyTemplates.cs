using System;
using System.Collections.Generic;
using System.Linq;
using InquadraCasa.Core.Models;

namespace InquadraCasa.Core.Checklist;

/// <summary>Ambiente previsto da un modello di checklist.</summary>
public sealed record RoomTemplate(RoomKind Kind, string Name, int WideShots, params string[] Details);

/// <summary>Modello di percorso guidato per una tipologia di immobile.</summary>
public sealed record PropertyTemplate(string Id, string Name, string Description, IReadOnlyList<RoomTemplate> Rooms)
{
    public int TotalWideShots => Rooms.Sum(r => r.WideShots);
}

/// <summary>Catalogo dei percorsi guidati. L'agente può poi aggiungere o togliere ambienti.</summary>
public static class PropertyTemplates
{
    public static RoomTemplate DefaultFor(RoomKind kind, string? name = null) => kind switch
    {
        RoomKind.Ingresso => new(kind, name ?? "Ingresso", 1, "Portoncino blindato", "Pavimentazione"),
        RoomKind.Soggiorno => new(kind, name ?? "Soggiorno", 2, "Camino o elementi di pregio", "Luce naturale dalle finestre", "Pavimentazione"),
        RoomKind.Cucina => new(kind, name ?? "Cucina", 1, "Piano di lavoro ed elettrodomestici", "Rubinetteria"),
        RoomKind.CameraPadronale => new(kind, name ?? "Camera padronale", 2, "Armadio a muro o cabina armadio"),
        RoomKind.Camera => new(kind, name ?? "Camera", 1),
        RoomKind.Cameretta => new(kind, name ?? "Cameretta", 1),
        RoomKind.Bagno => new(kind, name ?? "Bagno", 1, "Doccia o vasca", "Sanitari sospesi e rivestimenti"),
        RoomKind.Studio => new(kind, name ?? "Studio", 1),
        RoomKind.Lavanderia => new(kind, name ?? "Lavanderia", 1),
        RoomKind.Corridoio => new(kind, name ?? "Corridoio", 1),
        RoomKind.Balcone => new(kind, name ?? "Balcone", 1, "Vista dal balcone"),
        RoomKind.Terrazzo => new(kind, name ?? "Terrazzo", 2, "Vista dal terrazzo", "Pavimentazione esterna"),
        RoomKind.Giardino => new(kind, name ?? "Giardino", 2, "Vista della facciata dal giardino"),
        RoomKind.Garage => new(kind, name ?? "Garage", 1, "Porta basculante o automatizzata"),
        RoomKind.Cantina => new(kind, name ?? "Cantina", 1),
        RoomKind.Ufficio => new(kind, name ?? "Ufficio", 2, "Impianti e cablaggi a pavimento"),
        RoomKind.SalaRiunioni => new(kind, name ?? "Sala riunioni", 1),
        RoomKind.AreaVendita => new(kind, name ?? "Area vendita", 2, "Vetrine su strada"),
        RoomKind.Esterno => new(kind, name ?? "Facciata esterna", 1, "Portone d'ingresso"),
        _ => new(kind, name ?? "Ambiente", 1),
    };

    private static RoomTemplate R(RoomKind kind, string? name = null) => DefaultFor(kind, name);

    public static IReadOnlyList<PropertyTemplate> All { get; } =
    [
        new("monolocale", "Monolocale", "Open space con angolo cottura e bagno",
        [
            R(RoomKind.Ingresso), R(RoomKind.Soggiorno, "Zona giorno"), R(RoomKind.Cucina, "Angolo cottura"),
            R(RoomKind.Bagno), R(RoomKind.Balcone),
        ]),
        new("bilocale", "Bilocale", "Soggiorno, cucina, camera e bagno",
        [
            R(RoomKind.Ingresso), R(RoomKind.Soggiorno), R(RoomKind.Cucina),
            R(RoomKind.CameraPadronale, "Camera da letto"), R(RoomKind.Bagno), R(RoomKind.Balcone),
        ]),
        new("trilocale", "Trilocale", "Soggiorno, cucina, due camere, bagno",
        [
            R(RoomKind.Ingresso), R(RoomKind.Soggiorno), R(RoomKind.Cucina),
            R(RoomKind.CameraPadronale), R(RoomKind.Camera), R(RoomKind.Bagno), R(RoomKind.Balcone),
        ]),
        new("quadrilocale", "Quadrilocale", "Soggiorno, cucina, tre camere, due bagni",
        [
            R(RoomKind.Ingresso), R(RoomKind.Soggiorno), R(RoomKind.Cucina),
            R(RoomKind.CameraPadronale), R(RoomKind.Camera), R(RoomKind.Cameretta),
            R(RoomKind.Bagno, "Bagno padronale"), R(RoomKind.Bagno, "Bagno di servizio"),
            R(RoomKind.Corridoio), R(RoomKind.Terrazzo),
        ]),
        new("villa", "Villa / indipendente", "Più livelli con spazi esterni",
        [
            R(RoomKind.Esterno), R(RoomKind.Giardino), R(RoomKind.Ingresso), R(RoomKind.Soggiorno),
            R(RoomKind.Cucina), R(RoomKind.Studio), R(RoomKind.CameraPadronale), R(RoomKind.Camera),
            R(RoomKind.Camera, "Camera 2"), R(RoomKind.Bagno, "Bagno padronale"), R(RoomKind.Bagno, "Bagno di servizio"),
            R(RoomKind.Lavanderia), R(RoomKind.Terrazzo), R(RoomKind.Garage), R(RoomKind.Cantina),
        ]),
        new("ufficio", "Ufficio", "Spazi di lavoro e servizi",
        [
            R(RoomKind.Ingresso, "Reception"), R(RoomKind.Ufficio, "Open space"), R(RoomKind.Ufficio, "Ufficio direzionale"),
            R(RoomKind.SalaRiunioni), R(RoomKind.Bagno), R(RoomKind.Cucina, "Area break"),
        ]),
        new("negozio", "Negozio", "Locale commerciale su strada",
        [
            R(RoomKind.Esterno, "Vetrine e ingresso"), R(RoomKind.AreaVendita), R(RoomKind.Cantina, "Magazzino"), R(RoomKind.Bagno),
        ]),
    ];

    public static PropertyTemplate Get(string id) =>
        All.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"Modello '{id}' non trovato");

    /// <summary>Genera gli ambienti di un immobile a partire dal modello, numerando i nomi duplicati.</summary>
    public static List<Room> CreateRooms(PropertyTemplate template, int propertyId)
    {
        var rooms = new List<Room>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = 1;
        foreach (var t in template.Rooms)
        {
            var name = t.Name;
            if (used.TryGetValue(name, out var n))
            {
                used[name] = ++n;
                name = $"{name} {n}";
            }
            else
            {
                used[name] = 1;
            }

            rooms.Add(new Room
            {
                PropertyId = propertyId,
                Kind = t.Kind,
                Name = name,
                Order = order++,
                RequiredWideShots = t.WideShots,
                DetailSuggestions = string.Join('|', t.Details),
            });
        }
        return rooms;
    }
}
