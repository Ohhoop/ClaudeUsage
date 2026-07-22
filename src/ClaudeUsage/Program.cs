namespace ClaudeUsage;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ClaudeUsage.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayForm());
        GC.KeepAlive(mutex);
    }
}
