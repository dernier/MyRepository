using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InquadraCasa.Core.Checklist;
using InquadraCasa.Core.Data;
using InquadraCasa.Core.Models;

namespace InquadraCasa.App.ViewModels;

public sealed record PropertyItem(Property Property, string TemplateName, int Done, int Total)
{
    public string Title => Property.Code;
    public string Subtitle => string.Join(" · ", new[] { Property.Address, Property.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string ProgressText => $"{TemplateName} · {Done}/{Total} panoramiche";
    public double Progress => Total == 0 ? 0 : (double)Done / Total;
}

public partial class PropertiesViewModel(PropertyRepository repository) : ObservableObject
{
    public ObservableCollection<PropertyItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Items.Clear();
            foreach (var p in await repository.GetPropertiesAsync())
            {
                var progress = await repository.GetProgressAsync(p.Id);
                var name = PropertyTemplates.All.FirstOrDefault(t => t.Id == p.TemplateId)?.Name ?? p.TemplateId;
                Items.Add(new PropertyItem(p, name,
                    progress.Sum(r => Math.Min(r.WideTaken, r.Room.RequiredWideShots)),
                    progress.Sum(r => r.Room.RequiredWideShots)));
            }
            IsEmpty = Items.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task NewAsync() => Shell.Current.GoToAsync(Routes.NewProperty);

    [RelayCommand]
    private Task OpenAsync(PropertyItem item) =>
        Shell.Current.GoToAsync($"{Routes.Checklist}?id={item.Property.Id}");

    [RelayCommand]
    private async Task DeleteAsync(PropertyItem item)
    {
        var ok = await Shell.Current.DisplayAlertAsync("Elimina immobile",
            $"Eliminare {item.Title} dall'elenco? Le foto già salvate restano nella cartella dell'app.", "Elimina", "Annulla");
        if (!ok) return;
        await repository.DeletePropertyAsync(item.Property.Id);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
    }
}
