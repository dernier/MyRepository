using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using InquadraCasa.Core.Ai;
using InquadraCasa.Core.Catalog;
using InquadraCasa.Core.Checklist;
using InquadraCasa.Core.Data;
using InquadraCasa.Core.Models;

namespace InquadraCasa.Core.Tests;

public class CatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inquadracasa-" + Guid.NewGuid().ToString("N"));

    public CatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("Camera padronale", "camera-padronale")]
    [InlineData("Via Garibaldi, 10 – Città", "via-garibaldi-10-citta")]
    [InlineData("  ", "senza-nome")]
    public void Slug_IsFileSystemSafe(string input, string expected) =>
        Assert.Equal(expected, NameFormatter.Slug(input));

    [Fact]
    public void PhotoNames_AreStructured()
    {
        var p = new Property { Code = "rif 1234", Address = "Via Roma 10", City = "Milano" };
        p.FolderName = NameFormatter.PropertyFolder(p);
        var r = new Room { Name = "Cucina", Order = 3 };
        var catalog = new PhotoCatalog(_dir);

        var (final, original) = catalog.PhotoPaths(p, r, ShotKind.Dettaglio, 2, "Piano di lavoro");
        Assert.Equal("RIF-1234_via-roma-10-milano", p.FolderName);
        Assert.Equal("Immobili/RIF-1234_via-roma-10-milano/03_cucina/RIF-1234_03_cucina_dettaglio_02_piano-di-lavoro.jpg", final);
        Assert.Equal("Immobili/RIF-1234_via-roma-10-milano/03_cucina/_originali/RIF-1234_03_cucina_dettaglio_02_piano-di-lavoro.jpg", original);
    }

    [Fact]
    public void Templates_NumberDuplicateRooms()
    {
        var t = new PropertyTemplate("x", "X", "", [PropertyTemplates.DefaultFor(RoomKind.Bagno), PropertyTemplates.DefaultFor(RoomKind.Bagno)]);
        var rooms = PropertyTemplates.CreateRooms(t, 1);
        Assert.Equal(["Bagno", "Bagno 2"], rooms.Select(r => r.Name));
        Assert.Equal([1, 2], rooms.Select(r => r.Order));
    }

    [Fact]
    public void Bilocale_MatchesExpectedShots()
    {
        var t = PropertyTemplates.Get("bilocale");
        var soggiorno = t.Rooms.Single(r => r.Kind == RoomKind.Soggiorno);
        Assert.Equal(2, soggiorno.WideShots);
        Assert.Equal(1, t.Rooms.Single(r => r.Kind == RoomKind.Bagno).WideShots);
    }

    [Fact]
    public async Task Repository_TracksProgressAndExports()
    {
        var repo = new PropertyRepository(Path.Combine(_dir, "test.db3"));
        var property = await repo.CreatePropertyAsync("RIF-1", "Via Roma 1", "Torino", "bilocale");
        var rooms = await repo.GetRoomsAsync(property.Id);
        Assert.Equal(PropertyTemplates.Get("bilocale").Rooms.Count, rooms.Count);

        var extra = await repo.AddRoomAsync(property.Id, RoomKind.Bagno);
        Assert.Equal("Bagno 2", extra.Name);

        var catalog = new PhotoCatalog(_dir);
        var soggiorno = rooms.Single(r => r.Kind == RoomKind.Soggiorno);
        for (var i = 1; i <= 2; i++)
        {
            Assert.Equal(i, await repo.NextSequenceAsync(soggiorno.Id, ShotKind.Panoramica));
            var (final, _) = catalog.PhotoPaths(property, soggiorno, ShotKind.Panoramica, i);
            Directory.CreateDirectory(Path.GetDirectoryName(catalog.Absolute(final))!);
            await File.WriteAllBytesAsync(catalog.Absolute(final), [0xFF, 0xD8, 0xFF, 0xD9]);
            await repo.SavePhotoAsync(new Photo { PropertyId = property.Id, RoomId = soggiorno.Id, Kind = ShotKind.Panoramica, Sequence = i, RelativePath = final, IsSharp = true });
        }

        var progress = await repo.GetProgressAsync(property.Id);
        Assert.True(progress.Single(p => p.Room.Id == soggiorno.Id).IsComplete);
        var next = await repo.NextIncompleteRoomAsync(property.Id);
        Assert.Equal("Ingresso", next!.Room.Name);

        var zip = await new ExportService(catalog).ExportAsync(property, await repo.GetRoomsAsync(property.Id),
            await repo.GetPhotosAsync(property.Id), Path.Combine(_dir, "export"));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "manifest.json");
        Assert.Contains(archive.Entries, e => e.FullName.EndsWith("_soggiorno_panoramica_02.jpg"));

        await repo.CloseAsync();
    }

    [Fact]
    public void DistractorReport_ParsesStructuredOutput()
    {
        const string json = """
            {"elementi":[{"elemento":"Tavoletta del WC alzata","posizione":"in basso a destra","gravita":"Alta","azione":"Abbassa tavoletta e coperchio"}],
             "giudizio":"Quasi pronta"}
            """;
        var report = DistractorDetector.Parse(json);
        Assert.Single(report.Items);
        Assert.Equal(Severity.Alta, report.Items[0].Severity);
        Assert.True(report.HasBlocking);
        Assert.Equal(report, DistractorDetector.Parse(DistractorDetector.Serialize(report)) with { Items = report.Items });
    }

    [Fact]
    public void FallbackParam_SerializesAsDefault()
    {
        Anthropic.Models.Beta.Messages.BetaFallbacksParam p = new Anthropic.Models.Beta.Messages.Default();
        Assert.Equal("\"default\"", System.Text.Json.JsonSerializer.Serialize(p));
    }
}
