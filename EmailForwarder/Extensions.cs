namespace EmailForwarder;

public static class Extensions
{
    public static async Task AwaitIfNotNull(this Task? nullableTask)
    {
        if (nullableTask != null)
            await nullableTask;
    }

    public static async ValueTask AwaitIfNotNull(this ValueTask? nullableTask)
    {
        if (nullableTask != null)
            await nullableTask.Value;
    }
}