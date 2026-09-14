using System.Globalization;
using System.Text;

namespace CSharpGit.Presentation.ViewModels;

internal static class MiddleEllipsis
{
    private const string Ellipsis = "…";

    public static string Fit(string? text, double availableWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);

        var source = text ?? string.Empty;
        if (source.Length == 0) return source;
        if (double.IsNaN(availableWidth) || availableWidth <= 0) return string.Empty;
        if (double.IsPositiveInfinity(availableWidth) || measure(source) <= availableWidth) return source;
        if (measure(Ellipsis) > availableWidth) return string.Empty;

        var elements = SplitTextElements(source);
        for (var kept = elements.Count - 1; kept >= 2; kept--)
        {
            var suffixCount = (kept + 1) / 2;
            var prefixCount = kept - suffixCount;
            var candidate = BuildCandidate(elements, prefixCount, suffixCount);
            if (measure(candidate) <= availableWidth) return candidate;
        }

        return Ellipsis;
    }

    private static List<string> SplitTextElements(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        return elements;
    }

    private static string BuildCandidate(IReadOnlyList<string> elements, int prefixCount, int suffixCount)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < prefixCount; index++) builder.Append(elements[index]);
        builder.Append(Ellipsis);
        for (var index = elements.Count - suffixCount; index < elements.Count; index++) builder.Append(elements[index]);
        return builder.ToString();
    }
}
