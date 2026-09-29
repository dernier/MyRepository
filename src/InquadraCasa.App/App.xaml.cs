using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace InquadraCasa.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
