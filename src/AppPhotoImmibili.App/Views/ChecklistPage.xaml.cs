using AppPhotoImmibili.App.ViewModels;

namespace AppPhotoImmibili.App.Views;

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
