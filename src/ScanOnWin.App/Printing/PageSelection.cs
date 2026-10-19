using System.Globalization;

namespace ScanOnWin.App.Printing;

public static class PageSelection
{
    /// <summary>Parses one-based page numbers and inclusive ranges; returns document order, without duplicates.</summary>
    public static IReadOnlyList<int> Parse(string text, int pageCount)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Укажите страницы, например: 1, 3–5.");
        var selected = new SortedSet<int>();
        foreach (var part in text.Replace('–', '-').Replace('—', '-').Split([',', ';']))
        {
            var range = part.Trim().Split('-');
            if (range.Length is < 1 or > 2 || !Number(range[0], out var start)) throw Invalid(pageCount);
            var end = start;
            if (range.Length == 2 && !Number(range[1], out end)) throw Invalid(pageCount);
            if (start < 1 || end < start || end > pageCount) throw Invalid(pageCount);
            for (int page = start; page <= end; page++) selected.Add(page - 1);
        }
        return selected.ToArray();
    }

    private static bool Number(string text, out int value) => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    private static FormatException Invalid(int count) => new($"Введите номера от 1 до {count} или диапазоны через запятую, например: 1, 3–5.");
}
