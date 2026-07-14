using Microsoft.Extensions.Options;

namespace AeroFlow.Agent.Audio;

/// <summary>
/// File cache keyed by audioAssetId. A hit never touches the network, so repeated
/// announcements play even when core is temporarily unreachable. The extension is
/// derived from the download Content-Type; the id in the file name is what matters.
/// </summary>
public sealed class DiskAudioCache : IAudioCache
{
    private static readonly Dictionary<string, string> ExtensionByContentType = new()
    {
        ["audio/wav"] = "wav",
        ["audio/x-wav"] = "wav",
        ["audio/mpeg"] = "mp3",
        ["audio/ogg"] = "ogg",
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public DiskAudioCache(HttpClient httpClient, IOptions<AgentOptions> options)
    {
        _httpClient = httpClient;
        _cacheDirectory = options.Value.CacheDir;
    }

    public async Task<string> GetFilePathAsync(Guid audioAssetId, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var cached = Directory.EnumerateFiles(_cacheDirectory, $"{audioAssetId}.*").FirstOrDefault();
        if (cached is not null)
        {
            return cached;
        }

        return await DownloadAsync(audioAssetId, cancellationToken);
    }

    private async Task<string> DownloadAsync(Guid audioAssetId, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(
                $"internal/v1/audio-assets/{audioAssetId}/file",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AudioUnavailableException($"Core is unreachable while fetching asset {audioAssetId}.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AudioUnavailableException(
                    $"Core returned {(int)response.StatusCode} for asset {audioAssetId}.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var extension = ExtensionByContentType.GetValueOrDefault(contentType, "bin");
            var targetPath = Path.Combine(_cacheDirectory, $"{audioAssetId}.{extension}");

            // Download to a temp name first so a torn download never looks like a cache hit.
            var tempPath = targetPath + ".downloading";
            await using (var file = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);

            return targetPath;
        }
    }
}
