# The Oracle — Architecture & Interview Guide

This is the deep-dive document: what the system does, why every piece exists,
and the questions you're likely to get asked about it. `README.md` is for
running it. `WALKTHROUGH.md` is the 10-minute demo script. This is for when
someone starts asking "why" and "what if."

---

## 1. The 30-second pitch

"I rebuilt a $2 Magic 8-Ball as a real-time, observable, cloud-native .NET
microservice. It's a toy on the surface — it answers one yes/no question —
but underneath it's a real-time pipeline with SignalR and a bounded channel,
a Roslyn source generator for the answer catalog, EF Core with a committed
migration, OpenTelemetry tracing, rate limiting, and a test suite. The joke
is the over-engineering; the point is that every piece is a real pattern I
can defend line by line."

---

## 2. System architecture

```
Browser (wwwroot)                      Oracle.Api
──────────────────                     ─────────────────────────────────────
 type question                        ┌─────────────┐
 click ASK ─── SignalR invoke ───────▶│  OracleHub  │  Ask(question)
 ball shakes                          └─────┬───────┘  validates + per-conn
                                             │           rate-limits, then
                                             │ enqueues   enqueues only
                                             ▼
                                       ┌──────────────────┐
                                       │ DivinationChannel │  bounded
                                       │   (singleton)     │  Channel<T>(50)
                                       └─────┬────────────┘  TryWrite: non-
                                             │               blocking
                                             │ ReadAllAsync()
                                             ▼
                                       ┌─────────────────────┐
                                       │ DivinationWorker     │ singleton
                                       │  - IOracleEngine      │ BackgroundService
                                       │  - scoped DbContext   │ (scope per msg)
                                       │  - cosmic latency     │ config-driven
                                       │  - OpenTelemetry span │
                                       └─────┬────────────────┘
                                             │ IHubContext.Clients.Client(id)
                                             ▼
 "ProphecyRevealed" ◀── SignalR push ── answer streams back
 ball stops, answer fades in, history updates
```

There's a second path: `POST /api/v1/ask` computes synchronously (no
SignalR connection to push a result back to later), using the exact same
`IOracleEngine` + mood + cosmic-latency logic. It exists to prove the core
engine works over plain HTTP, and to give something curl-able for a demo.

---

## 3. Why this shape — the architectural decisions, and how to defend them

### The hub does no work

`OracleHub.Ask` validates the question, checks a per-connection rate limit,
and writes a `DivinationRequest` onto a channel. That's it. It never calls
`OracleEngine.Divine` itself.

**Why:** decoupling ingest from processing is the actual lesson here, not the
toy. In a real system this is the difference between a request handler that's
fast and stateless (so it scales horizontally, so a slow downstream doesn't
back up your connection pool) and one that does everything inline. The ball
shaking while the worker "consults the cosmos" visualizes exactly this
decoupling — the shake is the latency of an async pipeline, made visible.

**If asked "why not just call Divine directly in the hub":** because then the
hub thread is blocked for the full cosmic-latency duration, every concurrent
connection ties up a hub invocation, and you lose the ability to independently
scale or rate-limit the processing stage versus the ingest stage. In a real
system "processing" might be a slow downstream call (LLM, payment processor,
external API) — you don't want that coupled to the connection handler.

### The channel is bounded, not unbounded

`Channel.CreateBounded<DivinationRequest>(capacity: 50)`. `Writer.TryWrite`
never blocks — if the channel is full it returns `false` immediately, which
the hub turns into a "the cosmos is busy" rejection sent back to the caller.

**Why:** an unbounded channel under sustained overload grows memory without
limit — a slow consumer plus a fast producer is an unbounded queue, which is
an OOM waiting to happen. Bounding it gives you an explicit, cheap
back-pressure signal instead. Rejecting immediately (versus blocking the
writer) keeps the hub responsive under load rather than queuing up blocked
calls.

**If asked "why 50, why not block instead of drop":** 50 is a deliberately
small, demo-tunable number — in production you'd size it from measured
throughput and acceptable queue latency, not pick it arbitrarily. Blocking
the writer (default channel behavior without `TryWrite`) is also valid — the
tradeoff is "caller waits" vs "caller gets an immediate rejection." For a
request/response-shaped interaction with a human waiting for an answer, fast
rejection is the better UX than a caller that's hung somewhere not visible to
the user.

