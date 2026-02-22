using AtlasHub.Models;
using System.Globalization;
using System.IO;
using System.Xml;

namespace AtlasHub.Services;

public sealed class XmlTvParser
{
    public XmlTvParseResult Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new XmlTvParseResult(new List<EpgProgram>(), new List<EpgChannel>());

        xml = NormalizeXmlInput(xml);
        if (xml.Length == 0)
            return new XmlTvParseResult(new List<EpgProgram>(), new List<EpgChannel>());

        var programs = new List<EpgProgram>(8192);
        var channels = new List<EpgChannel>(2048);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CheckCharacters = false,
            XmlResolver = null
        };

        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;

            // <channel id="..."><display-name>...</display-name></channel>
            if (reader.Name.Equals("channel", StringComparison.OrdinalIgnoreCase))
            {
                var id = reader.GetAttribute("id");
                if (string.IsNullOrWhiteSpace(id)) { reader.Skip(); continue; }

                var displayNames = new List<string>(2);

                using (var sub = reader.ReadSubtree())
                {
                    sub.Read(); // channel
                    while (sub.Read())
                    {
                        if (sub.NodeType != XmlNodeType.Element) continue;

                        if (sub.Name.Equals("display-name", StringComparison.OrdinalIgnoreCase))
                        {
                            var dn = (sub.ReadElementContentAsString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(dn))
                                displayNames.Add(dn);
                        }
                    }
                }

                channels.Add(new EpgChannel(id.Trim(), displayNames));
                continue;
            }

            // <programme start="..." stop="..." channel="...">...</programme>
            if (!reader.Name.Equals("programme", StringComparison.OrdinalIgnoreCase))
                continue;

            var channel = reader.GetAttribute("channel");
            if (string.IsNullOrWhiteSpace(channel)) { reader.Skip(); continue; }

            var startRaw = reader.GetAttribute("start");
            var stopRaw = reader.GetAttribute("stop");
            if (string.IsNullOrWhiteSpace(startRaw) || string.IsNullOrWhiteSpace(stopRaw)) { reader.Skip(); continue; }

            var startUtc = ParseXmlTvTimeToUtc(startRaw);
            var endUtc = ParseXmlTvTimeToUtc(stopRaw);
            if (startUtc is null || endUtc is null) { reader.Skip(); continue; }
            if (endUtc <= startUtc) { reader.Skip(); continue; }

            string title = "(Başlıksız)";
            string? desc = null;

            using (var sub = reader.ReadSubtree())
            {
                sub.Read(); // programme
                while (sub.Read())
                {
                    if (sub.NodeType != XmlNodeType.Element) continue;

                    if (title == "(Başlıksız)" && sub.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                    {
                        title = (sub.ReadElementContentAsString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(title)) title = "(Başlıksız)";
                    }
                    else if (desc is null && sub.Name.Equals("desc", StringComparison.OrdinalIgnoreCase))
                    {
                        var d = (sub.ReadElementContentAsString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(d)) desc = d;
                    }

                    if (title != "(Başlıksız)" && desc is not null)
                        break;
                }
            }

            programs.Add(new EpgProgram(
                ChannelId: channel.Trim(),
                Title: title,
                Description: desc,
                StartUtc: startUtc.Value,
                EndUtc: endUtc.Value
            ));
        }

        return new XmlTvParseResult(programs, channels);
    }

    private static string NormalizeXmlInput(string xml)
    {
        xml = xml.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var idx = xml.IndexOf('<');
        if (idx < 0) return "";
        if (idx == 0) return xml;
        return xml.Substring(idx);
    }

    private static DateTimeOffset? ParseXmlTvTimeToUtc(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length < 14) return null;

        var dtPart = s.Substring(0, 14);
        if (!DateTime.TryParseExact(dtPart, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            return null;

        var offsetPart = s.Length > 14 ? s.Substring(14).Trim() : "";

        if (offsetPart.Length > 0)
        {
            offsetPart = offsetPart.Replace(" ", "");

            if (offsetPart.Length == 5 && (offsetPart[0] == '+' || offsetPart[0] == '-'))
            {
                if (int.TryParse(offsetPart.Substring(1, 2), out var hh) &&
                    int.TryParse(offsetPart.Substring(3, 2), out var mm))
                {
                    var sign = offsetPart[0] == '-' ? -1 : 1;
                    var offset = new TimeSpan(sign * hh, sign * mm, 0);
                    return new DateTimeOffset(dt, offset).ToUniversalTime();
                }
            }
        }

        dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
        return new DateTimeOffset(dt).ToUniversalTime();
    }
}

public sealed record XmlTvParseResult(
    List<EpgProgram> Programs,
    List<EpgChannel> Channels
);
