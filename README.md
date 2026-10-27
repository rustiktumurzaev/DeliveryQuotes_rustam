# Delivery Quotes - Assignment 7: Exception Handling in FP

Introduction to Functional Programming, 2026/2027.

Third snapshot of the delivery project. In Assignment 6 failures were lists of strings and files were read with a `File.Exists` check. Now every expected failure is a `Result` with a code, missing things are `Maybe`, and the file boundary catches real exceptions and turns them into clear outcomes.

## Team

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Тумурзаев Рустам | IT2505 | `Result`, `Maybe`, `OrderWorkflow`, `Ledger` rewrite, `DataFiles` | result, lookup and file tests | this README |

## Environment

- .NET SDK 8.0 or newer (`net8.0`, `RollForward=LatestMajor`).
- Working directory: the folder that contains `DeliveryQuotes.sln`.
- Internet once, to restore the xUnit packages.

```
dotnet build
dotnet test
dotnet run --project src/DeliveryQuotes
dotnet run --project src/DeliveryQuotes -- data/nope.csv
dotnet run --project src/DeliveryQuotes -- data/invalid_requests.csv
dotnet run --project src/DeliveryQuotes -- data/empty_requests.csv data/zones.csv
```

Arguments are `requests.csv zones.csv output.json` (all optional). Latest test outcome: run `dotnet test` and paste the summary here (`Passed: N, Failed: 0`).

## What changed since Assignment 6

- `Transition`, `Placement`, `RequestValidation` and `OrderDesk` are gone. `Result<T>` replaces all of them, and `OrderWorkflow.Place` is the new chain.
- `ValidatedRequest.Create` and every `Ledger` operation return `Result<...>`.
- All-or-nothing: if a request cannot be accepted (for example the zone is full), no quote is left in the ledger. In Assignment 6 a "quote only" stayed behind; I decided that was confusing, because the customer never got an order.
- `DataFiles` is the only code that touches the disk.

## Task 1 - Result and the domain failures

`Result<T>` (in `Result.cs`) has two cases: `Success` with a value and `Failure` with an `Error(Code, Message)`. The constructors of the cases are `internal`, so code outside the assembly can only use `Result<T>.Ok(value)` and `Result<T>.Fail(error)`. `Ok(null)` throws, so a success never has a missing value, and a failure has no value to misuse.

Failures that are part of normal business, none of them uses exceptions:

| Situation | Code | Example message | Where |
|---|---|---|---|
| Broken input rule (distance, weight, id, unknown zone) | `InvalidRequest` | `Request 'R4' rejected: Distance 75 km is outside the allowed range 1-50 km.` | `ValidatedRequest.Create` |
| Item does not exist (accept or cancel an unknown id) | `NotFound` | `There is no quote 'R999'. Issue a quote first.` | `Ledger.Accept`, `Ledger.Cancel` |
| Zone has no room | `CapacityExceeded` | `Zone A has 300 g free, but 500 g are needed for 'R5'.` | `Ledger.Accept` |
| Operation not allowed in the current state | `InvalidState` | `Order 'R1' is already accepted.` | `Ledger.Issue/Accept/Cancel` |

Expected success: `Place` returns `OrderOutcome(ledger, receipt)`, for example `Receipt("R2", "Bob", "A", "Accepted", 1500)` with the Standard policy.

Operational problems from the file boundary have their own codes: `FileMissing`, `FileMalformed`, `FileAccess`.

**What is the difference between a validation failure and a workflow state?** A `Result.Failure` says "this operation did not happen, and here is why". A `QuoteState` (`Quoted`, `Accepted`, `Cancelled`) says "this is where an order is in its life". A failed accept does not move an order to some "failed" state, it just leaves the order where it was.

## Task 2 - Maybe

`Maybe<T>` has `Some(value)` and `None`. Real lookups in the app:

- `Ledger.Find(id)` returns `Maybe<QuoteState>`. `Ledger.Accept` needs the item, so it converts `None` into `Result.Failure(NotFound)` with `ToResult`. The Program also uses `Find` directly in the "Lookups" demo, where "not found" is just printed.
- `Ledger.AcceptedGrams(zone)` returns `Maybe<int>`. This is the primitive-value case: a zone with nothing accepted gives `Some(0)`, and an unknown zone gives `None`. Zero is real data, not absence. `AcceptedGrams` is used by the capacity check.
- `zones.Lookup(code)` (an extension in `Maybe.cs`) is used by the factory for the zone code.

No `default(T)` checks and no `!` operators are used to decide absence.

## Task 3 - Map and Bind in the real chain

```csharp
ValidatedRequest.Create(request, ledger.Zones, limits)                     // Result<ValidatedRequest>
    .Bind(valid    => ledger.Issue(valid, policy.PriceForDistance(...)))   // Result<Placed>
    .Bind(issued   => issued.Ledger.Accept(issued.Id))                     // Result<Placed>
    .Map(accepted  => new OrderOutcome(accepted.Ledger, ToReceipt(accepted)));  // Result<OrderOutcome>
```

- `Bind` is used when the next step can fail itself (it returns a `Result`). `Map` is used for the last step, which is a plain function that cannot fail. That is why the return types of the lambdas differ: `Result<Placed>` for `Bind`, a plain `OrderOutcome` for `Map`.
- On failure both skip their function and pass the first `Error` forward. The tests count calls to prove it (`Map_Failure_IsPreservedAndFunctionIsNotCalled`, `Bind_Failure_...`, `Chain_FirstFailureSkipsAllLaterSteps`).
- The final outcome is handled once, at the edge: `Match` in `Program.cs` and `ConsolePresenter.FormatProcessed`.

## Task 4 - exceptions at the boundary

`DataFiles.cs`:

