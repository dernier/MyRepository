using System.Net;
using AppPhotoImmibili.Core.Catalog;
using AppPhotoImmibili.Core.Data;
using AppPhotoImmibili.Core.Imaging;
using AppPhotoImmibili.Core.Models;
using AppPhotoImmibili.Core.Sync;
using SkiaSharp;

namespace AppPhotoImmibili.Core.Tests;

public class SyncTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "appphotoimmibili-sync-" + Guid.NewGuid().ToString("N"));
    private PropertyRepository _repo = null!;
    private PhotoCatalog _catalog = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _repo = new PropertyRepository(Path.Combine(_dir, "test.db3"));
        _catalog = new PhotoCatalog(_dir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _repo.CloseAsync();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private sealed class FakeUploader(params UploadOutcome[] outcomes) : IPhotoUploader
    {
        public List<PhotoUpload> Uploads { get; } = [];

        public Task<UploadOutcome> UploadAsync(PhotoUpload upload, CancellationToken ct = default)
        {
            Uploads.Add(upload);
            return Task.FromResult(outcomes[Math.Min(Uploads.Count - 1, outcomes.Length - 1)]);
        }
    }

    private async Task<Photo> AddPhotoAsync(Property p, Room r, int seq)
    {
        var (final, original) = _catalog.PhotoPaths(p, r, ShotKind.Panoramica, seq);
        using var bmp = new SKBitmap(3000, 2000);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.Beige);
        ImageIo.SaveJpeg(bmp, _catalog.Absolute(final));
        var photo = new Photo { PropertyId = p.Id, RoomId = r.Id, Sequence = seq, RelativePath = final, OriginalRelativePath = original };
        await _repo.SavePhotoAsync(photo);
        return photo;
    }

    [Fact]
    public async Task Upload_SendsWebVersionWithMetadata_AndMarksUploaded()
    {
        var p = await _repo.CreatePropertyAsync("RIF-9", "Via Po 1", "Torino", "bilocale");
        var room = (await _repo.GetRoomsAsync(p.Id))[0];
        var photo = await AddPhotoAsync(p, room, 1);

        var uploader = new FakeUploader(new UploadOutcome(UploadStatus.Ok));
        var result = await new SyncEngine(_repo, _catalog, uploader) { Agent = "Anna" }.RunOnceAsync();

        Assert.Equal(1, result.Uploaded);
        var upload = Assert.Single(uploader.Uploads);
        Assert.Equal("RIF-9", upload.Metadata.PropertyCode);
        Assert.Equal(room.Name, upload.Metadata.RoomName);
        Assert.Equal(photo.Uid, upload.Metadata.Uid);
        using var web = SKBitmap.Decode(upload.Jpeg);
        Assert.Equal(2048, web.Width);

        var saved = (await _repo.GetPhotosAsync(p.Id)).Single();
        Assert.Equal(SyncState.Caricata, saved.SyncState);
        Assert.NotNull(saved.UploadedAt);
        Assert.Equal((0, 1, 0), await _repo.GetSyncSummaryAsync());
    }

    [Fact]
    public async Task TemporaryError_PostponesWithBackoff_AndStopsQueue()
    {
        var p = await _repo.CreatePropertyAsync("RIF-10", "", "", "bilocale");
        var room = (await _repo.GetRoomsAsync(p.Id))[0];
        await AddPhotoAsync(p, room, 1);
        await AddPhotoAsync(p, room, 2);

        var now = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        var uploader = new FakeUploader(new UploadOutcome(UploadStatus.Temporaneo, "rete"));
        var engine = new SyncEngine(_repo, _catalog, uploader) { UtcNow = () => now };
        var result = await engine.RunOnceAsync();

        Assert.Equal(1, result.Postponed);
        Assert.Single(uploader.Uploads);
        var first = (await _repo.GetPhotosAsync(p.Id)).OrderBy(x => x.Sequence).First();
        Assert.Equal(SyncState.InCoda, first.SyncState);
        Assert.Equal(now.AddMinutes(1), first.NextSyncAttemptAt);

        // La foto rimandata non viene ripresa prima dell'attesa; la seconda sì.
        Assert.Single(await _repo.GetPhotosToSyncAsync(now.AddSeconds(30)));
        Assert.Equal(2, (await _repo.GetPhotosToSyncAsync(now.AddMinutes(2))).Count);
    }

    [Fact]
    public async Task PermanentError_MarksFailed_UntilRetry()
    {
        var p = await _repo.CreatePropertyAsync("RIF-11", "", "", "bilocale");
        var room = (await _repo.GetRoomsAsync(p.Id))[0];
        await AddPhotoAsync(p, room, 1);

        var uploader = new FakeUploader(new UploadOutcome(UploadStatus.Definitivo, "negato", StopQueue: true));
        await new SyncEngine(_repo, _catalog, uploader).RunOnceAsync();
        Assert.Equal((0, 0, 1), await _repo.GetSyncSummaryAsync());

        Assert.Equal(1, await _repo.RetrySyncAsync());
        Assert.Equal((1, 0, 0), await _repo.GetSyncSummaryAsync());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 4)]
    [InlineData(10, 60)]
    public void Backoff_GrowsUpToOneHour(int attempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), SyncEngine.Backoff(attempts));

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    [Fact]
    public async Task AgencyUploader_PostsMultipartWithAuthAndIdempotencyKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        var uploader = new AgencyUploader(new HttpClient(handler),
            new AgencyUploaderOptions { BaseUrl = "https://gestionale.example/api/", Token = "t0k" });
        var meta = new PhotoMetadata { Uid = "u1", PropertyCode = "RIF 12", RoomName = "Cucina" };

        var outcome = await uploader.UploadAsync(new PhotoUpload(meta, "foto.jpg", [0xFF, 0xD8, 0xFF, 0xD9]));

        Assert.Equal(UploadStatus.Ok, outcome.Status);
        Assert.Equal("https://gestionale.example/api/immobili/RIF%2012/foto", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("u1", handler.Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("name=metadati", handler.Body);
        Assert.Contains("\"ambiente\":\"Cucina\"", handler.Body);
        Assert.Contains("filename=foto.jpg", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, UploadStatus.Ok)]
    [InlineData(HttpStatusCode.Unauthorized, UploadStatus.Definitivo)]
    [InlineData(HttpStatusCode.TooManyRequests, UploadStatus.Temporaneo)]
    [InlineData(HttpStatusCode.BadGateway, UploadStatus.Temporaneo)]
    public async Task AgencyUploader_ClassifiesResponses(HttpStatusCode code, UploadStatus expected)
    {
        var uploader = new AgencyUploader(new HttpClient(new RecordingHandler(code)),
            new AgencyUploaderOptions { BaseUrl = "https://gestionale.example/api" });
        var outcome = await uploader.UploadAsync(new PhotoUpload(new PhotoMetadata { Uid = "u", PropertyCode = "X" }, "a.jpg", [1]));
        Assert.Equal(expected, outcome.Status);
    }

    [Fact]
    public async Task AgencyUploader_RequiresHttps()
    {
        var uploader = new AgencyUploader(new HttpClient(new RecordingHandler(HttpStatusCode.OK)),
            new AgencyUploaderOptions { BaseUrl = "http://gestionale.example" });
        var outcome = await uploader.UploadAsync(new PhotoUpload(new PhotoMetadata(), "a.jpg", [1]));
        Assert.Equal(UploadStatus.Definitivo, outcome.Status);
    }
}
