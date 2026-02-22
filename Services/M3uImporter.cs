using System.Text;
using System.Text.RegularExpressions;
using AtlasHub.Models;

namespace AtlasHub.Services;

public sealed class M3uImporter
{
    // #EXTINF:-1 tvg-id="..." tvg-name="..." tvg-logo="..." group-title="..."
    private static readonly Regex AttrRegex =
        new(@"(\w+(?:-\w+)*)=""([^""]*)""", RegexOptions.Compiled);

    public CatalogSnapshot Parse(string providerId, string providerName, string m3uText)
    {
        var categories = new Dictionary<string, LiveCategory>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<LiveChannel>();

        // Pending info from EXTINF / EXTGRP
        string? pendingName = null;
        string? pendingGroup = null;
        string? pendingLogo = null;
        string? pendingTvgId = null;
        string? pendingTvgName = null;

        // Some lists use #EXTGRP right after EXTINF
        string? lastExtGrp = null;

        var lines = SplitLines(m3uText);

        foreach (var raw in lines)
        {
            var line = (raw ?? "").Trim();
            if (line.Length == 0) continue;

            // EXTINF
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pendingName = ExtractNameAfterComma(line);
                var attrs = ExtractAttrs(line);

                attrs.TryGetValue("group-title", out pendingGroup);
                attrs.TryGetValue("tvg-logo", out pendingLogo);
                attrs.TryGetValue("tvg-id", out pendingTvgId);
                attrs.TryGetValue("tvg-name", out pendingTvgName);

                // reset lastExtGrp to avoid carrying old value too far
                lastExtGrp = null;
                continue;
            }

            // EXTGRP (category line)
            if (line.StartsWith("#EXTGRP", StringComparison.OrdinalIgnoreCase))
            {
                // format: #EXTGRP:Some Group
                var idx = line.IndexOf(':');
                var grp = idx >= 0 ? line[(idx + 1)..] : "";
                grp = (grp ?? "").Trim();
                if (grp.Length > 0)
                    lastExtGrp = grp;

                // don't finalize channel yet
                continue;
            }

            // other tags
            if (line.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                continue;

            // URL line (stream)
            var url = NormalizeUrl(line);
            if (url.Length == 0)
                continue;

            // channel name fallback order:
            // 1) name after comma in EXTINF
            // 2) tvg-name
            // 3) url
            var name = FirstNonEmpty(pendingName, pendingTvgName, url);

            // group fallback order:
            // 1) group-title attr
            // 2) last #EXTGRP
            // 3) "Diğer"
            var group = FirstNonEmpty(pendingGroup, lastExtGrp, "Diğer");

            // create category if missing
            var catId = MakeId($"{providerId}:{group}");
            if (!categories.ContainsKey(group))
                categories[group] = new LiveCategory(providerId, catId, group);

            // build channel id
            var chId = MakeId($"{providerId}:{name}:{group}:{pendingTvgId}:{url}");
            channels.Add(new LiveChannel(
                ProviderId: providerId,
                ChannelId: chId,
                CategoryName: group,
                Name: name,
                LogoUrl: string.IsNullOrWhiteSpace(pendingLogo) ? null : pendingLogo.Trim(),
                TvgId: string.IsNullOrWhiteSpace(pendingTvgId) ? null : pendingTvgId.Trim(),
                StreamUrl: url
            ));

            // consume pending
            pendingName = null;
            pendingGroup = null;
            pendingLogo = null;
            pendingTvgId = null;
            pendingTvgName = null;
            lastExtGrp = null;
        }

        // sort categories
        var cats = categories.Values
                             .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                             .ToList();

        return new CatalogSnapshot(
            ProviderId: providerId,
            ProviderName: providerName,
            CreatedUtc: DateTimeOffset.UtcNow.ToString("O"),
            Categories: cats,
            Channels: channels
        );
    }

    private static string[] SplitLines(string text)
        => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private static string ExtractNameAfterComma(string extinfLine)
    {
        var idx = extinfLine.LastIndexOf(',');
        if (idx < 0) return "";
        return extinfLine[(idx + 1)..].Trim();
    }

    private static Dictionary<string, string> ExtractAttrs(string extinfLine)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRegex.Matches(extinfLine))
        {
            var key = m.Groups[1].Value;
            var val = m.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(key))
                dict[key] = val;
        }
        return dict;
    }

    private static string NormalizeUrl(string raw)
    {
        var s = raw.Trim();

        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();

        s = s.Replace("\uFEFF", "")
             .Replace("\u200B", "")
             .Replace("\u200C", "")
             .Replace("\u200D", "");

        return s;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return "";
    }

    private static string MakeId(string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