### Singleton worker, scoped DbContext — the captive dependency trap

`DivinationWorker` is registered via `AddHostedService` (singleton,
effectively — one instance for the app's lifetime). `OracleDbContext` is
registered via `AddDbContext` (scoped — by design, since `DbContext` is not
thread-safe and is meant to represent one unit of work). You cannot inject a
scoped service into a singleton's constructor — the DI container will either
throw at startup (with scope validation on) or, worse, silently capture a
single `DbContext` instance for the app's entire lifetime, shared across
concurrent operations with no isolation.

The fix: `DivinationWorker` takes an `IServiceScopeFactory`, and for every
message it processes, it does:
```csharp
using var scope = _scopeFactory.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<OracleDbContext>();
```
A fresh scope — and fresh `DbContext` — per message, disposed after use.

**Why this matters / what it's called:** this is the "captive dependency"
problem — a longer-lived service holding a reference to a shorter-lived one.
It's one of the most common real-world DI bugs in ASP.NET Core apps (usually
discovered via a `InvalidOperationException` about "Cannot consume scoped
service... from singleton" if scope validation is on, or worse — silently —
via a mysteriously shared `DbContext` causing concurrency exceptions in
production if it's not). Knowing the fix cold (resolve a scope factory, not
the scoped service itself) is a strong, specific signal of real ASP.NET Core
experience.

### Mood and cosmic latency are server config, not client input

`OracleOptions` binds `Oracle:Mood`, `Oracle:CosmicLatencyMinMs/MaxMs` from
`appsettings.json`, overridable via env var (`Oracle__Mood=Grumpy`), resolved
fresh per request via `IOptionsMonitor<OracleOptions>` (so a config change
or redeploy picks it up without a code change). Neither the hub method nor
the REST endpoint accepts mood as a parameter from the caller.

**Why:** a client-supplied weight feeding directly into a selection loop is
a resource-exhaustion vector (pass an enormous weight, force a huge
allocation) and a trust-boundary violation — the "cosmos's mood" is the
service's calibration knob, not something a caller should control.
`IOptionsMonitor` over a plain static read is the idiomatic way to make a
setting hot-reloadable without restarting the process.

### Source generator, not a runtime list

`Oracle.Generators` is a true **incremental** Roslyn generator
(`IIncrementalGenerator`, not the legacy `ISourceGenerator`). It reads
`prophecies.txt` via `AdditionalTextsProvider`, parses it, and emits a
`ProphecyCatalog.All` as a compile-time `ImmutableArray<Prophecy>` — there is
no array of strings anywhere in the runtime assembly.

**Why incremental, specifically:** the legacy `ISourceGenerator` re-runs its
entire `Execute` on every keystroke in the IDE — brutal for IDE responsiveness
on anything non-trivial. `IIncrementalGenerator` builds a pipeline of cached,
incremental steps (`AdditionalTextsProvider.Where(...).Select(...)`) so only
inputs that actually changed get reprocessed. This is the API Microsoft
pushed everyone toward starting with .NET 6/C# 10, specifically for IDE
performance.

**Gotcha I actually hit building this:** the `AdditionalFiles` entry that
feeds the generator has to be declared in the *consuming* project
(`Oracle.Core.csproj`), not the generator's own project
(`Oracle.Generators.csproj`). `AdditionalTextsProvider` reads additional
files from the compilation the generator is running *inside of* — and since
the generator runs as an analyzer attached to `Oracle.Core`'s build, it only
sees `Oracle.Core`'s additional files. This is a sharp edge worth mentioning
if asked about source generators — it shows you've actually built one, not
just read about them.

**Malformed input isn't silently dropped.** A bad line in `prophecies.txt`
(unparseable, or an unrecognized `Kind`) emits a real Roslyn diagnostic
(`ORCL001`/`ORCL002`) — visible as a build warning (which, with
`TreatWarningsAsErrors` on this project, becomes a hard build failure). The
whole premise of this generator is build-time safety; a silent fallback over
its own input data would undercut that.

### EF Core: a real migration, not a schema-on-the-fly

