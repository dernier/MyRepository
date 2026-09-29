using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InquadraCasa.App.Services;
using InquadraCasa.Core.Catalog;
using InquadraCasa.Core.Checklist;
using InquadraCasa.Core.Data;
using InquadraCasa.Core.Models;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace InquadraCasa.App.ViewModels;

public sealed record RoomItem(RoomProgress Progress)
{
    public Room Room => Progress.Room;
    public string Name => Room.Name;
    public bool IsComplete => Progress.IsComplete;
    public string StatusIcon => IsComplete ? "✓" : Progress.WideTaken > 0 ? "◐" : "○";
    public Color StatusColor => IsComplete ? Color.FromArgb("#3DBE63") : Progress.WideTaken > 0 ? Color.FromArgb("#F2A541") : Color.FromArgb("#9CA3AF");
    public string ShotsText => $"Panoramiche {Progress.WideTaken}/{Room.RequiredWideShots} · Dettagli {Progress.DetailsTaken}";
    public string DetailsText => Room.DetailSuggestionList.Count == 0 ? "" : "Dettagli: " + string.Join(", ", Room.DetailSuggestionList);
    public bool HasDetails => Room.DetailSuggestionList.Count > 0;
    public string Tip => ShootingTips.For(Room.Kind);
}

public partial class ChecklistViewModel(PropertyRepository repository, ExportService export, PhotoCatalog catalog, AppSettings settings)
    : ObservableObject, IQueryAttributable
{
    private int _propertyId;

    public ObservableCollection<RoomItem> Rooms { get; } = [];

    [ObservableProperty]
    public partial Property? Property { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string NextStepText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string GeneralTips => string.Join("\n", ShootingTips.General.Select(t => "• " + t));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id)) _propertyId = Convert.ToInt32(id);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Property = await repository.GetPropertyAsync(_propertyId);
        if (Property is null) return;

        var progress = await repository.GetProgressAsync(_propertyId);
        Rooms.Clear();
        foreach (var p in progress) Rooms.Add(new RoomItem(p));

        var done = progress.Sum(r => Math.Min(r.WideTaken, r.Room.RequiredWideShots));
        var total = progress.Sum(r => r.Room.RequiredWideShots);
        var details = progress.Sum(r => r.DetailsTaken);
        Summary = $"{done}/{total} panoramiche · {details} dettagli · {progress.Count(r => r.IsComplete)}/{progress.Count} ambienti completi";
        ProgressValue = total == 0 ? 0 : (double)done / total;
        var next = progress.FirstOrDefault(r => !r.IsComplete);
        NextStepText = next is null
            ? "Percorso completato. Verifica i dettagli di pregio ed esporta per l'ufficio."
            : $"Prossimo: {next.Room.Name} ({next.WideMissing} {(next.WideMissing == 1 ? "panoramica" : "panoramiche")})";
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        var next = await repository.NextIncompleteRoomAsync(_propertyId);
        if (next is null)
        {
            await Shell.Current.DisplayAlertAsync("Percorso completato",
                "Tutte le panoramiche richieste sono state scattate. Puoi aggiungere dettagli o esportare le foto.", "OK");
            return;
        }
        await OpenCameraAsync(next.Room, ShotKind.Panoramica, "");
    }

    [RelayCommand]
    private async Task RoomActionsAsync(RoomItem item)
    {
        const string wide = "Scatta panoramica", detail = "Scatta dettaglio", photos = "Vedi foto", rename = "Rinomina", delete = "Elimina ambiente";
        var choice = await Shell.Current.DisplayActionSheetAsync(item.Name, "Annulla", delete, wide, detail, photos, rename);
        switch (choice)
        {
            case wide:
                await OpenCameraAsync(item.Room, ShotKind.Panoramica, "");
                break;
            case detail:
                await ShootDetailAsync(item);
                break;
            case photos:
                await Shell.Current.GoToAsync($"{Routes.RoomPhotos}?roomId={item.Room.Id}");
                break;
            case rename:
                var name = await Shell.Current.DisplayPromptAsync("Rinomina ambiente", "Nuovo nome", "Salva", "Annulla", initialValue: item.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    await repository.RenameRoomAsync(item.Room.Id, name.Trim());
                    await LoadAsync();
                }
                break;
            case delete:
                if (await Shell.Current.DisplayAlertAsync("Elimina ambiente", $"Eliminare {item.Name} e le sue foto dal catalogo?", "Elimina", "Annulla"))
                {
                    await repository.DeleteRoomAsync(item.Room.Id);
                    await LoadAsync();
                }
                break;
        }
    }

    [RelayCommand]
    private async Task ShootDetailAsync(RoomItem item)
    {
        const string other = "Altro dettaglio…";
        var options = item.Room.DetailSuggestionList.Append(other).ToArray();
        var label = await Shell.Current.DisplayActionSheetAsync("Quale dettaglio?", "Annulla", null, options);
        if (label is null or "Annulla") return;
        if (label == other)
        {
            label = await Shell.Current.DisplayPromptAsync("Dettaglio", "Descrivi il dettaglio (es. parquet, vista, soffitto a volta)", "Scatta", "Annulla");
            if (string.IsNullOrWhiteSpace(label)) return;
        }
        await OpenCameraAsync(item.Room, ShotKind.Dettaglio, label.Trim());
    }

    [RelayCommand]
    private async Task AddRoomAsync()
    {
        var kinds = Enum.GetValues<RoomKind>().ToDictionary(k => PropertyTemplates.DefaultFor(k).Name, k => k);
        var choice = await Shell.Current.DisplayActionSheetAsync("Aggiungi ambiente", "Annulla", null, kinds.Keys.ToArray());
        if (choice is null || !kinds.TryGetValue(choice, out var kind)) return;
        await repository.AddRoomAsync(_propertyId, kind);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Property is null) return;
        IsBusy = true;
        try
        {
            var photos = await repository.GetPhotosAsync(_propertyId);
            if (photos.Count == 0)
            {
                await Shell.Current.DisplayAlertAsync("Nessuna foto", "Scatta almeno una foto prima di esportare.", "OK");
                return;
            }
            var rooms = await repository.GetRoomsAsync(_propertyId);
            var zip = await export.ExportAsync(Property, rooms, photos, Path.Combine(FileSystem.CacheDirectory, "export"),
                settings.IncludeOriginalsInExport);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Foto {Property.Code}",
                File = new ShareFile(zip, "application/zip"),
            });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Esportazione non riuscita", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string PropertyFolderPath => Property is null ? "" : catalog.PropertyDirectory(Property);

    private Task OpenCameraAsync(Room room, ShotKind kind, string label) =>
        Shell.Current.GoToAsync(Routes.Camera, new Dictionary<string, object>
        {
            ["propertyId"] = _propertyId,
            ["roomId"] = room.Id,
            ["kind"] = kind,
            ["label"] = label,
        });
}
