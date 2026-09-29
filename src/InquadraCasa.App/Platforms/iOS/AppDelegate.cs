using System;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace InquadraCasa.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
