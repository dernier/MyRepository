using System;
using InquadraCasa.App.ViewModels;
using Microsoft.Maui.Controls;

namespace InquadraCasa.App.Views;

public partial class NewPropertyPage : ContentPage
{
    public NewPropertyPage(NewPropertyViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
