using InquadraCasa.App.ViewModels;

namespace InquadraCasa.App.Views;

public partial class ChecklistPage : ContentPage
{
    private readonly ChecklistViewModel _vm;

    public ChecklistPage(ChecklistViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
