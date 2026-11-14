# Delivery Quotes - Assignment 8 (final project snapshot): Extension Methods, LINQ and a native C call

Introduction to Functional Programming, 2026/2027.

This is the integrated version of the delivery project. The idea from the beginning: a small courier company gets delivery requests, prices them, accepts orders while a zone still has capacity, and the dispatcher wants a few useful lists. Each assignment added one layer:

| Step | What it added |
|---|---|
| 5 | immutable models, a three-stage pipeline, a pricing policy passed in as a function |
| 6 | validated factory, order states, safe transitions |
| 7 | `Result`, `Maybe`, `Map`/`Bind`, a file boundary with real exception handling |
| 8 | two LINQ reports, custom extensions, curried scoring, lazy processing, resource notes, a C library called through P/Invoke |

## Team

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Tumurzaev Rustam | IT2505 | everything below (individual project) | all test files | this README |

## Environment and commands

- .NET SDK 8.0 or newer (`net8.0`, `RollForward=LatestMajor`). Fill in the exact version you tested with: `dotnet --version` = ____.
- Native build: `gcc` on the PATH (MinGW-w64 64-bit on Windows, gcc/clang on Linux/macOS). Not needed if a binary for your platform is already in `native/out`.
- Working directory for every command: the folder that contains `DeliveryQuotes.sln`.
- Internet once, for the xUnit packages.

```
dotnet build
dotnet test
dotnet run --project src/DeliveryQuotes
dotnet run --project src/DeliveryQuotes -- data/nope.csv
dotnet run --project src/DeliveryQuotes -- data/invalid_requests.csv
dotnet run --project src/DeliveryQuotes -- data/empty_requests.csv data/zones.csv
```

Arguments: `requests.csv zones.csv outputFolder` (all optional). Output goes to `data/out/` (`zone_revenue.csv`, `accepted_orders.json`).

Latest test outcome: run `dotnet test` and paste the summary here (`Passed: N, Failed: 0`).

## Input, rules and expected result

`data/requests.csv`: `id,customer,distanceKm,weightGrams,zone`. `data/zones.csv`: `code,name,capacityGrams`.

Rules (details in the Assignment 6 and 7 parts of the code):

- distance 1 to 50 km inclusive, weight 1 to 30000 g, zone must exist, a parcel cannot be heavier than the whole zone capacity;
- price = policy(distance) + weight surcharge (from the C library). Standard: `500 + 100*km`, Express: `800 + 150*km`;
- an order is accepted only if the accepted weight in its zone stays within the capacity. Failed requests leave no trace in the ledger.

Normal run, order desk part:

```
R1   ACCEPTED  Alice, zone A, price 700
R2   ACCEPTED  Bob, zone A, price 1700
R3   ACCEPTED  Carol, zone B, price 5900
R4   FAILED    [InvalidRequest] Request 'R4' rejected: Distance 75 km is outside the allowed range 1-50 km.
R5   FAILED    [CapacityExceeded] Zone A has 300 g free, but 500 g are needed for 'R5'.
R6   FAILED    [InvalidRequest] Request 'R6' rejected: Distance 0 km is outside the allowed range 1-50 km.
R7   ACCEPTED  Grace, zone B, price 1350
R8   ACCEPTED  Heidi, zone B, price 2550
R9   ACCEPTED  Ivan, zone C, price 1000
R10  ACCEPTED  Judy, zone C, price 1700
```

The batch quote part (pipeline only, no surcharge) still gives Standard total 14800 and Express total 22600.

Failure demos: a missing file prints `[FileMissing] File not found: 'data/nope.csv'.`, a broken file prints `[FileMalformed] ... has 5 invalid line(s) ...`. If the native library cannot be loaded, every order that passes validation fails with `[NativeUnavailable] ... Build it first`, and no order is stored.

## Task 1 - custom extension methods (`OrderQueries.cs`)

