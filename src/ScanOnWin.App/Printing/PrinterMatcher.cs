using System.Text.RegularExpressions;

namespace ScanOnWin.App.Printing;

public sealed record PrinterDescriptor(string Name, string DriverName = "", string PortName = "", string Location = "", string Comment = "");
public sealed record PrinterMatch(PrinterDescriptor? Printer, bool IsAmbiguous);

/// <summary>Only a unique exact normalized device/model or explicit host/serial match may be suggested.</summary>
public static class PrinterMatcher
{
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "scanner", "scan", "printer", "series", "wia", "twain", "usb", "network", "сканер", "принтер", "серия"
    };

    public static PrinterMatch Match(string? scannerName, string? scannerId, IReadOnlyList<PrinterDescriptor> printers)
    {
        var identity = Identities((scannerName ?? "") + " " + (scannerId ?? ""));
        var compatible = printers.Where(p => !Conflicts(identity, Identities($"{p.Name} {p.PortName} {p.Location} {p.Comment}"))).ToArray();
        if (identity.Count > 0)
        {
            var direct = compatible.Where(p => Identities($"{p.Name} {p.PortName} {p.Location} {p.Comment}").Overlaps(identity)).ToArray();
            if (direct.Length > 0) return new(direct.Length == 1 ? direct[0] : null, direct.Length > 1);
        }
        var model = Normalize(scannerName ?? "");
        // A manufacturer or a generic scanner name cannot identify a particular MFP.
        if (!model.Any(char.IsDigit) || !model.Any(char.IsLetter)) return new(null, false);
        var candidates = compatible.Where(p => Normalize(p.Name) == model || Normalize(p.DriverName) == model).ToArray();
        return new(candidates.Length == 1 ? candidates[0] : null, candidates.Length > 1);
    }

    private static bool Conflicts(HashSet<string> scanner, HashSet<string> printer)
    {
        foreach (var kind in new[] { "host:", "serial:" })
        {
            var scannerValues = scanner.Where(value => value.StartsWith(kind, StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var printerValues = printer.Where(value => value.StartsWith(kind, StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (scannerValues.Count > 0 && printerValues.Count > 0 && !scannerValues.Overlaps(printerValues)) return true;
        }
        return false;
    }

    private static string Normalize(string value)
    {
        // Windows commonly adds the port or a copy number to an otherwise exact model name.
        // Remove only recognized decorations, so a different model/suffix cannot become a match.
        value = Regex.Replace(value, @"\((?:USB\d+|WSD-[a-z0-9-]+|copy\s*\d+|копия\s*\d+)\)", "", RegexOptions.IgnoreCase);
        return string.Concat(Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{Nd}]+")
            .Select(m => m.Value).Where(token => !Noise.Contains(token)));
    }

    private static HashSet<string> Identities(string value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(value, @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])"))
            if (System.Net.IPAddress.TryParse(match.Value, out _)) result.Add("host:" + match.Value);
        foreach (Match match in Regex.Matches(value, @"https?://([^\s/]+)", RegexOptions.IgnoreCase))
            if (Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) result.Add("host:" + uri.Host);
        foreach (Match match in Regex.Matches(value, @"(?:serial|s/n|серийный\s+номер)\s*[:=]\s*([a-z0-9-]{5,})", RegexOptions.IgnoreCase))
            result.Add("serial:" + match.Groups[1].Value);
        return result;
    }
}