`db.Database.MigrateAsync()` runs on startup, against a migration that was
generated by the real `dotnet ef migrations add` tool — not hand-authored.
A hand-written migration file is missing the `[Migration("...")]` attribute,
the `.Designer.cs` snapshot, and the `ModelSnapshot.cs` that EF's tooling
needs to track what's already applied; faking one is a common AI-generated-
code smell and it doesn't actually work with `dotnet ef database update`.

**Why migrate-on-startup instead of a separate manual step:** zero-friction
demo requirement — clone, run, it just works, no "did you remember to run
the migration" step. In a real production system you'd usually run
migrations as a separate release step (so a bad migration doesn't take down
every replica simultaneously on deploy) — worth saying out loud if asked,
since blindly doing this in prod at scale is a legitimate anti-pattern.

### Observability: a custom ActivitySource and Meter, not just the defaults

`OracleTelemetry` defines a dedicated `ActivitySource("Oracle.Api")` and
`Meter("Oracle.Api")`, registered into OpenTelemetry's tracing and metrics
pipelines (`.AddSource(...)`, `.AddMeter(...)`) alongside the built-in
ASP.NET Core and `HttpClient` auto-instrumentation. Every divination gets an
`oracle.divine` span tagged with question length, prophecy kind, and cosmic
latency, plus a `Counter<long>` (`oracle.prophecies.revealed`, tagged by
kind) and a `Histogram<double>` (`oracle.cosmic_latency_ms`).

