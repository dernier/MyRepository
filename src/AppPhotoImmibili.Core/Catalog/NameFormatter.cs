using System.Globalization;
using System.Text;
using AppPhotoImmibili.Core.Models;

namespace AppPhotoImmibili.Core.Catalog;

/// <summary>Regole di denominazione di cartelle e file: niente rinomina manuale in ufficio.</summary>
public static class NameFormatter
{
    /// <summary>Trasforma un testo in un identificativo sicuro per il file system ("Camera padronale" → "camera-padronale").</summary>
    public static string Slug(string text, int maxLength = 40)
    {
        if (string.IsNullOrWhiteSpace(text)) return "senza-nome";
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastDash = true;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > maxLength) slug = slug[..maxLength].TrimEnd('-');
        return slug.Length == 0 ? "senza-nome" : slug;
    }

    /// <summary>Cartella dell'immobile: "RIF-1234_via-roma-10-milano".</summary>
    public static string PropertyFolder(Property p)
    {
        var code = Slug(p.Code, 24).ToUpperInvariant();
        var place = Slug($"{p.Address} {p.City}", 48);
        return place == "senza-nome" ? code : $"{code}_{place}";
    }

    /// <summary>Cartella dell'ambiente: "03_cucina".</summary>
    public static string RoomFolder(Room r) => $"{r.Order:00}_{Slug(r.Name)}";

    /// <summary>Nome file: "RIF-1234_03_cucina_panoramica_01.jpg" oppure "..._dettaglio_02_vista-dal-balcone.jpg".</summary>
    public static string PhotoFileName(Property p, Room r, ShotKind kind, int sequence, string? label = null, string suffix = "")
    {
        var kindText = kind == ShotKind.Panoramica ? "panoramica" : "dettaglio";
        var labelPart = string.IsNullOrWhiteSpace(label) ? "" : "_" + Slug(label, 30);
        return $"{Slug(p.Code, 24).ToUpperInvariant()}_{r.Order:00}_{Slug(r.Name)}_{kindText}_{sequence:00}{labelPart}{suffix}.jpg";
    }
}
