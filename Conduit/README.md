# Conduit

**Go-style channels, pipelines, and concurrency helpers for .NET** — built on `System.Threading.Channels`, with optional metrics, retry policies, rate limiting, batching, and AOT-friendly settings.

| | |
| --- | --- |
| **Target** | .NET 10 (`net10.0`) |
| **Package id** | `Conduit` (version `0.1.0` in source) |
| **Language** | C# with `LangVersion` preview |

---

## Table of contents

- [Why Conduit?](#why-conduit)
- [Repository layout](#repository-layout)
- [Requirements](#requirements)
- [Build & test](#build--test)
- [Quick start](#quick-start)
- [Core concepts](#core-concepts)
- [API reference](#api-reference)
- [Sample application](#sample-application)
- [Design notes](#design-notes)
- [Roadmap & contributing](#roadmap--contributing)

---

## Why Conduit?

- **Familiar model**: channels (`Chan<T>`), fan-in (`Merge`), worker pools, and a fluent **pipeline** API for stream processing.
- **Production-oriented pieces**: bounded channels with **backpressure metrics**, **retry** on transforms, **rate limiting**, and **time-bounded batching**.
- **Interop with .NET**: thin wrapper over `Channel<T>` — no custom scheduler; works with `async`/`await` and cancellation tokens throughout.
- **Packaging**: project is configured for trimming/AOT compatibility (`IsAotCompatible`, trim analyzer, `TrimmerRoots.xml`).

---

## Repository layout

```
Conduit/
├── Conduit.slnx              # Solution (SDK-style SLNX)
├── nuget.config              # NuGet sources (nuget.org)
├── src/Conduit/              # Library — Conduit.csproj
├── samples/Conduit.Sample/   # Console demo (log pipeline)
└── tests/Conduit.Tests/      # xUnit + FluentAssertions
```

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (matches `TargetFramework` `net10.0`).
- Restore uses `nuget.org` (see `nuget.config`).

---

## Build & test

From the `Conduit` directory (where `Conduit.slnx` lives):

```bash
dotnet build Conduit.slnx
dotnet test tests/Conduit.Tests/Conduit.Tests.csproj
```

Run the sample:

```bash
dotnet run --project samples/Conduit.Sample/Conduit.Sample.csproj
```

Pack the library (optional):

```bash
dotnet pack src/Conduit/Conduit.csproj -c Release
```

---

## Quick start

### 1. Reference the project

```xml
<ItemGroup>
  <ProjectReference Include="path/to/src/Conduit/Conduit.csproj" />
</ItemGroup>
```

When the package is published to NuGet, reference `Conduit` by package name instead.

### 2. Minimal pipeline

```csharp
using Conduit;

var source = Chan.Unbounded<int>();
for (int i = 0; i < 10; i++) source.TryWrite(i);
source.Complete();

await Flow.Pipeline(source)
    .Transform<string>(async (x, ct) => $"item-{x}")
    .DrainAsync(async (s, ct) => Console.WriteLine(s));
```

---

## Core concepts

### Channels (`Chan<T>`)

- **Bounded**: `Chan.Bounded<T>(capacity)` — optional `BoundedChannelFullMode` (default: wait when full).
- **Unbounded**: `Chan.Unbounded<T>()`.
- **Metrics**: `Chan.BoundedWithMetrics<T>(capacity, out var metrics)` and `Chan.UnboundedWithMetrics<T>(out metrics)` expose `ChanMetrics` (writes, reads, drops, peak buffer depth, derived buffer depth).
- **API surface**: `WriteAsync`, `TryWrite` (records drop when write fails), `ReadAsync`, `TryRead`, `ReadAllAsync`, `WaitToReadAsync`, `WaitToWriteAsync`, `Complete`, `Completion`, `IsCompleted`.

### Pipelines (`Flow.Pipeline` → `Pipeline<T>`)

Chains of asynchronous stages; each stage typically runs on a background task and forwards to the next channel. Operations include:

| Stage | Role |
| --- | --- |
| `Transform` | Map each item; optional `RetryPolicy`. |
| `ParallelTransform` | Multiple consumers; **order not preserved**; optional retry. |
| `Filter` | Async predicate; non-matching items dropped. |
| `Batch` | Groups into `T[]` by size; **also flushes on timeout** (default 1s if omitted). |
| `RateLimit` | Smoothing delay so throughput ≤ `permitsPerSecond`. |
| `DrainTo` / `DrainAsync` | Terminal consume; awaits upstream `Completion` for fault propagation. |
| `AsChannel` | Escape hatch to continue composing manually. |

### Flow helpers (`Flow`)

- `Spawn` — fire-and-forget background work.
- `WorkerPool` — N tasks reading `ReadAllAsync` from one channel.
- `Merge` — fan-in multiple `Chan<T>` into one unbounded channel; completes output when all sources finish.

### Other types

- **`RetryPolicy`**: `Exponential`, `Linear`, `Immediate` factories; `ShouldRetry`, `OnRetry`, multipliers/delays.
- **`Select`**: multi-channel **read racing** (poll + `WaitToReadAsync`); overload with timeout yields `SelectResult<T>.Timeout` (`Index == -1`).
- **`TaskGroup`**: spawn many tasks; `WaitAsync` / `WaitSafeAsync` (latter swallows cancellation from child tasks).

---

## API reference (summary)

### `Chan`

| Member | Description |
| --- | --- |
| `Bounded` / `Unbounded` | Create channels. |
| `BoundedWithMetrics` / `UnboundedWithMetrics` | Same + `ChanMetrics`. |
| `WriteAsync` / `TryWrite` | Producers; `TryWrite` updates drop counter on failure. |
| `ReadAsync` / `TryRead` / `ReadAllAsync` | Consumers. |
| `Complete` / `Completion` | Close writer; await reader completion. |

### `Pipeline<T>`

| Method | Notes |
| --- | --- |
| `Transform<TOut>` | Sequential; default buffer 16. |
| `ParallelTransform<TOut>` | `workers` parallel readers; order undefined. |
| `Filter` | Async filter. |
| `Batch` | `batchSize` + `flushTimeout`; drains remainder when source completes. |
| `RateLimit` | Global spacing using `Stopwatch` timestamps. |
| `DrainTo` / `DrainAsync` | Sink. |

### `RetryPolicy` (struct)

- `MaxAttempts`, `BaseDelay`, `BackoffMultiplier`
- `ShouldRetry(Exception)` — optional filter
- `OnRetry(Exception, attemptNumber)` — observation hook

Retries apply **inside** `Transform` / `ParallelTransform` around each item’s transform delegate.

---

## Sample application

`Conduit.Sample` simulates **log ingestion**:

- Three producers at different rates writing to a **bounded** channel with metrics.
- Pipeline: **rate limit** → **transform + exponential retry** (e.g. on `IOException`) → **batch** → **sink**.
- Live console metrics and a final report (ingested, processed, retries, drops, peak buffer).

This is the best place to see end-to-end usage: [samples/Conduit.Sample/Program.cs](samples/Conduit.Sample/Program.cs).

---

## Design notes

- **Faults**: pipeline stages complete the downstream channel with an exception when a non-cancellation fault occurs; `DrainAsync` / `DrainTo` await `Completion` to surface failures.
- **Cancellation**: most APIs accept `CancellationToken`; cooperative cancellation is expected in user delegates.
- **Parallelism**: `ParallelTransform` uses multiple tasks all enumerating the same source — suitable for CPU/IO-bound work per item; not a single-consumer guarantee.
- **Metrics**: `TotalDropped` increments when `TryWrite` fails (e.g. full bounded channel in modes where try-write can fail).

---

## Roadmap & contributing

- Add a **LICENSE** file if you open-source the repo (none is present yet).
- Versioning and NuGet publication are defined in [src/Conduit/Conduit.csproj](src/Conduit/Conduit.csproj) (`PackageId`, `Version`, `Authors`, `Description`, `PackageTags`).

Issues and pull requests are welcome: keep changes focused, match existing style, and extend tests under `tests/Conduit.Tests/`.

---

## Tiếng Việt (tóm tắt)

**Conduit** là thư viện C# cho lập trình đồng thời kiểu kênh (channel) và pipeline trên nền `System.Threading.Channels`, có thêm giới hạn tốc độ, gom batch, retry, merge kênh, worker pool và nhóm task (`TaskGroup`). Clone repo, cài .NET 10 SDK, chạy `dotnet build` / `dotnet test` và `dotnet run` project sample như phần [Build & test](#build--test).
