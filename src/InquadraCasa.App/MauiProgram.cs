using CommunityToolkit.Maui;
using InquadraCasa.App.Services;
using InquadraCasa.App.ViewModels;
using InquadraCasa.App.Views;
using InquadraCasa.Core.Catalog;
using InquadraCasa.Core.Data;
using Microsoft.Extensions.Logging;

namespace InquadraCasa.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitCamera();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var dataDir = FileSystem.AppDataDirectory;
        builder.Services.AddSingleton(new PropertyRepository(Path.Combine(dataDir, "inquadracasa.db3")));
        builder.Services.AddSingleton(new PhotoCatalog(dataDir));
        builder.Services.AddSingleton<ExportService>();
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<SensorService>();
        builder.Services.AddSingleton<PhotoService>();

        builder.Services.AddTransient<PropertiesViewModel>();
        builder.Services.AddTransient<PropertiesPage>();
        builder.Services.AddTransient<NewPropertyViewModel>();
        builder.Services.AddTransient<NewPropertyPage>();
        builder.Services.AddTransient<ChecklistViewModel>();
        builder.Services.AddTransient<ChecklistPage>();
        builder.Services.AddTransient<CameraViewModel>();
        builder.Services.AddTransient<CameraPage>();
        builder.Services.AddTransient<RoomPhotosViewModel>();
        builder.Services.AddTransient<RoomPhotosPage>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