| Problem | Exception caught | Result |
|---|---|---|
| file does not exist | `FileNotFoundException` | `FileMissing`, message contains the path |
| folder does not exist | `DirectoryNotFoundException` | `FileMissing` |
| no permission, file in use, disk errors | `UnauthorizedAccessException`, `IOException` | `FileAccess` |
| bad path text | `ArgumentException`, `NotSupportedException` | `FileAccess` |
| wrong header, bad line, empty file | no exception, parsing returns errors | `FileMalformed`, message lists the broken lines |

There is no catch-all and no empty catch. The only "quiet" catch is deleting a temp file after a failed save; there the real error is still returned.

Saving: `DataFiles.SaveAcceptedOrders` writes to `<file>.tmp` and then moves it over the real file. If the write fails, the old file is untouched. Loading never changes anything: a file with one bad line gives no partial dataset, only the failure.

No fallback is used: a missing file is a failure, the program prints it and exits with code 1.

## Task 5 - what the demo shows

Normal run output (Standard policy part):

```
R1   ACCEPTED  Alice, zone A, price 700
R2   ACCEPTED  Bob, zone A, price 1500
R3   ACCEPTED  Carol, zone B, price 5500
R4   FAILED    [InvalidRequest] Request 'R4' rejected: Distance 75 km is outside the allowed range 1-50 km.
R5   FAILED    [CapacityExceeded] Zone A has 300 g free, but 500 g are needed for 'R5'.
R6   FAILED    [InvalidRequest] Request 'R6' rejected: Distance 0 km is outside the allowed range 1-50 km.
R7   ACCEPTED  Grace, zone B, price 1300
R8   ACCEPTED  Heidi, zone B, price 2500
R9   ACCEPTED  Ivan, zone C, price 1000
R10  ACCEPTED  Judy, zone C, price 1700
```

Then the sequence checks (`Accept R999` gives `NotFound`, `Accept R1 again` gives `InvalidState`, `Cancel R5` gives `NotFound` because it was never stored, `Cancel R2` works), the lookups (`R3` found, `R404` not found, zone A has 1500 g left after the cancel, zone Z does not exist) and finally `Saved accepted orders to data/out/accepted_orders.json`.

Batch quotes are unchanged from before (Standard total 14800, Express total 22600, 8 quotes each).

File failure demos:

- `-- data/nope.csv` prints `[FileMissing] File not found: 'data/nope.csv'.` and exits with 1.
- `-- data/invalid_requests.csv` prints `[FileMalformed] ... has 5 invalid line(s): Line 3: ...` and exits with 1. Nothing is quoted.

## Requirement map

| Requirement | Where |
|---|---|
| Result with distinct cases, protected | `Result.cs` |
| Three or more domain failures | `ErrorCode.InvalidRequest/NotFound/CapacityExceeded/InvalidState`, `ValidatedRequest.cs`, `Ledger.cs` |
| Maybe for a real lookup, zero is not absence | `Maybe.cs`, `Ledger.Find`, `Ledger.AcceptedGrams` |
| Missing item becomes a Result failure | `Maybe.ToResult` in `Ledger.Accept` / `Ledger.Cancel` |
| Map and Bind in a real chain | `OrderWorkflow.Place`, `DataFiles.LoadRequests`, `Program.cs` |
| Failure skips later stages | `ResultExtensions.Map/Bind`, `ResultTests` |
| Exception boundary | `DataFiles.cs` |
| No partial updates | immutable `Ledger`, temp file + move in `DataFiles.WriteText` |
| Earlier pipeline and safe states kept | `QuotePipeline.cs`, `QuoteState.cs`, `ValidatedRequest.cs` |

## Tests

All in `tests/DeliveryQuotes.Tests`. The Assignment 5 and 6 cases are kept (ported to `Result`).

| Required case | Tests |
|---|---|
| Complete success | `CompleteChain_ValidRequest_ProducesAcceptedReceiptAndNewLedger`, `TwoPolicies_GiveDifferentPricesThroughTheSameChain`, `CompleteWorkflow_FromFilesToSavedOrders` |
| Domain failures | `DomainFailure_InvalidRequest_StopsTheChain`, `DomainFailure_CapacityExceeded_LeavesNoQuoteBehind`, `DomainFailure_NotFound_...`, `DomainFailure_InvalidState_ForRepeatedOperations`, `Factory_RejectsEachRule_...` |
| Lookup present and absent | `Lookup_FindPresentAndAbsent`, `Lookup_EmptyZoneIsSomeZero_UnknownZoneIsNone`, `Maybe_PresentAbsentAndLegitimateZero`, `Maybe_ToResult_MissingBecomesMeaningfulFailure` |
| Map success / Bind success | `Map_Success_...`, `Bind_Success_...` |
| Failure propagation | `Map_Failure_...`, `Bind_Failure_...`, `Chain_FirstFailureSkipsAllLaterSteps`, `CompleteWorkflow_MissingFile_StopsBeforeAnyLaterStepAndWritesNothing` |
| Missing file | `LoadRequests_MissingFile_...`, `LoadRequests_MissingFolder_...`, `LoadZones_ValidMissingAndMalformed` |
| Malformed file | `LoadRequests_MalformedLines_...`, `LoadRequests_WrongContent_JsonInsteadOfCsv_IsMalformed`, `LoadRequests_EmptyFile_...` |
| Save and state preservation | `SaveAcceptedOrders_WritesJson...`, `SaveAcceptedOrders_WhenTargetCannotBeWritten_...`, `ProcessAll_DoesNotChangeTheInputLedgerOrRequests` |

File tests create their own folder under the system temp directory and delete it afterwards. They do not need any file from my laptop.

## Limits

- Error messages are text; there is no localisation.
- The quote price is still `policy(distance)` only. Weight is checked but does not change the price yet (that comes in Assignment 8).
