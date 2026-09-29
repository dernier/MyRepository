using AppPhotoImmibili.App.ViewModels;

namespace AppPhotoImmibili.App.Views;

public partial class PropertiesPage : ContentPage
{
    private readonly PropertiesViewModel _vm;

    public PropertiesPage(PropertiesViewModel vm)
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
