using AppPhotoImmibili.App.ViewModels;

namespace AppPhotoImmibili.App.Views;

public partial class NewPropertyPage : ContentPage
{
    public NewPropertyPage(NewPropertyViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
