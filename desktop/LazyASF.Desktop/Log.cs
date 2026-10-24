namespace LazyASF;

public static class Log
{
    static readonly object Lock = new();
    static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "log.txt");

    public static event Action<string>? Line;

    public static void Write(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {msg}";
        lock (Lock)
        {
            try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}"); } catch { }
        }
        Line?.Invoke(line);
    }
}