| Extension | Receiver -> return | Behaviour | Why on this type | Used in |
|---|---|---|---|---|
| `AcceptedLines` | `Ledger` -> `IEnumerable<OrderLine>` | only accepted orders, as flat lines, sorted by id | the ledger knows the three order states; reports should not repeat that filter | `Program`, both reports' input |
| `RankedBy` | `IEnumerable<OrderLine>` + `Func<OrderLine,decimal>` -> `IEnumerable<ScoredLine>` | scores every line, best first, ties by id | ranking is a query over order lines and the score is the part that changes | `Reports.DispatchList` |
| `SummarizeByZone` | `IEnumerable<OrderLine>` -> `ImmutableArray<ZoneSummary>` | group per zone, count, sum, largest | reusable grouping over order lines | `Reports.RevenueByZone` |
| `Map`, `Bind`, `Match` (`Result.cs`), `Lookup`, `ToResult` (`Maybe.cs`) | on `Result<T>` / `Maybe<T>` / dictionaries | from Assignment 7 | composing outcomes | whole app |

None of them modifies its input; they return new sequences or arrays.

## Task 2 - two LINQ reports (`Reports.cs`)

| | Dispatch list | Revenue by zone |
|---|---|---|
| User | dispatcher | manager |
| Input | accepted order lines, minimum weight, a score function | accepted order lines |
| Operators | `Where`, `Select` (with index for the rank), `OrderByDescending` + `ThenBy` (inside `RankedBy`), several stages | `GroupBy`, `Select`, `Count`, `Sum`, `Aggregate` (largest order), `OrderByDescending` + `ThenBy` |
| Output | rank, id, customer, zone, weight, distance, score | zone, orders, weight, revenue, largest order |
| Tie rule | equal score -> smaller id first | equal revenue -> smaller zone code first |

Expected on the sample data (`minWeight = 4000`):

- HeavyFirst: R3 (49.6), R8 (22), R2 (21.4), R7 (16), R10 (14)
- FarFirst: R3 (106.15), R8 (43), R10 (26), R2 (24.1), R7 (19)
- Revenue by zone: B 3 orders, 24300 g, 9800, largest 5900; C 2 orders, 7000 g, 2700, largest 1700; A 2 orders, 9700 g, 2400, largest 1700.

Reports only read `OrderLine` values. Console printing is in `ConsolePresenter`, the CSV text is built by `Reports.ZoneRevenueCsv` (pure), and writing happens in `DataFiles.WriteText`.

## Task 3 - currying and partial application (`Scoring.cs`)

```csharp
Func<decimal, Func<decimal, Func<OrderLine, decimal>>> Priority =
    weightFactor => distanceFactor => line => kg * weightFactor + km * distanceFactor;

HeavyFirst = Priority(2m)(0.5m);   // partial application
FarFirst   = Priority(0.5m)(2m);
```

`Scoring.CreateScore(ScoreConfig)` is the `Configuration -> (OrderLine -> Score)` form. The configured function is passed into `Reports.DispatchList`; no global setting changes. On order R2 (8200 g, 10 km) HeavyFirst gives 21.4 and FarFirst gives 24.1, and the dispatch lists come out in a different order (see above).

## Task 4 - deferred execution

`OrderWorkflow.ProcessLazily` is an iterator (`yield return`). It returns `ProcessedOrder` values (the result and the ledger after each request). Creating it runs nothing: the argument checks happen immediately, the body runs at the first `MoveNext`. Each step pulls one request, so the file data is processed one item at a time.

`Program` calls `ToImmutableArray()` once, on purpose. After that line printing, reports and saving use the snapshot. If the lazy sequence were enumerated twice, all requests would be processed twice (and the source pulled twice), which is exactly what `Materialising_RunsOnce_...` shows with a counter. The file is never read twice, because the file is already a snapshot (`ImmutableArray<string>`) before the workflow starts. Business state does not change when enumerating, because the ledger is immutable and the one local variable lives inside the iterator.

## Task 5 - resource ownership

| Resource | Owner | Release point | Failure path |
|---|---|---|---|
| `StreamReader` for the CSV files | `LineSource.Iterate` (`using var`) | when enumeration ends, fails, or is stopped early | `using` disposes in all three cases; tested with tracking readers (full read, `break`, `IOException`) |
| `LoadRequests` / `LoadZones` | `DataFiles.ReadLines` materialises inside `try`, so the handle is gone before the method returns | end of `ReadLines` | missing folder/file, access and IO errors become `Result.Failure` |
| `File.WriteAllText` / `File.Move` | .NET (convenience API opens and closes the file internally) | inside the call | temp file is deleted if the write fails |

I did not open a stream just to be able to write `using`. Garbage collection would close a stream eventually but not at a known time, which matters on Windows because an open handle blocks deleting the file; `LoadRequests_ReleasesTheFileHandle_...` checks that.

## Task 6 - retention and allocation note

