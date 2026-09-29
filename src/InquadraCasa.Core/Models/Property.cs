using System;
using SQLite;

namespace InquadraCasa.Core.Models;

/// <summary>Scheda immobile: contenitore di ambienti e fotografie.</summary>
[Table("properties")]
public class Property
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Codice/riferimento interno dell'agenzia (es. "RIF-1234").</summary>
    [Indexed]
    public string Code { get; set; } = "";

    public string Address { get; set; } = "";

    public string City { get; set; } = "";

    /// <summary>Identificativo del modello di checklist (<see cref="Checklist.PropertyTemplates"/>).</summary>
    public string TemplateId { get; set; } = "";

    public string Notes { get; set; } = "";

    /// <summary>Nome della cartella su disco, stabile anche se l'indirizzo cambia.</summary>
    public string FolderName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Ignore]
    public string DisplayName => string.IsNullOrWhiteSpace(Address) ? Code : $"{Code} · {Address}";
}
