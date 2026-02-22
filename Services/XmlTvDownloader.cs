using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class XmlTvDownloader
{
    // Browser gibi UA (epgshare bazen botları kesiyor)
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public async Task<string?> LoadXmlTvTextAsync(string? xmltvUrl, string? xmltvFilePath, ProviderHttpConfig http)
    {
        if (!string.IsNullOrWhiteSpace(xmltvFilePath))
        {
            if (!File.Exists(xmltvFilePath)) return null;

            var bytes = await File.ReadAllBytesAsync(xmltvFilePath);

            if (xmltvFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || LooksLikeGzip(bytes))
                bytes = DecompressGzip(bytes);

            return Decode(bytes, charset: null);
        }

        if (string.IsNullOrWhiteSpace(xmltvUrl))
            return null;

        return await DownloadTextWithRetryAsync(xmltvUrl.Trim(), http);
    }

    private static async Task<string?> DownloadTextWithRetryAsync(string url, ProviderHttpConfig http)
    {
        // 3 deneme: 0ms, 800ms, 2000ms (basit backoff)
        var delays = new[] { 0, 800, 2000 };

        for (int attempt = 0; attempt < delays.Length; attempt++)
        {
            if (delays[attempt] > 0)
                await Task.Delay(delays[attempt]);

            try
            {
                return await DownloadTextAsync(url, http);
            }
            catch (TaskCanceledException)
            {
                // timeout
            }
            catch (HttpRequestException)
            {
                // network / 403 vs.
            }
            catch
            {
                // diğer durumlar
            }
        }

        return null;
    }

    private static async Task<string> DownloadTextAsync(string url, ProviderHttpConfig http)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(http.TimeoutSeconds, 10, 600))
        };

        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        var ua = string.IsNullOrWhiteSpace(http.UserAgent) ? DefaultUserAgent : http.UserAgent.Trim();
        req.Headers.TryAddWithoutValidation("User-Agent", ua);

        if (!string.IsNullOrWhiteSpace(http.Referer))
            req.Headers.Referrer = new Uri(http.Referer);

        if (http.Headers is not null)
        {
            foreach (var kv in http.Headers)
            {
                if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                {
                    req.Content ??= new StringContent("");
                    req.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        var bytes = await res.Content.ReadAsByteArrayAsync();

        // epgshare: *.xml.gz ama Content-Encoding header’ı olmayabiliyor
        if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || LooksLikeGzip(bytes))
            bytes = DecompressGzip(bytes);

        var charset = res.Content.Headers.ContentType?.CharSet;
        return Decode(bytes, charset);
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                var enc = System.Text.Encoding.GetEncoding(charset);
                return enc.GetString(bytes);
            }
            catch { }
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static bool LooksLikeGzip(byte[] bytes)
        => bytes is { Length: > 2 } && bytes[0] == 0x1F && bytes[1] == 0x8B;

    private static byte[] DecompressGzip(byte[] gzBytes)
    {
        using var input = new MemoryStream(gzBytes);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}
