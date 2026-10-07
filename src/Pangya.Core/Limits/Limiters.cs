using System.Collections.Concurrent;
using System.Net;

namespace Pangya.Core.Limits;

/// <summary>Balde de fichas: até <c>perSecond</c> eventos por segundo (com rajada do mesmo tamanho). Uma instância por conexão.</summary>
public sealed class TokenBucket(int perSecond)
{
    double tokens = perSecond;
    long last = Environment.TickCount64;

    public bool TryTake()
    {
        var now = Environment.TickCount64;
        tokens = Math.Min(perSecond, tokens + (now - last) * perSecond / 1000.0);
        last = now;
        if (tokens < 1) return false;
        tokens -= 1;
        return true;
    }
}

/// <summary>Conta conexões abertas por IP; recusa acima do limite.</summary>
public sealed class IpConnectionLimiter(int maxPerIp)
{
    readonly ConcurrentDictionary<IPAddress, int> open = new();

    public bool TryAcquire(IPAddress ip)
    {
        while (true)
        {
            var n = open.GetOrAdd(ip, 0);
            if (n >= maxPerIp) return false;
            if (open.TryUpdate(ip, n + 1, n)) return true;
        }
    }

    public void Release(IPAddress ip)
    {
        while (open.TryGetValue(ip, out var n))
        {
            if (n <= 1 ? open.TryRemove(new KeyValuePair<IPAddress, int>(ip, n)) : open.TryUpdate(ip, n - 1, n)) return;
        }
    }

    public int Count(IPAddress ip) => open.TryGetValue(ip, out var n) ? n : 0;
}

/// <summary>
/// Limite de tentativas numa janela de tempo por chave (IP, conta...). Usado no login: acima do limite
/// a tentativa é recusada sem nem consultar o banco.
/// </summary>
public sealed class AttemptLimiter(int maxAttempts, TimeSpan window)
{
    readonly ConcurrentDictionary<string, Queue<long>> attempts = new();

    /// <summary>Registra uma tentativa; devolve false se a chave já passou do limite na janela.</summary>
    public bool TryAttempt(string key)
    {
        var now = Environment.TickCount64;
        var q = attempts.GetOrAdd(key, _ => new Queue<long>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > window.TotalMilliseconds) q.Dequeue();
            if (q.Count >= maxAttempts) return false;
            q.Enqueue(now);
        }
        if (attempts.Count > 10_000) Prune(now);
        return true;
    }

    // Remove chaves sem tentativas recentes (evita crescer sem limite com IPs variados).
    void Prune(long now)
    {
        foreach (var (k, q) in attempts)
            lock (q)
            {
                // a fila está em ordem de tempo: se a primeira já venceu e não há outras recentes, a chave sai
                while (q.Count > 0 && now - q.Peek() > window.TotalMilliseconds) q.Dequeue();
                if (q.Count == 0) attempts.TryRemove(k, out _);
            }
    }
}
