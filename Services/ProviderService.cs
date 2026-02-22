using AtlasHub.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AtlasHub.Services;

public sealed class ProviderService
{
    private readonly ProviderRepository _providers;
    private readonly ProfileProviderRepository _links;
    private readonly CatalogRepository _catalog;
    private readonly M3uImporter _m3u;
    private readonly AppEventBus _bus;

    private readonly ProviderEpgRepository _providerEpg;
    private readonly EpgRepository _epgRepo;
    private readonly XmlTvParser _xmltv;
    private readonly XmlTvDownloader _xmltvDownloader;
    private readonly AppPaths _paths;

    public ProviderService(
        ProviderRepository providers,
        ProfileProviderRepository links,
        CatalogRepository catalog,
        M3uImporter m3u,
        AppEventBus bus,
        ProviderEpgRepository providerEpg,
        EpgRepository epgRepo,
        XmlTvParser xmltv,
        XmlTvDownloader xmltvDownloader,
        AppPaths paths)
    {
        _providers = providers;
        _links = links;
        _catalog = catalog;
        _m3u = m3u;
        _bus = bus;

        _providerEpg = providerEpg;
        _epgRepo = epgRepo;
        _xmltv = xmltv;
        _xmltvDownloader = xmltvDownloader;
        _paths = paths;
    }

    // ----------------------------
    // Public API
    // ----------------------------

    public Task<List<ProviderSource>> GetProvidersAsync()
        => _providers.GetAllAsync();

    public Task<List<ProfileProviderLink>> GetLinksAsync()
        => _links.GetAllAsync();