- **Where datasets are materialised:** `DataFiles.ReadLines` (lines as `ImmutableArray<string>`), the parsed `ImmutableArray<DeliveryRequest>`, `processed` in `Program` (once), and `lines` in `Program` (accepted order lines, once, shared by both reports).
- **Deferred queries and closures:** `RankedBy` and `AcceptedLines` are lazy and capture their source and the score function. They are always consumed inside `Reports` or right after, so they do not outlive the data. `ProcessLazily` captures the ledger passed in; it is immutable so nothing can change under it.
- **Long-lived collections, caches, event subscriptions:** none. No static collections, no cache, no events. The only statics are readonly policies and scoring functions.
- **Design decision:** the file reader never leaves an open stream or a lazy file query behind; everything after the boundary works on snapshots. The ledger is persistent (`ImmutableDictionary`), so each new version shares most of its structure with the old one instead of copying everything.
- **Trade-off:** streaming (`ProcessLazily`) needs little memory and starts producing results at once, but it can only be enumerated safely once, and it keeps the source alive while it runs. A snapshot costs memory for the whole dataset but can be reused for printing, reports and saving without redoing any work. Here the dataset is small, so I take the snapshot at one deliberate place.
- I did not fix a memory leak and do not claim one.

## Task 7-9 - the native C library

Files: `native/dq_surcharge.h`, `native/dq_surcharge.c`, `native/build.sh`, `native/build.cmd`. Binding and adapter: `src/DeliveryQuotes/NativeSurcharge.cs`.

**Function contract**

| | |
|---|---|
| Name | `int32_t dq_weight_surcharge(int32_t weight_grams)` |
| Input | parcel weight in grams, valid range 1 to 30000 |
| Output | surcharge in whole currency units (integers, so no rounding of money): 0 up to 5000 g, otherwise 50 for every started kilogram above 5000 g |
| Invalid input | returns `-1` (`DQ_ERR_INVALID_WEIGHT`) |
| Memory | none allocated, no pointers, nothing to release |
| Manual check | 12300 g: 12300 - 5000 = 7300 g, started kilograms = 8, 8 * 50 = **400** |

More hand-checked values: 5000 g -> 0, 5001 g -> 50, 8200 g -> 200, 30000 g -> 1250.

**How C# calls it**

- `NativeMethods.WeightSurcharge` is a `[DllImport("deliveryquotes_native", EntryPoint = "dq_weight_surcharge", CallingConvention = Cdecl)]` declaration; `int` matches `int32_t`. No project settings are needed for `DllImport`.
- `NativeWeightSurcharge` (implements `IWeightSurcharge`) is the adapter. It validates the weight first (so C is never called with an out-of-range value), then calls C, and translates `DllNotFoundException`, `EntryPointNotFoundException`, `BadImageFormatException` into `Result.Failure(NativeUnavailable, ...)` and a `-1` answer into `NativeFailure`.
- `OrderWorkflow.Place` calls the adapter inside the Result chain, between validation and issuing the quote. The returned number is added to the price, so it shows up in the receipts, the reports and the exported files. There is no managed replacement of the function.

**Build and loading**

- Compiler used for the Linux binary in this archive: gcc 13.3.0, Ubuntu 24.04, x86-64. Command: `gcc -shared -fPIC -O2 -o native/out/libdeliveryquotes_native.so native/dq_surcharge.c` (same as `native/build.sh`).
- Windows: run `native\build.cmd` (MinGW-w64 gcc, 64-bit) or build with `cl /LD /O2 dq_surcharge.c /Fe:out\deliveryquotes_native.dll`. My Windows compiler and version: ____ (fill in).
- `dotnet build` runs the same gcc command automatically (target `BuildNative` in the csproj). If gcc is missing it only warns and uses whatever is already in `native/out`. `-p:SkipNativeBuild=true` skips it.
- Binary location: `native/out/`. The csproj copies it next to the app when it exists; in addition `NativeMethods` has a DllImport resolver that looks in `$DQ_NATIVE_DIR`, the app folder, and `native/out` found by walking up from the app folder. So tests and `dotnet run` find it after a clean build.
- Architecture: the binary must have the same bitness as the .NET process (x64 with a normal x64 SDK). A mismatch shows up as `BadImageFormatException`, which the adapter reports as `NativeUnavailable`.
- **Difficulty:** after a clean build the app runs from `src/.../bin/...` and the tests from `tests/.../bin/...`, so the library was not "next to" either one unless it was copied. Copy items are decided before the build runs, so the very first build may miss the freshly built file. The resolver that also searches `native/out` solved it. (Replace this paragraph with your own experience if it was different.)
- I checked the Linux binary directly through its exported symbol and the values above before wiring it up.

