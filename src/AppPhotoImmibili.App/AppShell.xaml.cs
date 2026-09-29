using AppPhotoImmibili.App.Views;

namespace AppPhotoImmibili.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.NewProperty, typeof(NewPropertyPage));
        Routing.RegisterRoute(Routes.Checklist, typeof(ChecklistPage));
        Routing.RegisterRoute(Routes.Camera, typeof(CameraPage));
        Routing.RegisterRoute(Routes.RoomPhotos, typeof(RoomPhotosPage));
    }
}

public static class Routes
{
    public const string NewProperty = "nuovo-immobile";
    public const string Checklist = "checklist";
    public const string Camera = "fotocamera";
    public const string RoomPhotos = "foto-ambiente";
}
