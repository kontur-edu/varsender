namespace varsender.App;

public static class Extensions
{
    public static bool IsHashtag(this string str)
    {
        return str.StartsWith("#")
            && str.IndexOfAny(new[] { ' ', '\r', '\n', '\t' }) < 0;
    }

    public static string Pluralize(this int count, string one, string two, string five)
    {
        if (count % 10 == 0 || count % 10 >= 5 || count % 100 > 10 && count % 100 < 20) return five;
        return count % 10 == 1 ? one : two;
    }
}
