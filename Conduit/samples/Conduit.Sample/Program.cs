using System.Diagnostics;
using Conduit;

// ── Shared state ──────────────────────────────────────────────────────────────

var retryCount = 0L;
var processedCount = 0L;
var rng = new Random(42);

// ── Setup ─────────────────────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  ╔══════════════════════════════════════════════════════╗");
Console.WriteLine("  ║          Conduit — Log Processing Pipeline           ║");
Console.WriteLine("  ╠══════════════════════════════════════════════════════╣");
Console.WriteLine("  ║  Producers  : 3  (api-gw 24/s, worker-1 16/s, …)   ║");
Console.WriteLine("  ║  Rate limit : 30 req/s                               ║");
Console.WriteLine("  ║  Retry      : 5× exponential backoff on IOException  ║");
Console.WriteLine("  ║  Batch      : 10 items or 300ms flush                ║");
Console.WriteLine("  ║  Runtime    : 12 seconds                             ║");
Console.WriteLine("  ╚══════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine($"  {"Elapsed",-10} {"Buffer",8} {"Peak",6} {"Written",8} {"Retries",8} {"Processed",10}");
Console.WriteLine($"  {new string('─', 56)}");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

// Bounded input channel with backpressure metrics
var raw = Chan.BoundedWithMetrics<LogEntry>(256, out var metrics);

// ── 3 Producers (combined ~48 req/sec) ────────────────────────────────────────

var group = new TaskGroup();
var sources = new[] { ("api-gw", 24), ("worker-1", 16), ("worker-2", 8) };
var globalId = 0;
var verbs = new[] { "GET", "POST", "PUT", "DELETE" };
var paths = new[] { "/api/users", "/api/orders", "/api/products", "/health" };

foreach (var (name, rps) in sources)
{
    var src = name;
    var delayMs = 1000 / rps;
    group.Spawn(async ct =>
    {
        while (!ct.IsCancellationRequested)
        {
            var entry = new LogEntry(
                Interlocked.Increment(ref globalId), src,
                verbs[rng.Next(verbs.Length)], paths[rng.Next(paths.Length)],
                DateTimeOffset.UtcNow);
            try
            {
                await raw.WriteAsync(entry, ct);
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }, ct);
}

_ = group.WaitSafeAsync(ct).ContinueWith(_ => raw.Complete(), TaskScheduler.Default);

// ── Flaky processor (~15% transient failure rate) ─────────────────────────────

async ValueTask<ProcessedLog> ProcessAsync(LogEntry entry, CancellationToken ct)
{
    await Task.Delay(rng.Next(2, 8), ct);
    if (rng.NextDouble() < 0.15)
        throw new IOException($"upstream timeout for {entry.Id}");
    return new ProcessedLog(entry.Id, entry.Source,
        StatusCode: rng.NextDouble() < 0.95 ? 200 : 500,
        LatencyMs: rng.Next(5, 80));
}

// ── Live metrics (every 500ms) ────────────────────────────────────────────────

var sw = Stopwatch.StartNew();
_ = Task.Run(async () =>
{
    while (!ct.IsCancellationRequested)
    {
        try { await Task.Delay(500, ct); } catch { break; }
        var buf = metrics.BufferDepth;
        Console.ForegroundColor = buf > 128 ? ConsoleColor.Yellow : ConsoleColor.Green;
        Console.Write($"  {sw.Elapsed:mm\\:ss\\.f,-10}");
        Console.ResetColor();
        Console.WriteLine(
            $" {buf,8}" +
            $" {metrics.PeakBufferDepth,6}" +
            $" {metrics.TotalWritten,8}" +
            $" {Volatile.Read(ref retryCount),8}" +
            $" {Volatile.Read(ref processedCount),10}");
    }
}, ct);

// ── Pipeline ──────────────────────────────────────────────────────────────────
//   raw  →  RateLimit(30/s)  →  Transform+Retry(5×exp)  →  Batch(10)  →  Sink

Console.WriteLine($"  {"---",-10} {"---",8} {"---",6} {"---",8} {"---",8} {"---",10}");

try
{
    await Flow.Pipeline(raw)

        // 1. Cap at 30 logs/sec to protect downstream
        .RateLimit(permitsPerSecond: 30)

        // 2. Process with up to 5 exponential-backoff retries on IO errors
        .Transform<ProcessedLog>(
            ProcessAsync,
            retry: RetryPolicy.Exponential(
                maxAttempts: 5,
                baseDelay: TimeSpan.FromMilliseconds(30),
                shouldRetry: ex => ex is IOException,
                onRetry: (_, _) => Interlocked.Increment(ref retryCount)))

        // 3. Batch into groups of 10, flush every 300ms
        .Batch(batchSize: 10, flushTimeout: TimeSpan.FromMilliseconds(300))

        // 4. Sink: simulate batch DB write
        .DrainAsync(async (batch, ct) =>
        {
            await Task.Delay(5, ct);
            Interlocked.Add(ref processedCount, batch.Length);
        }, ct);
}
catch (OperationCanceledException) { }
catch (Exception ex) { Console.WriteLine($"\n  [pipeline fault] {ex.GetType().Name}: {ex.Message}"); }

cts.Cancel(); // stop producers and metrics loop

// ── Final report ──────────────────────────────────────────────────────────────

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  ┌─── Final Report ───────────────────────────────────┐");
Console.ResetColor();
Console.WriteLine($"  │  Runtime        : {sw.Elapsed:mm\\:ss\\.ff}");
Console.WriteLine($"  │  Total ingested : {metrics.TotalWritten}");
Console.WriteLine($"  │  Total processed: {processedCount}");
Console.WriteLine($"  │  Retries fired  : {retryCount}");
Console.WriteLine($"  │  Items dropped  : {metrics.TotalDropped}");
Console.WriteLine($"  │  Peak buffer    : {metrics.PeakBufferDepth}");
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  └────────────────────────────────────────────────────┘");
Console.ResetColor();

// ── Types ─────────────────────────────────────────────────────────────────────

record LogEntry(int Id, string Source, string Verb, string Path, DateTimeOffset At);

record ProcessedLog(int Id, string Source, int StatusCode, long LatencyMs);
