using AppPhotoImmibili.App.ViewModels;

namespace AppPhotoImmibili.App.Views;

public partial class RoomPhotosPage : ContentPage
{
    private readonly RoomPhotosViewModel _vm;

    public RoomPhotosPage(RoomPhotosViewModel vm)
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
