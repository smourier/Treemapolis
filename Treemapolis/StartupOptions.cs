namespace Treemapolis;

public sealed class StartupOptions
{
    private const string _warpArgument = "warp";
    private const string _vsyncArgument = "vsync";
    private const string _debugArgument = "debug";
    private const string _gpuValidationArgument = "gpuvalidation";
    private const string _settingsArgument = "settings";

    public string? Location { get; init; }
    public bool UseWarp { get; init; }
    public bool VSync { get; init; } = true;
    public bool Debug { get; init; }

    // much slower, it checks on the GPU what the debug layer cannot see from the CPU.
    public bool GpuValidation { get; init; }

    // null is the user's own settings file, a test run points at one of its own.
    public string? SettingsPath { get; init; }

    public static StartupOptions FromCommandLine(CommandLine commandLine, string? defaultSettingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        return new StartupOptions
        {
            Location = commandLine.PositionedArguments.OrderBy(argument => argument.Key).Select(argument => argument.Value).FirstOrDefault(),
            UseWarp = commandLine.HasArgument(_warpArgument),
            GpuValidation = commandLine.HasArgument(_gpuValidationArgument),
            SettingsPath = commandLine.GetNullifiedArgument(_settingsArgument) ?? defaultSettingsPath,
            VSync = commandLine.GetArgument(_vsyncArgument, true),
#if DEBUG
            Debug = commandLine.GetArgument(_debugArgument, true),
#else
            Debug = commandLine.HasArgument(_debugArgument),
#endif
        };
    }
}
