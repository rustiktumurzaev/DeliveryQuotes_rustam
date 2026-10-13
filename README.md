# Delivery Quotes

Functional pipeline that turns delivery requests into price quotes. Assignment 5: Composing Functions Using Pipelining.

## Topic and users

- Topic: calculate delivery quotes.
- Intended users: dispatchers of a small courier company.
- Planned team: see the Team table below.

## Input

CSV file with a header line and fields `id,customer,distanceKm`.

| Field | Rule |
|---|---|
| id | required, not empty |
| customer | required, not empty |
| distanceKm | required decimal number, invariant culture (`2`, `2.5`), must not be negative |

Zero is a valid parsed value. Invalid fields reject the line with a message. If any line is invalid, no quotes are produced.

Missing input versus an empty dataset: a file that contains only the header line is a **valid empty dataset** (result: no quotes, total 0). A file with no lines at all, or whose first line is not `id,customer,distanceKm`, is **rejected** with a message, and a missing file stops the program with an error. None of these cases returns a fabricated "successful" result.

## Business rules

- A request is deliverable if `1 <= distanceKm <= 50` (limits are inclusive and passed as `DeliveryLimits`).
- Standard policy: `500 + 100 * km`.
- Express policy: `800 + 150 * km`.
- Quotes are ordered by price, then by id.

## Pipeline stages

| Stage | Method | Input -> Output | Rule | Example |
|---|---|---|---|---|
| 1 | `QuotePipeline.SelectDeliverable` | `IReadOnlyList<DeliveryRequest>`, `DeliveryLimits` -> `ImmutableArray<DeliveryRequest>` | keep requests within the distance limits | `R4 (75 km)` is removed, `R3 (50 km)` stays |
| 2 | `QuotePipeline.PriceRequests` | `IReadOnlyList<DeliveryRequest>`, `Func<decimal, decimal>` -> `ImmutableArray<PricedRequest>` | price = policy function applied to the distance | `R1 (2 km)` -> 700 (Standard) |
| 3 | `QuotePipeline.BuildSummary` | `IReadOnlyList<PricedRequest>` -> `QuoteSummary` | order by price then id, count, total | 4 quotes, total 8300 |

`QuotePipeline.Run` composes the three stages with ordinary function calls.

## Configurable rule

- Higher-order function: `QuotePipeline.PriceRequests` (parameter `priceForDistance`, invoked as `priceForDistance(r.DistanceKm)`).
- Policies: `PricePolicies.Standard` and `PricePolicies.Express` (immutable, defined in `PricePolicies.cs`).
- The policy is selected in `Program.cs` and passed to `Run` as an argument. No mutable global state is used.

## Expected results (documented before running)

Dataset `data/requests.csv`: R1 2 km, R2 10 km, R3 50 km (max boundary), R4 75 km (rejected, too far), R5 1 km (min boundary), R6 0 km (rejected, below minimum).

| Policy | R5 | R1 | R2 | R3 | Count | Total |
|---|---|---|---|---|---|---|
| Standard | 600 | 700 | 1500 | 5500 | 4 | 8300 |
| Express | 950 | 1100 | 2300 | 8300 | 4 | 12650 |

Trace of R1 (`Alice, 2 km`) with Standard:
`string "R1,Alice,2"` -> `DeliveryRequest("R1","Alice",2)` -> stage 1 keeps it -> stage 2 `PricedRequest(R1, 700)` -> stage 3 `Quote("R1","Alice",2,700)`.

## Environment

- .NET SDK 8.0 or newer (projects target `net8.0` with `RollForward=LatestMajor`).
- Working directory: the folder that contains `DeliveryQuotes.sln`.
- Internet access is needed once to restore the xUnit NuGet packages.

## Commands

```
dotnet build
dotnet run --project src/DeliveryQuotes
dotnet run --project src/DeliveryQuotes -- data/invalid_requests.csv
dotnet run --project src/DeliveryQuotes -- data/empty_requests.csv
dotnet test
```

- Normal run: prints Standard and Express results from `data/requests.csv`.
- Failure demo: `data/invalid_requests.csv` prints 4 errors (lines 3 to 6) and exits with code 1.
- Empty demo: `data/empty_requests.csv` prints `No quotes.` and total 0 for both policies.

## Requirement map

| Requirement | Implementation |
|---|---|
| Typed immutable models | `Models.cs` (records, `ImmutableArray`) |
| Parsing and validation at the boundary | `RequestParser.TryParseRequest`, `RequestParser.ParseCsvLines` |
| Three pure stages | `QuotePipeline.cs` |
| Visible composition | `QuotePipeline.Run` |
| Higher-order function and two policies | `QuotePipeline.PriceRequests`, `PricePolicies.cs` |
| Policy selected outside the pipeline | `Program.cs` |
| Input and output separated from calculations | `Program.cs`, `ConsolePresenter.cs` |
| Tests | `tests/DeliveryQuotes.Tests/QuotePipelineTests.cs` |

## Tests

| Case | Tests |
|---|---|
| Normal workflow | `NormalWorkflow_StandardPolicy_ProducesDocumentedResult` |
| Empty dataset | `EmptyDataset_ProducesEmptySummary` |
| Boundary value | `BoundaryValues_AreIncludedAtLimitsAndExcludedOutside` |
| Invalid and missing input | `InvalidOrMissingLine_IsRejectedWithMessage`, `EmptyFileOrWrongHeader_IsRejectedAndIsNotAnEmptyDataset`, `ParseCsvLines_ReportsEveryInvalidLineWithNumber`, `MissingInput_ThrowsInsteadOfReturningSuccess`, `ZeroDistance_IsValidInputAndNotReplacedByDefault` |
| Two policies | `TwoPolicies_GiveDifferentResultsOnSameInput` |
| Purity and stages | `Stages_DoNotModifyCallerOwnedInput`, `SelectDeliverable_ReturnsOnlyRequestsWithinLimits`, `BuildSummary_OrdersByPriceThenIdAndSumsTotal`, `PriceRequests_InvokesDelegateOncePerRequest`, `SameInputAndPolicy_ProduceSameOutput` |

Latest test outcome: run `dotnet test` and write the result here (for example `Passed: N, Failed: 0`). I did not write a number here because I want it to come from my own run.

## Team

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Tumurzaev (full name: fill in) | IT2505 | individual project: models, parsing and validation, the three pipeline stages, two price policies, console output | all tests in `QuotePipelineTests.cs` | this README |

## Defense cheat sheet

- Run: `dotnet run --project src/DeliveryQuotes` shows both policies. Standard total 8300, Express total 12650.
- Trace R1: see "Trace of R1" above. Stage 1 `SelectDeliverable` (filter), stage 2 `PriceRequests` (calls the delegate), stage 3 `BuildSummary` (order, count, total).
- Higher-order function: `QuotePipeline.PriceRequests`; the delegate call is `priceForDistance(r.DistanceKm)`.
- Why the stages are pure: they only read their arguments and return new `ImmutableArray` values. Reading the file, printing and choosing the policy happen in `Program.cs` and `ConsolePresenter.cs`.
- Typical small change: edit a policy in `PricePolicies.cs` (for example Standard to `600 + 100 * km`) or the limits in `Program.cs`, then update the expected numbers in the README table and in `NormalWorkflow_StandardPolicy_ProducesDocumentedResult` / `TwoPolicies_GiveDifferentResultsOnSameInput`.
