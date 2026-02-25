using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AtlasHub.Localization;
using AtlasHub.Models;

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
        var links = await _links.GetAllAsync().ConfigureAwait(false);

        var enabledIds = links
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) && x.IsEnabled)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabledIds.Count == 0)
            return new List<ProviderSource>();

        var allProviders = await _providers.GetAllAsync().ConfigureAwait(false);
        return allProviders.Where(p => enabledIds.Contains(p.Id)).ToList();
    }

    public Task<CatalogSnapshot?> GetCatalogAsync(string providerId)
        => _catalog.LoadAsync(providerId);

    public async Task SetEnabledAsync(string profileId, string providerId, bool isEnabled)
    {
        await _links.SetEnabledAsync(profileId, providerId, isEnabled).ConfigureAwait(false);
        _bus.RaiseProvidersChanged();
    }

    public async Task DeleteProviderAsync(string providerId)
    {
        await _providers.DeleteAsync(providerId).ConfigureAwait(false);
        await _links.DeleteByProviderAsync(providerId).ConfigureAwait(false);
        await _catalog.DeleteAsync(providerId).ConfigureAwait(false);
        await _epgRepo.DeleteAsync(providerId).ConfigureAwait(false);

        _bus.RaiseProvidersChanged();
        _bus.RaiseToast(Loc.Svc["Providers.Toast.ProviderDeleted"]);
    }

    /// <summary>
    /// Yeni M3U sağlayıcı ekler.
    /// Artık burada:
    ///  - sağlayıcı kaydediliyor,
    ///  - profil-link ekleniyor,
    ///  - ardından otomatik olarak RefreshCatalogAsync(provider) çağrılıyor.
    /// 
    /// Yani kullanıcı ekstra "Kataloğu yenile" butonuna basmak zorunda değil.
    /// </summary>
    public async Task AddM3uProviderAsync(
        string profileId,
        string name,
        string? m3uUrl,
        string? m3uFilePath,
        bool enableForProfile,
        ProviderHttpConfig? http)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(Loc.Svc["Providers.Error.M3uNameRequired"], nameof(name));

        var cfg = new ProviderM3uConfig(
            M3uUrl: string.IsNullOrWhiteSpace(m3uUrl) ? null : m3uUrl.Trim(),
            M3uFilePath: string.IsNullOrWhiteSpace(m3uFilePath) ? null : m3uFilePath.Trim());

        if (string.IsNullOrWhiteSpace(cfg.M3uUrl) && string.IsNullOrWhiteSpace(cfg.M3uFilePath))
            throw new ArgumentException(Loc.Svc["Providers.Error.M3uSourceRequired"]);

        var provider = new ProviderSource(
            Id: Guid.NewGuid().ToString("N"),
            Name: name.Trim(),
            Type: "M3U",
            M3u: cfg,
            Http: NormalizeHttp(http),
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        // 1) Provider & link kaydı
        await _providers.UpsertAsync(provider).ConfigureAwait(false);

        var existing = await _links.GetAllAsync().ConfigureAwait(false);
        var maxSort = existing
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.SortOrder)
            .DefaultIfEmpty(-1)
            .Max();

        await _links.UpsertAsync(new ProfileProviderLink(
            ProfileId: profileId,
            ProviderId: provider.Id,
            IsEnabled: enableForProfile,
            SortOrder: maxSort + 1)).ConfigureAwait(false);

        // 2) Kullanıcıdan ekstra aksiyon beklemeden:
        //    M3U katalog + EPG keşfi + merge işlemini hemen yap.
        try
        {
            await RefreshCatalogAsync(provider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Katalog/EPG başarısız olsa bile provider eklenmiş durumda olsun.
            // Hata detayını kullanıcıya toast olarak geçelim.
            var fmt = Loc.Svc["Providers.Status.RefreshErrorPrefix"];
            var msg = string.Format(CultureInfo.CurrentCulture, fmt, ex.Message);
            _bus.RaiseToast(msg);

            // Yine de en azından listelerin yenilendiğinden emin olalım.
            _bus.RaiseProvidersChanged();
        }

        // 3) Add işlemi için ayrı bilgi mesajı (isteğe bağlı).
        _bus.RaiseToast(Loc.Svc["Providers.Toast.ProviderAdded"]);
    }

    /// <summary>
    /// Mevcut bir sağlayıcının (ad, M3U, HTTP vs.) güncellenmesi.
    /// </summary>
    public async Task UpdateProviderAsync(ProviderSource updated)
    {
        if (updated is null) throw new ArgumentNullException(nameof(updated));

        await _providers.UpsertAsync(updated).ConfigureAwait(false);
        _bus.RaiseProvidersChanged();
        _bus.RaiseToast(Loc.Svc["Providers.Toast.ProviderUpdated"]);
    }

    public async Task RefreshCatalogAsync(ProviderSource provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));

        var http = NormalizeHttp(provider.Http);

        // 1) M3U indir/oku + parse
        var m3uText = await LoadM3uTextAsync(provider.M3u, http).ConfigureAwait(false);
        var snapshot = _m3u.Parse(provider.Id, provider.Name, m3uText);
        await _catalog.SaveAsync(snapshot).ConfigureAwait(false);

        // 2) EPG URL keşfi: kullanıcı elle ayarlamadıysa header’daki HEPSİ var
        var epgCfg = await _providerEpg.GetForProviderAsync(provider.Id).ConfigureAwait(false);

        var hasManual = epgCfg is not null &&
                        (!string.IsNullOrWhiteSpace(epgCfg.XmltvUrl) ||
                         !string.IsNullOrWhiteSpace(epgCfg.XmltvFilePath));

        if (!hasManual)
        {
            var allUrls = M3uHeaderParser.ExtractEpgUrls(m3uText);
            if (allUrls.Count > 0)
            {
                var joined = M3uHeaderParser.JoinUrls(allUrls);
                await _providerEpg.SetForProviderAsync(provider.Id, joined, null).ConfigureAwait(false);
                epgCfg = new ProviderEpgConfig(provider.Id, joined, null);
            }
        }

        // 3) EPG indir + parse + merge
        if (epgCfg is not null)
        {
            if (!string.IsNullOrWhiteSpace(epgCfg.XmltvFilePath))
            {
                var xmlSingle = await _xmltvDownloader
                    .LoadXmlTvTextAsync(null, epgCfg.XmltvFilePath, http)
                    .ConfigureAwait(false);

                await SaveSingleEpgAsync(provider.Id, xmlSingle).ConfigureAwait(false);
            }
            else
            {
                var urls = M3uHeaderParser.SplitJoinedUrls(epgCfg.XmltvUrl);
                if (urls.Count == 0)
                {
                    _bus.RaiseToast(Loc.Svc["Providers.Toast.EpgNotFound"]);
                }
                else
                {
                    await DownloadMergeAndSaveAllEpgAsync(provider.Id, urls, http).ConfigureAwait(false);
                }
            }
        }

        _bus.RaiseProvidersChanged();
        _bus.RaiseToast(Loc.Svc["Providers.Toast.CatalogUpdated"]);
    }

    // ------------------------------------------------------------
    // EPG download + merge (soft rate limit + report)
    // ------------------------------------------------------------

    private sealed record ParsedEpg(
        string Url,
        List<EpgProgram> Programs,
        List<EpgChannel> Channels);

    private async Task DownloadMergeAndSaveAllEpgAsync(
        string providerId,
        List<string> urls,
        ProviderHttpConfig http)
    {
        var epgshareUrls = urls.Where(IsEpgShare).ToList();
        var otherUrls = urls.Where(u => !IsEpgShare(u)).ToList();

        var epgshareParallel = 2;
        var otherParallel = Math.Clamp(Environment.ProcessorCount, 4, 12);

        var parsedBag = new ConcurrentBag<ParsedEpg>();
        var report = new ConcurrentBag<string>();

        int ok = 0, parseFail = 0, notXml = 0, dlFail = 0;

        async Task ProcessUrl(string url, SemaphoreSlim gate, int startDelayMs)
        {
            if (startDelayMs > 0)
                await Task.Delay(startDelayMs).ConfigureAwait(false);

            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var xml = await _xmltvDownloader
                    .LoadXmlTvTextAsync(url, null, http)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(xml))
                {
                    Interlocked.Increment(ref dlFail);
                    report.Add($"DL_FAIL {url}");
                    return;
                }

                if (!LooksLikeXmlTv(xml))
                {
                    Interlocked.Increment(ref notXml);
                    report.Add($"NOT_XML {url} (head: {SafeHead(xml)})");
                    return;
                }

                try
                {
                    var parsed = _xmltv.Parse(xml);
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

        using (var gate1 = new SemaphoreSlim(epgshareParallel, epgshareParallel))
        using (var gate2 = new SemaphoreSlim(otherParallel, otherParallel))
        {
            int i = 0;
            foreach (var url in epgshareUrls)
            {
                tasks.Add(ProcessUrl(url, gate1, startDelayMs: i * 250));
                i++;
            }

            foreach (var url in otherUrls)
                tasks.Add(ProcessUrl(url, gate2, 0));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var parsedAll = parsedBag.ToList();

        // Kanal bazında tek kaynak seçimi
        var urlOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < urls.Count; i++)
            urlOrder[urls[i]] = i;

        var bestSourceByChannel =
            new Dictionary<string, (string url, int count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var pe in parsedAll)
        {
            var counts = pe.Programs
                .Where(p => !string.IsNullOrWhiteSpace(p.ChannelId))
                .GroupBy(p => p.ChannelId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (channelId: g.Key, count: g.Count()));

            foreach (var (channelId, count) in counts)
            {
                if (!bestSourceByChannel.TryGetValue(channelId, out var best))
                {
                    bestSourceByChannel[channelId] = (pe.Url, count);
                }
                else if (count > best.count)
                {
                    bestSourceByChannel[channelId] = (pe.Url, count);
                }
                else if (count == best.count)
                {
                    var a = urlOrder.TryGetValue(pe.Url, out var ai) ? ai : int.MaxValue;
                    var b = urlOrder.TryGetValue(best.url, out var bi) ? bi : int.MaxValue;
                    if (a < b)
                        bestSourceByChannel[channelId] = (pe.Url, count);
                }
            }
        }

        var mergedPrograms = new List<EpgProgram>(parsedAll.Sum(x => x.Programs.Count));

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

        var dedupedPrograms = mergedPrograms
            .GroupBy(p => $"{p.ChannelId}|{p.StartUtc:O}|{p.EndUtc:O}|{p.Title}",
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

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
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Programs: dedupedPrograms,
            Channels: mergedChannels);

        await _epgRepo.SaveAsync(epgSnap).ConfigureAwait(false);

        try
        {
            var dir = Path.Combine(_paths.Root, "epg");
            Directory.CreateDirectory(dir);
            var reportPath = Path.Combine(dir, $"epg_report_{providerId}.txt");
            var lines = report.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            await File.WriteAllLinesAsync(reportPath, lines).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        var toastFmt = Loc.Svc["Providers.Toast.EpgMergeSummary"];
        var toast = string.Format(
            CultureInfo.CurrentCulture,
            toastFmt,
            ok, dlFail, parseFail, notXml,
            dedupedPrograms.Count,
            mergedChannels.Count);

        _bus.RaiseToast(toast);
    }

    private async Task SaveSingleEpgAsync(string providerId, string? xml)
    {
        if (!LooksLikeXmlTv(xml))
        {
            _bus.RaiseToast(Loc.Svc["Providers.Toast.EpgLoadFailed"]);
            return;
        }

        var parsed = _xmltv.Parse(xml!);

        var epgSnap = new EpgSnapshot(
            ProviderId: providerId,
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Programs: parsed.Programs ?? new List<EpgProgram>(),
            Channels: parsed.Channels ?? new List<EpgChannel>());

        await _epgRepo.SaveAsync(epgSnap).ConfigureAwait(false);

        var fmt = Loc.Svc["Providers.Toast.EpgLoadedSummary"];
        var msg = string.Format(
            CultureInfo.CurrentCulture,
            fmt,
            epgSnap.Programs.Count,
            epgSnap.Channels.Count);

        _bus.RaiseToast(msg);
    }

    private static bool LooksLikeXmlTv(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        string t = xml!;

        t = t.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        if (!t.StartsWith("<", StringComparison.Ordinal))
            return false;

        if (t.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            var idx = t.IndexOf('>');
            if (idx >= 0 && idx + 1 < t.Length)
                t = t[(idx + 1)..].TrimStart();
        }

        if (t.StartsWith("<tv", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("<!DOCTYPE tv", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string SafeHead(string? s, int max = 40)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        s = s.Replace("\r", " ").Replace("\n", " ");
        if (s.Length > max) s = s[..max];
        return s;
    }

    private static bool IsEpgShare(string url)
        => url.Contains("epgshare", StringComparison.OrdinalIgnoreCase)
           || url.Contains("epg.best", StringComparison.OrdinalIgnoreCase);

    // ----------------------------
    // HTTP / M3U helpers
    // ----------------------------

    private static ProviderHttpConfig NormalizeHttp(ProviderHttpConfig? http)
    {
        string ua;
        if (string.IsNullOrWhiteSpace(http?.UserAgent))
        {
            ua = "AtlasHub/1.0 (Windows; WPF)";
        }
        else
        {
            ua = http.UserAgent!.Trim();
        }

        string? referer;
        if (http is null || string.IsNullOrWhiteSpace(http.Referer))
        {
            referer = null;
        }
        else
        {
            referer = http.Referer.Trim();
        }

        var timeout = http?.TimeoutSeconds > 0 ? http.TimeoutSeconds : 180;

        Dictionary<string, string>? headers = null;
        if (http?.Headers is { Count: > 0 } raw)
        {
            headers = raw
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(
                    kv => kv.Key.Trim(),
                    kv => kv.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }

        return new ProviderHttpConfig(
            UserAgent: ua,
            Referer: referer,
            Headers: headers,
            TimeoutSeconds: timeout);
    }

    private static async Task<string> LoadM3uTextAsync(
        ProviderM3uConfig m3u,
        ProviderHttpConfig http)
    {
        if (!string.IsNullOrWhiteSpace(m3u.M3uFilePath))
        {
            if (!File.Exists(m3u.M3uFilePath))
                throw new FileNotFoundException(
                    Loc.Svc["Providers.Error.M3uFileNotFound"],
                    m3u.M3uFilePath);

            return await File.ReadAllTextAsync(m3u.M3uFilePath).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(m3u.M3uUrl))
            throw new InvalidOperationException(Loc.Svc["Providers.Error.M3uUrlOrFileMissing"]);

        return await DownloadTextAsync(m3u.M3uUrl, http).ConfigureAwait(false);
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
                    req.Content ??= new StringContent(string.Empty);
                    req.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        using var res = await client.SendAsync(req).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        var bytes = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var charset = res.Content.Headers.ContentType?.CharSet;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                var enc = Encoding.GetEncoding(charset);
                return enc.GetString(bytes);
            }
            catch
            {
                // charset hatalıysa UTF-8'e düş
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }
}