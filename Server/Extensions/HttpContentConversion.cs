public static class ConverterExtensions
{
    public static string ToString (this HttpContent input)
    {
        var task = input.ReadAsStringAsync().GetAwaiter();
        while(!task.IsCompleted)
        {
            Thread.Yield();
        }
        return task.GetResult();
    }
}
