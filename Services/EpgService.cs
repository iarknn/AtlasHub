using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class EpgService
{
    private static readonly ConditionalWeakTable<EpgSnapshot, EpgIndex> _indexCache = new();
    private static readonly ConditionalWeakTable<EpgSnapshot, ConcurrentDictionary<string, string>> _mapCache = new();

    // ------------------------------------------------------------
    // Now / Next
    // ------------------------------------------------------------

    public (EpgProgram? now, EpgProgram? next) GetNowNext(EpgSnapshot epg, LiveChannel channel, DateTimeOffset nowUtc)
    {
        if (epg is null) return (null, null);
        if (channel is null) return (null, null);

        // Güvenlik: nowUtc yanlışlıkla local geldiyse UTC’ye normalize et
        nowUtc = nowUtc.ToUniversalTime();

        var index = _indexCache.GetValue(epg, BuildIndex);
        if (index.AllChannelIds.Count == 0) return (null, null);

        var resolvedChannelId = ResolveChannelIdCached(epg, index, channel);
        if (string.IsNullOrWhiteSpace(resolvedChannelId)) return (null, null);

        if (!index.ByRaw.TryGetValue(resolvedChannelId, out var list) || list.Count == 0)
            return (null, null);

        return PickNowNext(list, nowUtc);
    }

    // ------------------------------------------------------------
    // Timeline (Sprint 3-02)
    // ------------------------------------------------------------

    public IReadOnlyList<EpgProgram> GetTimeline(
        EpgSnapshot epg,
        LiveChannel channel,
        DateTimeOffset nowUtc,
        TimeSpan pastWindow,
        TimeSpan futureWindow)
    {
        if (epg is null) return Array.Empty<EpgProgram>();
        if (channel is null) return Array.Empty<EpgProgram>();

        nowUtc = nowUtc.ToUniversalTime();

        if (pastWindow < TimeSpan.Zero) pastWindow = TimeSpan.Zero;
        if (futureWindow < TimeSpan.Zero) futureWindow = TimeSpan.Zero;

        var from = nowUtc - pastWindow;
        var to = nowUtc + futureWindow;

        var index = _indexCache.GetValue(epg, BuildIndex);
        if (index.AllChannelIds.Count == 0) return Array.Empty<EpgProgram>();

        var resolvedChannelId = ResolveChannelIdCached(epg, index, channel);
        if (string.IsNullOrWhiteSpace(resolvedChannelId)) return Array.Empty<EpgProgram>();

        if (!index.ByRaw.TryGetValue(resolvedChannelId, out var list) || list.Count == 0)
            return Array.Empty<EpgProgram>();

        // list sorted. Window filter.
        var windowed = new List<EpgProgram>(256);
        foreach (var p in list)
        {
            if (p.EndUtc <= from) continue;
            if (p.StartUtc >= to) break; // sorted => early exit
            windowed.Add(p);
        }

        // ✅ SAFE dedup: sadece net duplicate (aynı start/end/title)
        return DedupTimelineProgramsStrict(windowed);
    }

    public IReadOnlyList<(EpgProgram program, bool isNow, int progress)> GetTimelineItems(
        EpgSnapshot epg,
        LiveChannel channel,
        DateTimeOffset nowUtc,
        TimeSpan pastWindow,
        TimeSpan futureWindow)
    {
        nowUtc = nowUtc.ToUniversalTime();

        var programs = GetTimeline(epg, channel, nowUtc, pastWindow, futureWindow);
        if (programs.Count == 0) return Array.Empty<(EpgProgram, bool, int)>();

        var items = new List<(EpgProgram, bool, int)>(programs.Count);

        foreach (var p in programs)
        {
            var isNow = p.StartUtc <= nowUtc && p.EndUtc > nowUtc;

            int prog = 0;
            if (isNow)
            {
                var total = (p.EndUtc - p.StartUtc).TotalSeconds;
                if (total > 0)
                {
                    var done = (nowUtc - p.StartUtc).TotalSeconds;
                    prog = (int)Math.Clamp(done / total * 100, 0, 100);
                }
            }

            items.Add((p, isNow, prog));
        }

        return items;
    }

    // ------------------------------------------------------------
    // Channel resolve + caching
    // ------------------------------------------------------------

    private static string ResolveChannelIdCached(EpgSnapshot epg, EpgIndex index, LiveChannel channel)
    {
        var channelKey = BuildChannelCacheKey(channel);
        var map = _mapCache.GetValue(epg, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        if (map.TryGetValue(channelKey, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
            return resolved;

        resolved = ResolveBestChannelId(index, channel) ?? "";
        if (!string.IsNullOrWhiteSpace(resolved))
            map[channelKey] = resolved;

        return resolved;
    }

    private static string BuildChannelCacheKey(LiveChannel ch)
        => !string.IsNullOrWhiteSpace(ch.TvgId)
            ? "id:" + ch.TvgId.Trim()
            : "name:" + (ch.Name ?? "").Trim();

    // ------------------------------------------------------------
    // Index build
    // ------------------------------------------------------------

    private static EpgIndex BuildIndex(EpgSnapshot epg)
    {
        var byRaw = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in epg.Programs)
        {
            if (string.IsNullOrWhiteSpace(p.ChannelId)) continue;

            var raw = p.ChannelId.Trim();
            allIds.Add(raw);

            if (!byRaw.TryGetValue(raw, out var listRaw))
            {
                listRaw = new List<EpgProgram>(256);
                byRaw[raw] = listRaw;
            }
            listRaw.Add(p);
        }

        foreach (var kv in byRaw)
            kv.Value.Sort(static (a, b) => a.StartUtc.CompareTo(b.StartUtc));

        // display-name index (normalized display-name -> channelId)
        var byDisplayNameNorm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (epg.Channels is not null)
        {
            foreach (var ch in epg.Channels)
            {
                if (string.IsNullOrWhiteSpace(ch.Id)) continue;
                if (ch.DisplayNames is null) continue;

                foreach (var dn in ch.DisplayNames)
                {
                    var n = NormalizeKey(dn);
                    if (n.Length == 0) continue;

                    // aynı display-name birden fazla id’ye gidebilir, ilkini tutuyoruz
                    if (!byDisplayNameNorm.ContainsKey(n))
                        byDisplayNameNorm[n] = ch.Id.Trim();
                }
            }
        }

        // normalized id list (fuzzy için)
        var allNorm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in allIds)
        {
            var n = NormalizeKey(id);
            if (n.Length == 0) continue;

            if (!allNorm.TryGetValue(n, out var list))
            {
                list = new List<string>(1);
                allNorm[n] = list;
            }
            list.Add(id);
        }

        return new EpgIndex(byRaw, allIds, allNorm, byDisplayNameNorm);
    }

    private sealed record EpgIndex(
        Dictionary<string, List<EpgProgram>> ByRaw,
        HashSet<string> AllChannelIds,
        Dictionary<string, List<string>> AllChannelIdsByNormalized,
        Dictionary<string, string> ByDisplayNameNormalized
    );

    // ------------------------------------------------------------
    // Channel resolve strategy
    // ------------------------------------------------------------

    private static string? ResolveBestChannelId(EpgIndex index, LiveChannel channel)
    {
        var rawCandidates = new List<string>(8);

        if (!string.IsNullOrWhiteSpace(channel.TvgId))
        {
            var tid = channel.TvgId.Trim();
            rawCandidates.Add(tid);
            rawCandidates.Add(RemoveDotCountrySuffix(tid));
            rawCandidates.Add(RemoveDashCountrySuffix(tid));
        }

        if (!string.IsNullOrWhiteSpace(channel.Name))
        {
            rawCandidates.Add(channel.Name.Trim());
            rawCandidates.Add(StripQualityTokens(channel.Name));
        }

        rawCandidates = rawCandidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 1) Raw birebir
        foreach (var r in rawCandidates)
            if (index.ByRaw.ContainsKey(r))
                return r;

        // 2) display-name üzerinden bul (TRT 1 -> xmltv channel id)
        if (!string.IsNullOrWhiteSpace(channel.Name))
        {
            var dnKey = NormalizeKey(channel.Name);
            if (dnKey.Length > 0 && index.ByDisplayNameNormalized.TryGetValue(dnKey, out var idFromName))
                return idFromName;

            var dnKey2 = NormalizeKey(StripQualityTokens(channel.Name));
            if (dnKey2.Length > 0 && index.ByDisplayNameNormalized.TryGetValue(dnKey2, out var idFromName2))
                return idFromName2;
        }

        // 3) Normalize id birebir
        var normCandidates = rawCandidates
            .Select(NormalizeKey)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var n in normCandidates)
        {
            if (index.AllChannelIdsByNormalized.TryGetValue(n, out var hits) && hits.Count > 0)
                return hits.OrderBy(h => h.Length).First();
        }

        // 4) Fuzzy normalize
        foreach (var n in normCandidates)
        {
            var best = FindBestFuzzy(index.AllChannelIdsByNormalized.Keys, n);
            if (best is not null &&
                index.AllChannelIdsByNormalized.TryGetValue(best, out var hits) &&
                hits.Count > 0)
                return hits.OrderBy(h => h.Length).First();
        }

        return null;
    }

    private static string? FindBestFuzzy(IEnumerable<string> normalizedKeys, string query)
    {
        string? best = null;
        int bestScore = 0;

        foreach (var k in normalizedKeys)
        {
            int score = 0;
            if (k.Equals(query, StringComparison.OrdinalIgnoreCase)) score = 100;
            else if (k.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score = 80;
            else if (query.StartsWith(k, StringComparison.OrdinalIgnoreCase)) score = 70;
            else if (k.Contains(query, StringComparison.OrdinalIgnoreCase)) score = 60;
            else if (query.Contains(k, StringComparison.OrdinalIgnoreCase)) score = 55;

            if (score > 0)
            {
                var lenDelta = Math.Abs(k.Length - query.Length);
                score -= Math.Min(lenDelta, 20);
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }

        return bestScore >= 55 ? best : null;
    }

    // ------------------------------------------------------------
    // Picking logic
    // ------------------------------------------------------------

    private static (EpgProgram? now, EpgProgram? next) PickNowNext(List<EpgProgram> list, DateTimeOffset nowUtc)
    {
        if (list.Count == 0) return (null, null);

        var now = list.FirstOrDefault(p => p.StartUtc <= nowUtc && p.EndUtc > nowUtc);
        if (now is null)
        {
            var nextOnly = list.FirstOrDefault(p => p.StartUtc > nowUtc);
            return (null, nextOnly);
        }

        var next = list.FirstOrDefault(p => p.StartUtc >= now.EndUtc);
        return (now, next);
    }

    // ------------------------------------------------------------
    // Timeline dedup (SAFE / strict)
    // ------------------------------------------------------------

    private static List<EpgProgram> DedupTimelineProgramsStrict(List<EpgProgram> programs)
    {
        if (programs.Count <= 1) return programs;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EpgProgram>(programs.Count);

        foreach (var p in programs)
        {
            var ch = (p.ChannelId ?? "").Trim();
            var titleKey = NormalizeProgramTitle(p.Title);

            // Start/End birebir: “agresif” tolerans yok
            var key = $"{ch}|{p.StartUtc.UtcDateTime:O}|{p.EndUtc.UtcDateTime:O}|{titleKey}";

            if (seen.Add(key))
                result.Add(p);
        }

        // Zaten sorted geliyor ama garanti olsun
        result.Sort(static (a, b) => a.StartUtc.CompareTo(b.StartUtc));
        return result;
    }

    private static string NormalizeProgramTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        // sadece trim + whitespace collapse + harf/rakam uppercase
        var t = Regex.Replace(s.Trim(), @"\s{2,}", " ");

        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));

        return sb.ToString();
    }

    // ------------------------------------------------------------
    // Normalization helpers (channel keys)
    // ------------------------------------------------------------

    private static string NormalizeKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        s = StripQualityTokens(s.Trim());

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));

        return sb.ToString();
    }

    private static string StripQualityTokens(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim();
        t = Regex.Replace(t, @"\b(HD|FHD|UHD|4K)\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s{2,}", " ").Trim();
        return t;
    }

    private static string RemoveDotCountrySuffix(string s)
    {
        var idx = s.LastIndexOf('.');
        if (idx > 0 && idx >= s.Length - 3)
        {
            var suf = s[(idx + 1)..];
            if (suf.Length == 2 && suf.All(char.IsLetter))
                return s[..idx];
        }
        return s;
    }

    private static string RemoveDashCountrySuffix(string s)
    {
        var idx = s.LastIndexOf('-');
        if (idx > 0 && idx >= s.Length - 3)
        {
            var suf = s[(idx + 1)..];
            if (suf.Length == 2 && suf.All(char.IsLetter))
                return s[..idx];
        }
        return s;
    }
}
