using SQLite;

namespace AppPhotoImmibili.Core.Models;

/// <summary>Ambiente di un immobile con gli scatti richiesti dalla checklist.</summary>
[Table("rooms")]
public class Room
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PropertyId { get; set; }

    public RoomKind Kind { get; set; }

    /// <summary>Nome mostrato e usato per file e cartelle (es. "Camera padronale", "Bagno 2").</summary>
    public string Name { get; set; } = "";

    /// <summary>Posizione nel percorso guidato.</summary>
    public int Order { get; set; }

    public int RequiredWideShots { get; set; } = 1;

    /// <summary>Dettagli suggeriti separati da '|'.</summary>
    public string DetailSuggestions { get; set; } = "";

    [Ignore]
    public IReadOnlyList<string> DetailSuggestionList =>
        DetailSuggestions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
