namespace Content.Server.CMU14.Robotics.UtilityDrones;

/// <summary>
/// Item-name matching shared by world searches, backpack searches and script
/// diagnostics. Matching is accent- and culture-independent so Russian and
/// English names behave the same on every server culture.
/// </summary>
public static class CMUUtilityScriptNameMatcher
{
    public static bool Matches(string? source, string? query)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
            return false;

        query = query.Trim();
        return string.Equals(source, query, StringComparison.OrdinalIgnoreCase) ||
               ContainsWholeWord(source, query);
    }

    public static bool ContainsWholeWord(string source, string query)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
            return false;

        var offset = 0;
        while (offset < source.Length)
        {
            var index = source.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var startsAtBoundary = index == 0 || !char.IsLetterOrDigit(source[index - 1]);
            var end = index + query.Length;
            var endsAtBoundary = end >= source.Length || !char.IsLetterOrDigit(source[end]);
            if (startsAtBoundary && endsAtBoundary)
                return true;

            offset = index + 1;
        }

        return false;
    }
}
