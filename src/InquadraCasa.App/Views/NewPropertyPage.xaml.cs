using InquadraCasa.App.ViewModels;

namespace InquadraCasa.App.Views;

public partial class NewPropertyPage : ContentPage
{
    public NewPropertyPage(NewPropertyViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
