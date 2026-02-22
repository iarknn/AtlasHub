using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasHub.Services;

public static class M3uHeaderParser
{
    /// <summary>
    /// M3U header'ından EPG URL'lerini okur:
    /// #EXTM3U x-tvg-url="a.xml.gz,b.xml.gz"
    /// veya url-tvg / tvg-url varyasyonları.
    /// </summary>
    public static IReadOnlyList<string> ExtractEpgUrls(string? m3uText)
    {
        if (string.IsNullOrWhiteSpace(m3uText))
            return Array.Empty<string>();

        var firstLine = m3uText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(l => (l ?? "").Trim())
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (firstLine is null)
            return Array.Empty<string>();

        if (!firstLine.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var attrs = ParseAttributes(firstLine);

        var keys = new[]
        {
            "x-tvg-url",
            "url-tvg",
            "tvg-url",
            "x-tvg-url1",
            "x-tvg-url2"
        };

        foreach (var key in keys)
        {
            if (attrs.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                return NormalizeUrls(SplitRawUrls(raw));
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Repo'da saklanan virgüllü (veya satır sonlu) URL listesini geri okur.
    /// </summary>
    public static List<string> SplitJoinedUrls(string? joined)
    {
        if (string.IsNullOrWhiteSpace(joined)) return new List<string>();

        var parts = joined
            .Split(new[] { ",", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (x ?? "").Trim().Trim('"'))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts;
    }

    public static string JoinUrls(IEnumerable<string> urls)
        => string.Join(",", NormalizeUrls(urls));

    // -----------------------
    // internal
    // -----------------------

    private static IEnumerable<string> SplitRawUrls(string raw)
    {
        // epgshare genelde virgülle ayrılmış liste veriyor
        var parts = raw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(p => p.Trim().Trim('"'))
                       .Where(p => p.Length > 0)
                       .ToList();

        if (parts.Count == 0 && raw.Trim().Length > 0)
            parts.Add(raw.Trim().Trim('"'));

        return parts;
    }

    private static IReadOnlyList<string> NormalizeUrls(IEnumerable<string> urls)
    {
        return urls
            .Select(u => (u ?? "").Trim().Trim('"'))
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ParseAttributes(string headerLine)
    {
        // #EXTM3U key="value" key2="value2"
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var i = headerLine.IndexOf(' ');
        if (i < 0) return dict;
        i++;

        while (i < headerLine.Length)
        {
            while (i < headerLine.Length && char.IsWhiteSpace(headerLine[i])) i++;
            if (i >= headerLine.Length) break;

            int keyStart = i;
            while (i < headerLine.Length && !char.IsWhiteSpace(headerLine[i]) && headerLine[i] != '=') i++;
            if (i <= keyStart) break;

            var key = headerLine.Substring(keyStart, i - keyStart).Trim();

            while (i < headerLine.Length && char.IsWhiteSpace(headerLine[i])) i++;
            if (i >= headerLine.Length || headerLine[i] != '=') { SkipToken(ref i, headerLine); continue; }
            i++; // '='

            while (i < headerLine.Length && char.IsWhiteSpace(headerLine[i])) i++;
            if (i >= headerLine.Length) break;

            string value;

            if (headerLine[i] == '"')
            {
                i++; // opening quote
                int valStart = i;
                while (i < headerLine.Length && headerLine[i] != '"') i++;
                value = headerLine.Substring(valStart, i - valStart);
                if (i < headerLine.Length && headerLine[i] == '"') i++; // closing quote
            }
            else
            {
                int valStart = i;
                while (i < headerLine.Length && !char.IsWhiteSpace(headerLine[i])) i++;
                value = headerLine.Substring(valStart, i - valStart);
            }

            if (!string.IsNullOrWhiteSpace(key))
                dict[key] = value ?? "";

            while (i < headerLine.Length && char.IsWhiteSpace(headerLine[i])) i++;
        }

        return dict;
    }

    private static void SkipToken(ref int i, string s)
    {
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
