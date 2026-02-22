using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AtlasHub.Services;

public sealed class LogoCacheService
{
    private readonly AppPaths _paths;
    private readonly ConcurrentDictionary<string, Task<string?>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public LogoCacheService(AppPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(_paths.LogoCacheRoot);
    }

    public Task<string?> GetCachedPathAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<string?>(null);

        url = url.Trim();
        return _inflight.GetOrAdd(url, u => DownloadIfNeededAsync(u));
    }

    private async Task<string?> DownloadIfNeededAsync(string url)
    {
        try
        {
            var file = Path.Combine(_paths.LogoCacheRoot, $"{Sha1(url)}.img");

            if (File.Exists(file) && new FileInfo(file).Length > 0)
                return file;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;

            await File.WriteAllBytesAsync(file, bytes);
            return file;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.TryRemove(url, out _);
        }
    }

    private static string Sha1(string s)
    {
        using var sha1 = SHA1.Create();
        var b = sha1.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(b).ToLowerInvariant();
    }
}