    public async Task<List<ProviderSource>> GetEnabledProvidersForProfileAsync(string profileId)
    {
        var links = await _links.GetAllAsync();
        var enabledIds = links
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) && x.IsEnabled)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabledIds.Count == 0) return new();

        var allProviders = await _providers.GetAllAsync();
        return allProviders.Where(p => enabledIds.Contains(p.Id)).ToList();
    }

    public Task<CatalogSnapshot?> GetCatalogAsync(string providerId)
        => _catalog.LoadAsync(providerId);

    public async Task SetEnabledAsync(string profileId, string providerId, bool isEnabled)
    {
        await _links.SetEnabledAsync(profileId, providerId, isEnabled);
        _bus.RaiseProvidersChanged();
    }

    public async Task DeleteProviderAsync(string providerId)
    {
        await _providers.DeleteAsync(providerId);
        await _links.DeleteByProviderAsync(providerId);

        await _catalog.DeleteAsync(providerId);
        await _epgRepo.DeleteAsync(providerId);

        _bus.RaiseProvidersChanged();
        _bus.RaiseToast("Kaynak silindi.");
    }

    public async Task AddM3uProviderAsync(
        string profileId,
        string name,
        string? m3uUrl,
        string? m3uFilePath,
        bool enableForProfile,
        ProviderHttpConfig? http)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Kaynak adı boş olamaz.", nameof(name));

        var cfg = new ProviderM3uConfig(
            M3uUrl: string.IsNullOrWhiteSpace(m3uUrl) ? null : m3uUrl.Trim(),
            M3uFilePath: string.IsNullOrWhiteSpace(m3uFilePath) ? null : m3uFilePath.Trim()
        );

        if (string.IsNullOrWhiteSpace(cfg.M3uUrl) && string.IsNullOrWhiteSpace(cfg.M3uFilePath))
            throw new ArgumentException("M3U URL veya dosya yolu girilmelidir.");

        var provider = new ProviderSource(
            Id: Guid.NewGuid().ToString("N"),
            Name: name.Trim(),
            Type: "M3U",
            M3u: cfg,
            Http: NormalizeHttp(http),
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O")
        );

        await _providers.UpsertAsync(provider);

        // SortOrder düzgün olsun
        var existing = await _links.GetAllAsync();
        var maxSort = existing
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.SortOrder)
            .DefaultIfEmpty(-1)
            .Max();

        await _links.UpsertAsync(new ProfileProviderLink(
            ProfileId: profileId,
            ProviderId: provider.Id,
            IsEnabled: enableForProfile,
            SortOrder: maxSort + 1
        ));

        _bus.RaiseProvidersChanged();
        _bus.RaiseToast("Kaynak eklendi.");
    }

    public async Task RefreshCatalogAsync(ProviderSource provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));

        var http = NormalizeHttp(provider.Http);

        // 1) M3U indir/oku + parse
        var m3uText = await LoadM3uTextAsync(provider.M3u, http);
        var snapshot = _m3u.Parse(provider.Id, provider.Name, m3uText);
        await _catalog.SaveAsync(snapshot);

        // 2) EPG URL keşfi: kullanıcı elle ayarlamadıysa header’daki HEPSİ
        var epgCfg = await _providerEpg.GetForProviderAsync(provider.Id);

        var hasManual =
            epgCfg is not null &&
            (!string.IsNullOrWhiteSpace(epgCfg.XmltvUrl) || !string.IsNullOrWhiteSpace(epgCfg.XmltvFilePath));

        if (!hasManual)
        {
            var allUrls = M3uHeaderParser.ExtractEpgUrls(m3uText);
            if (allUrls.Count > 0)
            {
                var joined = M3uHeaderParser.JoinUrls(allUrls);
                await _providerEpg.SetForProviderAsync(provider.Id, joined, null);
                epgCfg = new ProviderEpgConfig(provider.Id, joined, null);
            }
        }

        // 3) EPG indir + parse + merge
        if (epgCfg is not null)
        {
            if (!string.IsNullOrWhiteSpace(epgCfg.XmltvFilePath))
            {
                var xmlSingle = await _xmltvDownloader.LoadXmlTvTextAsync(null, epgCfg.XmltvFilePath, http);
                await SaveSingleEpgAsync(provider.Id, xmlSingle);
            }
            else
            {
                var urls = M3uHeaderParser.SplitJoinedUrls(epgCfg.XmltvUrl);

                if (urls.Count == 0)
                {
                    _bus.RaiseToast("EPG bulunamadı (x-tvg-url yok).");
                }
                else
                {
                    await DownloadMergeAndSaveAllEpgAsync(provider.Id, urls, http);
                }
            }
        }

        _bus.RaiseProvidersChanged();
        _bus.RaiseToast("Katalog güncellendi.");
    }

    // ----------------------------
    // EPG download + merge (soft rate limit + report)
    // ----------------------------

    private sealed record ParsedEpg(string Url, List<EpgProgram> Programs, List<EpgChannel> Channels);

    private async Task DownloadMergeAndSaveAllEpgAsync(string providerId, List<string> urls, ProviderHttpConfig http)
    {
        // Domain bazlı yumuşak rate-limit:
        // epgshare çok paraleli kesebiliyor -> aynı anda az gönderiyoruz
        var epgshareUrls = urls.Where(IsEpgShare).ToList();
        var otherUrls = urls.Where(u => !IsEpgShare(u)).ToList();

        var epgshareParallel = 2; // kritik
        var otherParallel = Math.Clamp(Environment.ProcessorCount, 4, 12);

        var parsedBag = new ConcurrentBag<ParsedEpg>();
        var report = new ConcurrentBag<string>();

        int ok = 0, parseFail = 0, notXml = 0, dlFail = 0;

        async Task ProcessUrl(string url, SemaphoreSlim gate, int startDelayMs)
        {
            if (startDelayMs > 0)
                await Task.Delay(startDelayMs);

            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var xml = await _xmltvDownloader.LoadXmlTvTextAsync(url, null, http).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(xml))
                {
                    Interlocked.Increment(ref dlFail);
                    report.Add($"DL_FAIL {url}");
                    return;
                }

                if (!LooksLikeXmlTv(xml))
                {
                    Interlocked.Increment(ref notXml);
                    report.Add($"NOT_XML {url} (ilk chars: {SafeHead(xml)})");
                    return;
                }

                try
                {
                    var parsed = _xmltv.Parse(xml!);

                    var progs = parsed.Programs ?? new List<EpgProgram>();
                    var chs = parsed.Channels ?? new List<EpgChannel>();

                    parsedBag.Add(new ParsedEpg(url, progs, chs));

                    Interlocked.Increment(ref ok);
                    report.Add($"OK {url} programs={progs.Count} channels={chs.Count}");
                }
                catch
                {
                    Interlocked.Increment(ref parseFail);
                    report.Add($"PARSE_FAIL {url}");
                }
            }
            finally
            {
                gate.Release();
            }
        }

        var tasks = new List<Task>(urls.Count);

        // epgshare: yavaş ve az paralel, başlangıçları yay
        using (var gate1 = new SemaphoreSlim(epgshareParallel, epgshareParallel))
        {
            int i = 0;
            foreach (var url in epgshareUrls)
            {
                tasks.Add(ProcessUrl(url, gate1, startDelayMs: i * 250));
                i++;
            }

            // diğerleri: daha paralel, delay yok
            using var gate2 = new SemaphoreSlim(otherParallel, otherParallel);
            foreach (var url in otherUrls)
                tasks.Add(ProcessUrl(url, gate2, startDelayMs: 0));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var parsedAll = parsedBag.ToList();

        // ✅ Kanal bazında TEK kaynak seçimi
        // Her channelId için hangi URL daha çok program veriyorsa o "kazansın".
        // Tie-break: urls listesinde daha önce gelen URL kazansın.
        var urlOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < urls.Count; i++) urlOrder[urls[i]] = i;

        // channelId -> (url, count)
        var bestSourceByChannel = new Dictionary<string, (string url, int count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var pe in parsedAll)
        {
            // her kaynaktaki programları channelId’ye göre say
            var counts = pe.Programs
                .Where(p => !string.IsNullOrWhiteSpace(p.ChannelId))
                .GroupBy(p => p.ChannelId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (channelId: g.Key, count: g.Count()))
                .ToList();

            foreach (var (channelId, count) in counts)
            {
                if (!bestSourceByChannel.TryGetValue(channelId, out var best))
                {
                    bestSourceByChannel[channelId] = (pe.Url, count);
                    continue;
                }

                if (count > best.count)
                {
                    bestSourceByChannel[channelId] = (pe.Url, count);
                    continue;
                }

                if (count == best.count)
                {
                    // tie-break: earlier url wins
                    var a = urlOrder.TryGetValue(pe.Url, out var ai) ? ai : int.MaxValue;
                    var b = urlOrder.TryGetValue(best.url, out var bi) ? bi : int.MaxValue;
                    if (a < b)
                        bestSourceByChannel[channelId] = (pe.Url, count);
                }
            }
        }

        // Seçilen kaynağa göre programları filtrele:
        var mergedPrograms = new List<EpgProgram>(capacity: parsedAll.Sum(x => x.Programs.Count));
        foreach (var pe in parsedAll)
        {
            foreach (var p in pe.Programs)
            {
                if (string.IsNullOrWhiteSpace(p.ChannelId)) continue;
                var ch = p.ChannelId.Trim();

                if (bestSourceByChannel.TryGetValue(ch, out var best) &&
                    best.url.Equals(pe.Url, StringComparison.OrdinalIgnoreCase))
                {
                    mergedPrograms.Add(p);
                }
            }
        }

        // Son bir dedup (aynı kaynağın kendi iç tekrarları için)
        var dedupedPrograms = mergedPrograms
            .GroupBy(p => $"{p.ChannelId}|{p.StartUtc}|{p.EndUtc}|{p.Title}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Channels: tüm kaynakların channel listelerini birleştir
        var mergedChannels = parsedAll
            .SelectMany(x => x.Channels)
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var names = g
                    .SelectMany(x => x.DisplayNames ?? new List<string>())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new EpgChannel(g.Key, names);
            })
            .ToList();

        var epgSnap = new EpgSnapshot(
            ProviderId: providerId,
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O"),
            Programs: dedupedPrograms,
            Channels: mergedChannels
        );

        await _epgRepo.SaveAsync(epgSnap).ConfigureAwait(false);

        // Debug report dosyası
        try
        {
            var dir = Path.Combine(_paths.Root, "epg");
            Directory.CreateDirectory(dir);

            var reportPath = Path.Combine(dir, $"epg_report_{providerId}.txt");
            var lines = report.OrderBy(x => x).ToArray();
            await File.WriteAllLinesAsync(reportPath, lines);
        }
        catch { }

        _bus.RaiseToast($"EPG: OK={ok}, DL_FAIL={dlFail}, PARSE_FAIL={parseFail}, NOT_XML={notXml}, Programs={dedupedPrograms.Count}, Channels={mergedChannels.Count}");
    }

    private async Task SaveSingleEpgAsync(string providerId, string? xml)
    {
        if (!LooksLikeXmlTv(xml))
        {
            _bus.RaiseToast("EPG indirilemedi (XMLTV değil / erişim engeli).");
            return;
        }

        var parsed = _xmltv.Parse(xml!);

        var epgSnap = new EpgSnapshot(
            ProviderId: providerId,
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O"),
            Programs: parsed.Programs,
            Channels: parsed.Channels
        );

        await _epgRepo.SaveAsync(epgSnap);

        _bus.RaiseToast($"EPG yüklendi: {parsed.Programs.Count} program, {parsed.Channels.Count} kanal");
    }

    private static bool LooksLikeXmlTv(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;

        // BOM (U+FEFF) whitespace sayılmadığı için TrimStart() tek başına yetmiyor.
        var t = xml.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        if (!t.StartsWith("<", StringComparison.Ordinal)) return false;

        if (t.StartsWith("<tv", StringComparison.OrdinalIgnoreCase)) return true;

        if (t.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("<tv", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsEpgShare(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.Host.Contains("epgshare", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return url.Contains("epgshare", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string SafeHead(string s)
    {
        s = s.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (s.Length <= 40) return s.Replace("\r", " ").Replace("\n", " ");
        return s.Substring(0, 40).Replace("\r", " ").Replace("\n", " ") + "...";
    }

    // ----------------------------
    // HTTP / M3U helpers
    // ----------------------------

    private static ProviderHttpConfig NormalizeHttp(ProviderHttpConfig? http)
    {
        var ua = string.IsNullOrWhiteSpace(http?.UserAgent)
            ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            : http!.UserAgent!.Trim();

        var referer = string.IsNullOrWhiteSpace(http?.Referer) ? null : http!.Referer!.Trim();
        var timeout = http?.TimeoutSeconds is > 0 ? http.TimeoutSeconds : 180;

        Dictionary<string, string>? headers = null;
        if (http?.Headers is not null && http.Headers.Count > 0)
        {
            headers = http.Headers
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        return new ProviderHttpConfig(
            UserAgent: ua,
            Referer: referer,
            Headers: headers,
            TimeoutSeconds: timeout
        );
    }

    private static async Task<string> LoadM3uTextAsync(ProviderM3uConfig m3u, ProviderHttpConfig http)
    {
        if (!string.IsNullOrWhiteSpace(m3u.M3uFilePath))
        {
            if (!File.Exists(m3u.M3uFilePath))
                throw new FileNotFoundException("M3U dosyası bulunamadı.", m3u.M3uFilePath);

            return await File.ReadAllTextAsync(m3u.M3uFilePath);
        }

        if (string.IsNullOrWhiteSpace(m3u.M3uUrl))
            throw new InvalidOperationException("M3U URL veya dosya yolu boş.");

        return await DownloadTextAsync(m3u.M3uUrl, http);
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

        if (!string.IsNullOrWhiteSpace(http.UserAgent))
            req.Headers.TryAddWithoutValidation("User-Agent", http.UserAgent);

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

        using var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();

        var bytes = await res.Content.ReadAsByteArrayAsync();

        var charset = res.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                var enc = Encoding.GetEncoding(charset);
                return enc.GetString(bytes);
            }
            catch { }
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
