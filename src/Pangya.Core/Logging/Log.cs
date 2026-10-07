using System.Threading.Channels;

namespace Pangya.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Log simples e barato: as linhas vão para uma fila e uma tarefa de fundo escreve no console e no arquivo
/// (logs/&lt;nome&gt;-AAAAMMDD.log). Quem loga nunca espera pelo disco.
/// </summary>
public static class Log
{
    static readonly Channel<string> Queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(100_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    static Task? writer;
    static string name = "pangya";
    static string? dir;

    public static LogLevel Level { get; set; } = LogLevel.Info;

    /// <summary>Liga a escrita em arquivo (dir = null: só console).</summary>
    public static void Start(string serverName, string? logDir, LogLevel level)
    {
        name = serverName;
        dir = logDir;
        Level = level;
        if (dir != null) Directory.CreateDirectory(dir);
        writer ??= Task.Run(WriteLoop);
    }

    public static bool IsEnabled(LogLevel level) => level >= Level;
    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warn, msg);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, ex == null ? msg : $"{msg}: {ex}");

    static void Write(LogLevel level, string msg)
    {
        if (level < Level) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} {msg}";
        if (writer == null) Console.WriteLine(line);       // antes de Start (ex.: testes): direto no console
        else Queue.Writer.TryWrite(line);
    }

    static async Task WriteLoop()
    {
        StreamWriter? file = null;
        var day = "";
        await foreach (var line in Queue.Reader.ReadAllAsync())
        {
            Console.WriteLine(line);
            if (dir == null) continue;
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (today != day)
            {
                file?.Dispose();
                file = new StreamWriter(Path.Combine(dir, $"{name}-{today}.log"), append: true) { AutoFlush = false };
                day = today;
            }
            file!.WriteLine(line);
            if (Queue.Reader.Count == 0) await file.FlushAsync();
        }
    }

    /// <summary>Espera a fila esvaziar (ao encerrar o processo).</summary>
    public static async Task FlushAsync()
    {
        for (int i = 0; i < 50 && Queue.Reader.Count > 0; i++) await Task.Delay(20);
    }
}
