using System.Runtime.InteropServices;
using Pangya.Core.Config;
using Pangya.Core.Logging;

namespace Pangya.Core.Hosting;

/// <summary>Esqueleto de processo: carrega a configuração, liga o log e encerra limpo com Ctrl+C / SIGTERM.</summary>
public static class ServerHost
{
    public static async Task<int> RunAsync(string name, string? configPath, Func<PangyaConfig, CancellationToken, Task> run)
    {
        PangyaConfig cfg;
        try { cfg = PangyaConfig.Load(configPath); }
        catch (Exception e)
        {
            Console.Error.WriteLine("Configuração inválida: " + e.Message);
            return 2;
        }
        Log.Start(name, string.IsNullOrEmpty(cfg.Logging.Dir) ? null : cfg.Logging.Dir, cfg.Logging.Level);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; cts.Cancel(); });
        try
        {
            await run(cfg, cts.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { return 0; }
        catch (Exception e)
        {
            Log.Error("erro fatal", e);
            return 1;
        }
        finally
        {
            Log.Info("encerrado");
            await Log.FlushAsync();
        }
    }
}