## Requirement map

| Requirement | Where |
|---|---|
| Two used extension methods | `OrderQueries.cs`: `AcceptedLines`, `RankedBy`, `SummarizeByZone` |
| Two reports with where/select/order+thenby/groupby/aggregate | `Reports.DispatchList`, `Reports.RevenueByZone` |
| Currying + two partial applications | `Scoring.Priority`, `HeavyFirst`, `FarFirst`, `CreateScore` |
| `yield return`, deferred execution, materialisation | `OrderWorkflow.ProcessLazily`, `LineSource.ReadLines`, `Program` (`ToImmutableArray`) |
| Dispose | `LineSource.Iterate`, `DataFiles.ReadLines`, notes above |
| Native C library, P/Invoke, adapter | `native/*`, `NativeSurcharge.cs`, `OrderWorkflow.Place` |
| Earlier layers kept | pipeline `QuotePipeline.cs`; factory and states `ValidatedRequest.cs`, `QuoteState.cs`, `Ledger.cs`; Result/Maybe `Result.cs`, `Maybe.cs`; boundary `DataFiles.cs` |
| Pure core / I/O outside | core: `Ledger`, `OrderWorkflow`, `Reports`, `Scoring`; I/O: `Program`, `DataFiles`, `ConsolePresenter`, `NativeSurcharge` (boundary) |
| Export / save | `DataFiles.WriteText`, `DataFiles.SaveAcceptedOrders` |

## Tests

`dotnet test` runs everything in `tests/DeliveryQuotes.Tests` (xUnit). Kept from earlier steps: pipeline tests, factory/workflow tests, `Result`/`Maybe` tests, file tests. New in this step:

| Area | Tests |
|---|---|
| Reports | `DispatchList_FiltersScoresRanksAndNumbers`, `DispatchList_EqualScores_...`, `DispatchList_EmptyInputAndNoMatches_...`, `RevenueByZone_GroupsSumsCountsAndOrdersByRevenue`, `RevenueByZone_EqualRevenue_...`, `RevenueByZone_EmptyInput_...`, `ZoneRevenueCsv_...` |
| Extensions | `AcceptedLines_ReturnsOnlyAcceptedOrdersSortedById`, `RankedBy_UsesTheGivenScore...` (plus the report tests that go through `RankedBy` and `SummarizeByZone`) |
| Configured functions | `ConfiguredScores_SameInput_DifferentResultsAndOrder`, `CreateScore_MatchesTheCurriedFunction` |
| Deferred execution | `ProcessLazily_DoesNothingUntilEnumerated_...`, `Materialising_RunsOnce_...`, `ProcessLazily_NullArgumentsAreRejectedImmediately` |
| Resource lifetime | `LineSource_OpensOnlyWhenEnumerated_AndDisposesAfterFullRead`, `LineSource_EarlyStop_...`, `LineSource_FailureWhileReading_...`, `LoadRequests_ReleasesTheFileHandle_...` |
| Native | `RealNativeCall_ReturnsManuallyCalculatedSurcharge` (7 values), `InvalidWeight_IsRejectedByTheAdapterBeforeTheNativeCall`, `NativeRejection_BecomesNativeFailure`, `LoadingProblems_BecomeClearNativeUnavailableFailures` |
| Complete workflows | `CompleteWorkflow_Normal_NativeSurchargeFeedsPricesReportsAndExport`, `CompleteWorkflow_NativeFailure_KeepsLedgerEmpty...` |
| File boundary | `LoadRequests_MissingFile_...`, `LoadRequests_MalformedLines_...`, `SaveAcceptedOrders_WhenTargetCannotBeWritten_...` and the other tests in `DataFilesTests` (temp folders, cleaned up) |

The native tests need the library: if they fail with `NativeUnavailable`, build it (`native/build.sh` or `native\build.cmd`) and run again.

## Limits and not done

- Only the Linux build of the library is included as a binary; Windows and macOS need the build step.
- Console application only. No database, login, or web interface.
- Prices are whole currency units; there are no taxes, discounts or time windows.
