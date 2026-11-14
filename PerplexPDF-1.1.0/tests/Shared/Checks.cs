namespace PerplexPDF.Tests;

internal static class Check
{
    public static void That(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException(detail);
    }

    public static async Task FailsAsync<T>(Func<Task> action, Func<T, bool>? filter = null) where T : Exception
    {
        try { await action(); }
        catch (T error) when (filter is null || filter(error)) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
