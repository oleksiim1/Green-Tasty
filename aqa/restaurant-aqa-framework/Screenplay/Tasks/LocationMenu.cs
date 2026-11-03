namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>One at a time through changes to a location's menu. Creating a dish rewrites that location's
/// dish list, and two scenarios doing it together can drop each other's dishes.</summary>
internal static class LocationMenu
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> UseAsync<T>(Func<Task<T>> work)
    {
        await Gate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task UseAsync(Func<Task> work) =>
        UseAsync(async () =>
        {
            await work();
            return 0;
        });
}
