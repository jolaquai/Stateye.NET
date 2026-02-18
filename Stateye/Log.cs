namespace Stateye;

/// <summary>
/// Conditional debug logging — only emits output in Debug builds.
/// </summary>
public static class Log
{
    public static ReadOnlySpan<char> Now => DateTime.Now.ToString("HH:mm:ss").AsSpan();

    public static void Debug(string message) => Write(message);
    public static void Fatal(string v)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Write(v);
        Console.ResetColor();
    }

    private static void Write(string message) => Console.WriteLine($"[{Now}] {message}");
}
