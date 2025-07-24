using Spectre.Console;

namespace BashScriptManager;

public static class AnsiConsoleLogger
{
    public static void LogInformation(object obj)
    {
        AnsiConsole.WriteLine($"[[[blue]+[/]]]: {obj}");
    }

    public static void LogWarning(object obj)
    {
        AnsiConsole.WriteLine($"[[[yellow]/[/]]]: {obj}");
    }

    public static void LogError(object obj)
    {
        AnsiConsole.WriteLine($"[[[red]-[/]]]: {obj}");
    }

    public static void LogFatal(object obj)
    {
        AnsiConsole.WriteLine($"[[[red]FATAL[/]]]: {obj}");
    }

    public static void LogException(Exception ex, string extraMessage)
    {
        AnsiConsole.WriteLine(extraMessage);
        AnsiConsole.WriteException(ex);
    }
}
