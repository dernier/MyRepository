using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InquadraCasa.App.Controls;
using InquadraCasa.App.Services;
using InquadraCasa.Core.Analysis;
using InquadraCasa.Core.Checklist;
using InquadraCasa.Core.Data;
using InquadraCasa.Core.Imaging;
using InquadraCasa.Core.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace InquadraCasa.App.ViewModels;

public sealed record ReviewMessage(string Icon, string Text, Color Color);

public partial class CameraViewModel(
    PropertyRepository repository,
    PhotoService photos,
    SensorService sensors,
    AppSettings settings) : ObservableObject, IQueryAttributable
{
    private static readonly Color Ok = Color.FromArgb("#3DBE63");
    private static readonly Color Warn = Color.FromArgb("#F2A541");
    private static readonly Color Bad = Color.FromArgb("#E5484D");

    private int _propertyId, _roomId;
    private CancellationTokenSource? _aiCts;

    public FramingOverlay Overlay { get; } = new();
    public ObservableCollection<ReviewMessage> ReviewMessages { get; } = [];
    public SensorService Sensors => sensors;
    public AppSettings Settings => settings;

    public Property? Property { get; private set; }
    public Room? Room { get; private set; }

    [ObservableProperty] public partial ShotKind Kind { get; set; }
    [ObservableProperty] public partial string Label { get; set; } = "";
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string ShotText { get; set; } = "";
    [ObservableProperty] public partial string TipText { get; set; } = "";
    [ObservableProperty] public partial bool ShowTip { get; set; } = true;
    [ObservableProperty] public partial string LevelText { get; set; } = "Livella…";
    [ObservableProperty] public partial Color LevelColor { get; set; } = Color.FromArgb("#F2A541");
    [ObservableProperty] public partial string HeightText { get; set; } = "";
    [ObservableProperty] public partial Color HeightColor { get; set; } = Color.FromArgb("#99000000");
    [ObservableProperty] public partial string OrientationHint { get; set; } = "";
    [ObservableProperty] public partial string MaskText { get; set; } = "Tre pareti";
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    [ObservableProperty] public partial bool IsReviewing { get; set; }
    [ObservableProperty] public partial bool IsProcessing { get; set; }
    [ObservableProperty] public partial bool IsCheckingAi { get; set; }
    [ObservableProperty] public partial ImageSource? ReviewImage { get; set; }
    [ObservableProperty] public partial string ReviewVerdict { get; set; } = "";
    [ObservableProperty] public partial Color ReviewVerdictColor { get; set; } = Color.FromArgb("#3DBE63");

    public PendingShot? Pending { get; private set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _propertyId = Convert.ToInt32(query["propertyId"]);
        _roomId = Convert.ToInt32(query["roomId"]);
        Kind = query.TryGetValue("kind", out var k) ? (ShotKind)k : ShotKind.Panoramica;
        Label = query.TryGetValue("label", out var l) ? l as string ?? "" : "";
    }

    public async Task LoadAsync()
    {
        Property = await repository.GetPropertyAsync(_propertyId);
        Room = await repository.GetRoomAsync(_roomId);
        if (Property is null || Room is null) return;

        Overlay.ToleranceDegrees = settings.LevelToleranceDegrees;
        Overlay.HorizontalFovDegrees = settings.HorizontalFovDegrees;
        Overlay.Mask = Kind == ShotKind.Dettaglio || !settings.ShowCornerMask ? MaskMode.Nessuna : MaskMode.TrePareti;
        MaskText = MaskLabel(Overlay.Mask);
        await RefreshShotTextAsync();
        TipText = Kind == ShotKind.Dettaglio
            ? $"Dettaglio «{Label}»: avvicinati, riempi l'inquadratura con il particolare e cura la messa a fuoco toccando lo schermo."
            : ShootingTips.For(Room.Kind);
        UpdateHeight(sensors.Height.CurrentHeightCm);
    }

    private async Task RefreshShotTextAsync()
    {
        if (Room is null) return;
        Title = Room.Name;
        var roomPhotos = await repository.GetRoomPhotosAsync(Room.Id);
        var wide = roomPhotos.Count(p => p.Kind == ShotKind.Panoramica);
        var n = wide + 1;
        ShotText = Kind == ShotKind.Panoramica
            ? $"Panoramica {n} di {Math.Max(Room.RequiredWideShots, n)}"
            : $"Dettaglio: {Label}";
    }

    public void UpdateAttitude(DeviceAttitude a)
    {
        Overlay.Attitude = a;
        Overlay.HasAttitude = true;
        var level = a.IsLevel(settings.LevelToleranceDegrees);
        LevelColor = Overlay.LevelColor;
        LevelText = level ? "In bolla ✓"
            : Math.Abs(a.PitchDegrees) > Math.Abs(a.RollDegrees)
                ? (a.PitchDegrees > 0 ? $"Inclina in giù {a.PitchDegrees:0.0}°" : $"Inclina in su {-a.PitchDegrees:0.0}°")
                : $"Raddrizza {Math.Abs(a.RollDegrees):0.0}°";
        OrientationHint = Kind == ShotKind.Panoramica && !a.IsLandscape ? "Ruota il telefono in orizzontale" : "";
    }

    public void UpdateHeight(double? cm)
    {
        if (!sensors.HasBarometer)
        {
            HeightText = "Altezza: usa l'orizzonte";
            HeightColor = Color.FromArgb("#99000000");
            return;
        }
        switch (sensors.Height.Status)
        {
            case HeightStatus.NotCalibrated:
                HeightText = "Altezza: tocca per azzerare";
                HeightColor = Color.FromArgb("#99000000");
                break;
            case HeightStatus.TooLow:
                HeightText = $"↑ {cm:0} cm (alza a {settings.HeightMinCm:0}–{settings.HeightMaxCm:0})";
                HeightColor = Warn;
                break;
            case HeightStatus.TooHigh:
                HeightText = $"↓ {cm:0} cm (abbassa a {settings.HeightMinCm:0}–{settings.HeightMaxCm:0})";
                HeightColor = Warn;
                break;
            default:
                HeightText = $"Altezza {cm:0} cm ✓";
                HeightColor = Ok;
                break;
        }
    }

    [RelayCommand]
    private void ToggleMask()
    {
        Overlay.Mask = Overlay.Mask switch
        {
            MaskMode.Nessuna => MaskMode.TrePareti,
            MaskMode.TrePareti => MaskMode.Spigolo,
            _ => MaskMode.Nessuna,
        };
        MaskText = MaskLabel(Overlay.Mask);
    }

    private static string MaskLabel(MaskMode m) => m switch
    {
        MaskMode.TrePareti => "Tre pareti",
        MaskMode.Spigolo => "Spigolo",
        _ => "Nessuna maschera",
    };

    [RelayCommand]
    private void HideTip() => ShowTip = false;

    [RelayCommand]
    private async Task CalibrateHeightAsync()
    {
        if (!sensors.HasBarometer)
        {
            await Shell.Current.DisplayAlertAsync("Altezza di scatto",
                "Questo telefono non ha il barometro. Con il telefono in bolla la linea d'orizzonte è all'altezza della fotocamera: " +
                "per stare tra 120 e 140 cm deve tagliare gli stipiti delle porte (210 cm) a circa 3/5 della loro altezza, " +
                "all'incirca all'altezza della maniglia più una spanna.", "OK");
            return;
        }
        var ok = await Shell.Current.DisplayAlertAsync("Azzera altezza",
            "Appoggia il telefono sul pavimento, premi Azzera e aspetta 3 secondi senza muoverlo.", "Azzera", "Annulla");
        if (!ok) return;
        sensors.Height.BeginFloorCalibration();
        StatusText = "Calibrazione a terra…";
        await Task.Delay(3000);
        StatusText = sensors.Height.EndFloorCalibration()
            ? "Fatto: ora solleva il telefono all'altezza indicata."
            : "Nessuna lettura dal barometro.";
        UpdateHeight(sensors.Height.CurrentHeightCm);
        await Task.Delay(2500);
        StatusText = "";
    }

    public CaptureContext BuildContext() => new(
        Overlay.Attitude.PitchDegrees,
        Overlay.Attitude.RollDegrees,
        sensors.Height.IsCalibrated ? sensors.Height.CurrentHeightCm : null,
        sensors.MaxAngularVelocity(TimeSpan.FromMilliseconds(600)));

    /// <summary>Riceve lo scatto dalla fotocamera ed esegue i controlli immediati; avvia quello AI in parallelo.</summary>
    public async Task ReviewAsync(Stream capture, CaptureContext context)
    {
        if (Property is null || Room is null) return;
        IsProcessing = true;
        try
        {
            DiscardPending();
            Pending = await photos.PrepareAsync(capture, context, Property, Room, Kind, Label);
            ReviewImage = ImageSource.FromFile(Pending.TempPath);
            IsReviewing = true;
            RebuildReview();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Scatto non riuscito", ex.Message, "OK");
            return;
        }
        finally
        {
            IsProcessing = false;
        }

        if (!settings.CheckDistractors) return;
        _aiCts = new CancellationTokenSource();
        IsCheckingAi = true;
        var shot = Pending;
        try
        {
            await photos.CheckDistractorsAsync(shot, _aiCts.Token);
            if (ReferenceEquals(shot, Pending)) RebuildReview();
        }
        catch (OperationCanceledException)
        {
            // scatto scartato nel frattempo
        }
        finally
        {
            IsCheckingAi = false;
        }
    }

    private void RebuildReview()
    {
        if (Pending is null) return;
        ReviewMessages.Clear();
        var s = Pending.Sharpness!;
        ReviewMessages.Add(new(s.IsSharp ? "✓" : "✗", s.Message, s.IsSharp ? Ok : Bad));

        var level = Math.Abs(Pending.Context.PitchDegrees) <= settings.LevelToleranceDegrees &&
                    Math.Abs(Pending.Context.RollDegrees) <= settings.LevelToleranceDegrees;
        ReviewMessages.Add(level
            ? new("✓", "Scatto in bolla: verticali dritte.", Ok)
            : new("!", $"Fuori bolla ({Pending.Context.PitchDegrees:+0.0;-0.0}° / {Pending.Context.RollDegrees:+0.0;-0.0}°): " +
                       (settings.AutoPerspective ? "verrà raddrizzato al salvataggio." : "valuta di riscattare."), Warn));

        if (Pending.Context.HeightCm is { } hc)
        {
            var inRange = hc >= settings.HeightMinCm && hc <= settings.HeightMaxCm;
            ReviewMessages.Add(new(inRange ? "✓" : "!", $"Altezza stimata {hc:0} cm.", inRange ? Ok : Warn));
        }

        var e = Pending.Exposure!;
        if (e.Suggestions.Count == 0)
            ReviewMessages.Add(new("✓", "Esposizione corretta.", Ok));
        foreach (var tip in e.Suggestions)
            ReviewMessages.Add(new(e.BacklightDetected ? "☀" : "!", tip, e.BacklightDetected ? Bad : Warn));

        if (Pending.Distractors is { } d)
        {
            if (d.Items.Count == 0)
                ReviewMessages.Add(new("✓", string.IsNullOrWhiteSpace(d.Summary) ? "Nessun elemento di disturbo." : d.Summary, Ok));
            foreach (var item in d.Items.OrderByDescending(i => i.Severity))
                ReviewMessages.Add(new("⚠", $"{item.Item} ({item.Position}): {item.Action}",
                    item.Severity == Severity.Alta ? Bad : item.Severity == Severity.Media ? Warn : Colors.Gray));
        }
        if (Pending.DistractorError is { } err)
            ReviewMessages.Add(new("i", err, Colors.Gray));

        var report = Pending.Report;
        (ReviewVerdict, ReviewVerdictColor) = report.ShouldRetake
            ? ("Consigliato riscattare prima di cambiare ambiente", Bad)
            : ("Foto valida", Ok);
    }

    [RelayCommand]
    private void Retake()
    {
        DiscardPending();
        IsReviewing = false;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Pending is null || Room is null) return;
        IsProcessing = true;
        try
        {
            _aiCts?.Cancel();
            await photos.CommitAsync(Pending);
            DiscardPending();
            IsReviewing = false;
            await AfterCommitAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Salvataggio non riuscito", ex.Message, "OK");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>Percorso guidato: completato l'ambiente ricorda i dettagli e propone il successivo.</summary>
    private async Task AfterCommitAsync()
    {
        if (Room is null) return;
        if (Kind == ShotKind.Dettaglio)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var progress = (await repository.GetProgressAsync(_propertyId)).First(p => p.Room.Id == Room.Id);
        if (!progress.IsComplete)
        {
            await RefreshShotTextAsync();
            StatusText = Room.Kind == RoomKind.Soggiorno || Room.RequiredWideShots > 1
                ? "Spostati nell'angolo opposto per il prossimo scatto."
                : "";
            return;
        }

        var next = await repository.NextIncompleteRoomAsync(_propertyId);
        const string detail = "Scatta un dettaglio", go = "Prossimo ambiente", list = "Torna alla lista";
        var message = Room.DetailSuggestionList.Count > 0
            ? $"{Room.Name} completato. Non dimenticare i dettagli: {string.Join(", ", Room.DetailSuggestionList)}."
            : $"{Room.Name} completato.";
        var options = new List<string>();
        if (Room.DetailSuggestionList.Count > 0) options.Add(detail);
        if (next is not null) options.Add($"{go}: {next.Room.Name}");
        options.Add(list);

        var choice = await Shell.Current.DisplayActionSheetAsync(message, null, null, options.ToArray());
        if (choice == detail)
        {
            var label = await Shell.Current.DisplayActionSheetAsync("Quale dettaglio?", "Annulla", null, Room.DetailSuggestionList.ToArray());
            if (label is null or "Annulla") return;
            Kind = ShotKind.Dettaglio;
            Label = label;
            await LoadAsync();
        }
        else if (choice?.StartsWith(go) == true && next is not null)
        {
            _roomId = next.Room.Id;
            Kind = ShotKind.Panoramica;
            Label = "";
            ShowTip = true;
            await LoadAsync();
        }
        else
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    public void DiscardPending()
    {
        _aiCts?.Cancel();
        _aiCts = null;
        Pending?.Dispose();
        Pending = null;
        ReviewImage = null;
    }
}
