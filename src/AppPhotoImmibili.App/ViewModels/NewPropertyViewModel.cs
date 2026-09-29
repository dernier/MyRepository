using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppPhotoImmibili.Core.Checklist;
using AppPhotoImmibili.Core.Data;

namespace AppPhotoImmibili.App.ViewModels;

public partial class NewPropertyViewModel(PropertyRepository repository) : ObservableObject
{
    public IReadOnlyList<PropertyTemplate> Templates { get; } = PropertyTemplates.All;

    [ObservableProperty]
    public partial string Code { get; set; } = "";

    [ObservableProperty]
    public partial string Address { get; set; } = "";

    [ObservableProperty]
    public partial string City { get; set; } = "";

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplatePreview))]
    public partial PropertyTemplate? SelectedTemplate { get; set; } = PropertyTemplates.All[1];

    public string TemplatePreview => SelectedTemplate is null
        ? ""
        : string.Join("\n", SelectedTemplate.Rooms.Select(r =>
            $"• {r.Name}: {r.WideShots} {(r.WideShots == 1 ? "scatto" : "scatti")}" +
            (r.Details.Length > 0 ? $" + dettagli ({string.Join(", ", r.Details)})" : "")));

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            await Shell.Current.DisplayAlertAsync("Dati mancanti", "Inserisci il codice di riferimento dell'immobile.", "OK");
            return;
        }
        if (SelectedTemplate is null) return;

        var property = await repository.CreatePropertyAsync(Code, Address, City, SelectedTemplate.Id, Notes);
        // Sostituisce questa pagina con la checklist del nuovo immobile.
        await Shell.Current.GoToAsync($"../{Routes.Checklist}?id={property.Id}");
    }
}
