using System.Net;
using AeroFlow.Agent.Audio;
using Microsoft.Extensions.Options;

namespace AeroFlow.Agent.Tests;

public sealed class DiskAudioCacheTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"aeroflow-agent-tests-{Guid.NewGuid()}");

    [Fact]
    public async Task DownloadsOnMissAndSavesWithContentTypeExtension()
    {
        var assetId = Guid.NewGuid();
        var handler = new FakeHttpHandler(_ => WavResponse([1, 2, 3]));
        var cache = Cache(handler);

        var path = await cache.GetFilePathAsync(assetId, CancellationToken.None);

        Assert.Equal(Path.Combine(_cacheDir, $"{assetId}.wav"), path);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task HitDoesNotTouchTheNetwork()
    {
        var assetId = Guid.NewGuid();
        var handler = new FakeHttpHandler(_ => WavResponse([1]));
        var cache = Cache(handler);

        var first = await cache.GetFilePathAsync(assetId, CancellationToken.None);
        var second = await cache.GetFilePathAsync(assetId, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task UnknownAssetRaisesAudioUnavailable()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cache = Cache(handler);

        var exception = await Assert.ThrowsAsync<AudioUnavailableException>(
            () => cache.GetFilePathAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("404", exception.Message);
    }

    private DiskAudioCache Cache(FakeHttpHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://core.test/") },
            Options.Create(new AgentOptions { CacheDir = _cacheDir }));

    private static HttpResponseMessage WavResponse(byte[] payload)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");

        return response;
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;

            return Task.FromResult(respond(request));
        }
    }
}
