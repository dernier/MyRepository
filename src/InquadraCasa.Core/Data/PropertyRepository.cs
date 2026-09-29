using InquadraCasa.Core.Catalog;
using InquadraCasa.Core.Checklist;
using InquadraCasa.Core.Models;
using SQLite;

namespace InquadraCasa.Core.Data;

/// <summary>Stato di avanzamento di un ambiente nella checklist.</summary>
public sealed record RoomProgress(Room Room, int WideTaken, int DetailsTaken)
{
    public bool IsComplete => WideTaken >= Room.RequiredWideShots;
    public int WideMissing => Math.Max(0, Room.RequiredWideShots - WideTaken);
}

/// <summary>Archivio locale SQLite di immobili, ambienti e foto.</summary>
public sealed class PropertyRepository
{
    private readonly SQLiteAsyncConnection _db;
    private bool _initialized;

    public PropertyRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    private async Task InitAsync()
    {
        if (_initialized) return;
        await _db.CreateTablesAsync<Property, Room, Photo>();
        _initialized = true;
    }

    public async Task<List<Property>> GetPropertiesAsync()
    {
        await InitAsync();
        return await _db.Table<Property>().OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    public async Task<Property?> GetPropertyAsync(int id)
    {
        await InitAsync();
        return await _db.FindAsync<Property>(id);
    }

    /// <summary>Crea l'immobile e genera gli ambienti dal modello di checklist.</summary>
    public async Task<Property> CreatePropertyAsync(string code, string address, string city, string templateId, string notes = "")
    {
        await InitAsync();
        var template = PropertyTemplates.Get(templateId);
        var property = new Property { Code = code.Trim(), Address = address.Trim(), City = city.Trim(), TemplateId = templateId, Notes = notes };
        property.FolderName = NameFormatter.PropertyFolder(property);

        var existing = await _db.Table<Property>().Where(p => p.FolderName.StartsWith(property.FolderName)).CountAsync();
        if (existing > 0) property.FolderName += $"-{existing + 1}";

        await _db.InsertAsync(property);
        await _db.InsertAllAsync(PropertyTemplates.CreateRooms(template, property.Id));
        return property;
    }

    public async Task UpdatePropertyAsync(Property property)
    {
        await InitAsync();
        await _db.UpdateAsync(property);
    }

    public async Task DeletePropertyAsync(int propertyId)
    {
        await InitAsync();
        await _db.RunInTransactionAsync(c =>
        {
            c.Execute("DELETE FROM photos WHERE PropertyId = ?", propertyId);
            c.Execute("DELETE FROM rooms WHERE PropertyId = ?", propertyId);
            c.Delete<Property>(propertyId);
        });
    }

    public async Task<List<Room>> GetRoomsAsync(int propertyId)
    {
        await InitAsync();
        return await _db.Table<Room>().Where(r => r.PropertyId == propertyId).OrderBy(r => r.Order).ToListAsync();
    }

    public async Task<Room?> GetRoomAsync(int roomId)
    {
        await InitAsync();
        return await _db.FindAsync<Room>(roomId);
    }

    /// <summary>Aggiunge un ambiente in fondo al percorso (es. un secondo bagno).</summary>
    public async Task<Room> AddRoomAsync(int propertyId, RoomKind kind, string? name = null)
    {
        await InitAsync();
        var rooms = await GetRoomsAsync(propertyId);
        var t = PropertyTemplates.DefaultFor(kind, name);
        var finalName = t.Name;
        var n = 2;
        while (rooms.Any(r => string.Equals(r.Name, finalName, StringComparison.OrdinalIgnoreCase)))
            finalName = $"{t.Name} {n++}";

        var room = new Room
        {
            PropertyId = propertyId,
            Kind = kind,
            Name = finalName,
            Order = rooms.Count == 0 ? 1 : rooms.Max(r => r.Order) + 1,
            RequiredWideShots = t.WideShots,
            DetailSuggestions = string.Join('|', t.Details),
        };
        await _db.InsertAsync(room);
        return room;
    }

    public async Task DeleteRoomAsync(int roomId)
    {
        await InitAsync();
        await _db.ExecuteAsync("DELETE FROM photos WHERE RoomId = ?", roomId);
        await _db.DeleteAsync<Room>(roomId);
    }

    public async Task<List<Photo>> GetPhotosAsync(int propertyId)
    {
        await InitAsync();
        return await _db.Table<Photo>().Where(p => p.PropertyId == propertyId).ToListAsync();
    }

    public async Task<List<Photo>> GetRoomPhotosAsync(int roomId)
    {
        await InitAsync();
        return await _db.Table<Photo>().Where(p => p.RoomId == roomId).OrderBy(p => p.Kind).ThenBy(p => p.Sequence).ToListAsync();
    }

    public async Task<int> NextSequenceAsync(int roomId, ShotKind kind)
    {
        await InitAsync();
        var photos = await _db.Table<Photo>().Where(p => p.RoomId == roomId && p.Kind == kind).ToListAsync();
        return photos.Count == 0 ? 1 : photos.Max(p => p.Sequence) + 1;
    }

    public async Task SavePhotoAsync(Photo photo)
    {
        await InitAsync();
        if (photo.Id == 0) await _db.InsertAsync(photo);
        else await _db.UpdateAsync(photo);
    }

    public async Task DeletePhotoAsync(Photo photo)
    {
        await InitAsync();
        await _db.DeleteAsync(photo);
    }

    public async Task<List<RoomProgress>> GetProgressAsync(int propertyId)
    {
        var rooms = await GetRoomsAsync(propertyId);
        var photos = await GetPhotosAsync(propertyId);
        return rooms.Select(r => new RoomProgress(r,
            photos.Count(p => p.RoomId == r.Id && p.Kind == ShotKind.Panoramica),
            photos.Count(p => p.RoomId == r.Id && p.Kind == ShotKind.Dettaglio))).ToList();
    }

    /// <summary>Primo ambiente con scatti panoramici mancanti, per il flusso guidato.</summary>
    public async Task<RoomProgress?> NextIncompleteRoomAsync(int propertyId) =>
        (await GetProgressAsync(propertyId)).FirstOrDefault(p => !p.IsComplete);

    public Task CloseAsync() => _db.CloseAsync();
}
