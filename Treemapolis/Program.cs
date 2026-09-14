namespace Treemapolis;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var options = StartupOptions.FromCommandLine(CommandLine.Current);
        using var app = new Application();
        WindowSynchronizationContext.Install();

        using var window = new MainWindow(options);
        window.Show();
        window.SetForeground();
        app.Run();
    }
}
