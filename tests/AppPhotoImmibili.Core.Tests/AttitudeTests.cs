using AppPhotoImmibili.Core.Analysis;

namespace AppPhotoImmibili.Core.Tests;

public class AttitudeTests
{
    private static double Rad(double d) => d * Math.PI / 180;

    [Fact]
    public void UprightPortrait_IsLevel()
    {
        var a = AttitudeCalculator.FromAccelerometer(0, 1, 0);
        Assert.Equal(0, a.PitchDegrees, 3);
        Assert.Equal(0, a.RollDegrees, 3);
        Assert.Equal(HoldOrientation.Portrait, a.Orientation);
        Assert.True(a.IsLevel(0.5));
    }

    [Fact]
    public void CameraTiltedUp_GivesPositivePitch()
    {
        var a = AttitudeCalculator.FromAccelerometer(0, Math.Cos(Rad(10)), -Math.Sin(Rad(10)));
        Assert.Equal(10, a.PitchDegrees, 3);
        Assert.False(a.IsLevel(1));
    }

    [Fact]
    public void Landscape_RollIsRelativeToNearestOrientation()
    {
        var a = AttitudeCalculator.FromAccelerometer(Math.Cos(Rad(3)), -Math.Sin(Rad(3)), 0);
        Assert.Equal(HoldOrientation.LandscapeTopLeft, a.Orientation);
        Assert.True(a.IsLandscape);
        Assert.Equal(3, a.RollDegrees, 3);

        var b = AttitudeCalculator.FromAccelerometer(-1, 0, 0);
        Assert.Equal(HoldOrientation.LandscapeTopRight, b.Orientation);
        Assert.Equal(0, b.RollDegrees, 3);
    }

    [Fact]
    public void InvertedPlatformSign_IsNormalized()
    {
        var a = AttitudeCalculator.FromAccelerometer(0, -9.81, 0, sign: -1);
        Assert.Equal(HoldOrientation.Portrait, a.Orientation);
    }

    [Fact]
    public void Height_FromPressureDifference()
    {
        // ~0,12 hPa corrispondono a circa un metro
        var h = HeightEstimator.HeightFromPressure(1013.25, 1013.25 - 0.12);
        Assert.InRange(h, 0.95, 1.05);
    }

    [Fact]
    public void HeightEstimator_CalibratesAndClassifies()
    {
        var est = new HeightEstimator();
        Assert.Equal(HeightStatus.NotCalibrated, est.Status);
        est.BeginFloorCalibration();
        for (var i = 0; i < 10; i++) est.Push(1000.0);
        Assert.True(est.EndFloorCalibration());

        // 130 cm ≈ 0,154 hPa di differenza a 1000 hPa
        var target = 1000.0 / Math.Exp(1.30 / 8434.5);
        for (var i = 0; i < 200; i++) est.Push(target);
        Assert.InRange(est.CurrentHeightCm!.Value, 128, 132);
        Assert.Equal(HeightStatus.Ok, est.Status);
    }
}
