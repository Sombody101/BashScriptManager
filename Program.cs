namespace BashScriptManager;

public static class Program
{
    private static void Main(string[] args)
    {
        Interface ui = new();
        ui.Run();
    }
}