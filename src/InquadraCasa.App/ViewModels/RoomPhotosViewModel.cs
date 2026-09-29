using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InquadraCasa.App.Services;
using InquadraCasa.Core.Data;
using InquadraCasa.Core.Models;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace InquadraCasa.App.ViewModels;

public sealed record PhotoItem(Photo Photo, ImageSource Image)
{
    public string Title => Photo.Kind == ShotKind.Panoramica
        ? $"Panoramica {Photo.Sequence}"
        : $"Dettaglio {Photo.Sequence}" + (string.IsNullOrWhiteSpace(Photo.Label) ? "" : $" · {Photo.Label}");

    public string Badges => string.Join("  ", new[]
    {
        Photo.IsSharp ? "✓ nitida" : "✗ sfocata",
        Photo.BacklightDetected ? "☀ controluce" : null,
        Photo.PerspectiveCorrected ? "⟂ raddrizzata" : null,
        Photo.HeightCm is { } h ? $"↕ {h:0} cm" : null,
        !string.IsNullOrEmpty(Photo.DistractorsJson) && Photo.DistractorsJson.Contains("\"elemento\"") ? "⚠ disturbi" : null,
    }.Where(b => b is not null));

    public string FileName => Path.GetFileName(Photo.RelativePath);
}

public partial class RoomPhotosViewModel(PropertyRepository repository, PhotoService photos) : ObservableObject, IQueryAttributable
{
    private int _roomId;

    public ObservableCollection<PhotoItem> Items { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("roomId", out var id)) _roomId = Convert.ToInt32(id);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var room = await repository.GetRoomAsync(_roomId);
        Title = room?.Name ?? "Foto";
        Items.Clear();
        foreach (var p in await repository.GetRoomPhotosAsync(_roomId))
            Items.Add(new PhotoItem(p, ImageSource.FromFile(photos.AbsolutePath(p))));
    }

    [RelayCommand]
    private async Task DeleteAsync(PhotoItem item)
    {
        if (!await Shell.Current.DisplayAlertAsync("Elimina foto", $"Eliminare {item.FileName}?", "Elimina", "Annulla")) return;
        await photos.DeletePhotoAsync(item.Photo);
        Items.Remove(item);
    }

    [RelayCommand]
    private Task ShareAsync(PhotoItem item) =>
        Share.Default.RequestAsync(new ShareFileRequest(item.FileName, new ShareFile(photos.AbsolutePath(item.Photo))));
}