**Why a custom source instead of relying only on auto-instrumentation:**
auto-instrumentation gives you HTTP-shaped telemetry (route, status code,
duration) — it knows nothing about your domain. The business-meaningful
signal here ("how often does the cosmos say yes, and how long does
consultation actually take") only exists if you instrument it yourself. This
is the difference between "the app has observability" and "the app emits
HTTP logs."

### Two rate limiters, because SignalR isn't HTTP-shaped per call

The REST endpoint (`POST /api/v1/ask`) uses ASP.NET Core's built-in
`RateLimiter` middleware with a named fixed-window policy
(`RequireRateLimiting("global")`, 5 requests / 10s) — this works because
each REST call is a discrete HTTP request that goes through the middleware
pipeline.

A SignalR connection is a single long-lived HTTP upgrade — only the initial
`/negotiate` request goes through ASP.NET Core's rate-limiting middleware;
individual `Ask` invocations inside that one connection do not. So
`OracleHub` has its own lightweight manual limiter: a
`ConcurrentDictionary<string, Queue<DateTimeOffset>>` keyed by connection id,
checked and pruned on every `Ask` call.

**If asked "why not use `IHubFilter` for this":** `IHubFilter` is the more
"proper" extensibility point for cross-cutting hub concerns (and is the right
call if you need this policy shared across multiple hubs). For one hub with
one simple policy, a dictionary and a lock is less code and does the same
job — reaching for the abstraction before there's a second consumer would be
premature. Good instinct to name this one if the interviewer pushes on it:
it shows you know the "more correct" tool exists and chose the simpler one
deliberately, not out of not knowing better.

### Tests prove the things that actually need proving

- **Determinism:** two independently-constructed `OracleEngine` instances,
  each seeded with `new Random(42)`, produce the same first draw. This is
  the right shape for a determinism test — calling `Divine` twice on the
  *same* instance doesn't prove determinism, it just proves the RNG advances
  (an earlier draft of this test made exactly that mistake).
- **Mood bias, statistically:** 1000 draws with a fixed seed; `Optimistic`
  must produce strictly more `Affirmative` results than `Grumpy`. Asserting
  on a fixed-seed count instead of "roughly X%" avoids a flaky test while
  still proving the weighting logic actually biases the distribution.
- **Generator output:** a dedicated test project compiles a snippet through
  the real generator and asserts the generated `ProphecyCatalog.All` has all
  20 entries and all three kinds — proving the generator's output, not just
  its lack of exceptions.

---

## 4. Likely interview questions, by topic

**Real-time / SignalR**
- *Why SignalR instead of raw WebSockets?* Transport negotiation/fallback
  (WebSockets → Server-Sent Events → long polling) handled for you, typed
  hub methods instead of hand-rolled message parsing, automatic reconnect on
  the JS client side (`withAutomaticReconnect()`), and it's the idiomatic
  ASP.NET Core answer — less code, same outcome, better supported.
- *What happens if a client disconnects mid-divination?* The worker still
  finishes the work and tries to push to that `ConnectionId` — `SendAsync`
  to a dead connection is a no-op, not an exception, so this fails safe.
  The DB write still happens either way, so the log is accurate even if the
  push is lost. (Said honestly: a production system that cared about this
  would want a correlation/retry or a "fetch missed results" endpoint — a
  fair thing to flag as a known gap if asked.)

**Concurrency**
- *What's the difference between `Channel<T>` and `BlockingCollection<T>`?*
  `Channel<T>` is async-first (`ReadAllAsync`, `WriteAsync` as awaitable
  operations, no thread blocked while waiting) — `BlockingCollection<T>`
  blocks the calling thread. For an ASP.NET Core app where thread-pool
  threads are precious, `Channel<T>` is the modern, correct choice.
- *Why `SingleReader = true, SingleWriter = false` on the channel options?*
  One `DivinationWorker` instance reads; multiple concurrent hub
  invocations (and the REST path, if it were wired through the channel) can
  write. Declaring this lets the channel pick a cheaper internal
  implementation than the fully general multi-reader/multi-writer case.

**Source generators**
- *Incremental vs. legacy generators — what's actually different under the
  hood?* Legacy `ISourceGenerator.Execute` gets the whole `Compilation` and
  re-runs fully on every edit. `IIncrementalGenerator` builds a pipeline of
  `IncrementalValueProvider` steps with cached, comparable intermediate
  values — Roslyn only re-executes the steps downstream of what actually
  changed. It's the difference between re-doing all the work and memoizing
  it.

**EF Core**
- *Why SQLite instead of SQL Server/Postgres for a "real" showcase?*
  Zero-friction local dev and demo — no external DB server, no connection
  string juggling, `docker compose up` or even `dotnet run` alone just
  works. The EF Core patterns demonstrated (DbContext lifetime, migrations,
  `MigrateAsync`) are provider-agnostic; swapping to Postgres is a
  `UseNpgsql` + connection-string change, not an architecture change.
- *What's the tradeoff of migrate-on-startup you'd flag in a system design
  interview?* Every replica starting concurrently could race to apply a
  migration simultaneously; EF Core's migrations lock table
  (`__EFMigrationsLock`, visible in the startup logs) handles the race
  safely, but it's still usually better to run migrations as a single
  release-pipeline step ahead of rolling out new replicas, not as part of
  app startup, in a system with more than one instance.

**Deployment**
- *Why Render and not Vercel?* Vercel's serverless functions are short-lived
  and stateless, with no first-class ASP.NET Core runtime — this app needs a
  persistent WebSocket connection (SignalR) and a long-running background
  process (`DivinationWorker`), neither of which fits a serverless
  invocation model. Render runs the actual Docker container as a
  continuously-running process, which is what this architecture requires.
- *What's the known limitation of the current deploy?* Render's free tier
  filesystem is ephemeral — the SQLite file resets on redeploy/restart.
  Fine for a demo (history just resets occasionally); a production version
  would want a managed Postgres or a persistent disk.

**"Did you use AI to build this?"**
Answer this one directly and with specifics — it's increasingly a credible,
even expected, answer rather than something to dodge: "Yes — I used an AI
agent to scaffold it quickly, then went through it myself: I found and fixed
a compile-breaking bug, rewrote the core real-time architecture because the
first pass had collapsed the hub/worker split into a plain synchronous RPC
call, replaced a fake hand-written EF migration with a real one, fixed a
SQLite query-translation bug that only showed up at runtime, and wrote the
frontend and deployment config that had been claimed done but didn't
actually exist. I can walk through why every piece is built the way it is,
including the parts I had to go back and fix." That's a stronger answer than
pretending every line was typed by hand — and it's true.

---

## 5. Known limitations (good to volunteer, not just survive being asked)

- SQLite on Render's free tier is ephemeral — acceptable for a demo, not for
  production data.
- No correlation/retry if a SignalR connection drops between the ask and the
  worker pushing the result back (the DB write still succeeds; the live push
  is best-effort).
- The per-connection Hub rate limiter is in-process memory — fine for a
  single instance, would need a distributed store (Redis) behind multiple
  replicas.
- `docker compose up` was written carefully but not verified in an
  environment with Docker available — worth confirming before leaning on it
  live in an interview.
