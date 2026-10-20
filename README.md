# Delivery Quotes - Assignment 6: Handling Temporal Coupling

Introduction to Functional Programming, 2026/2027.

This is the second snapshot of my delivery project. Assignment 5 gave me a pipeline that prices a batch of delivery requests. In this step I added the part that was missing: an actual order flow where a customer gets a quote, accepts it, and the courier company runs out of capacity at some point. That flow has a natural order (quote, then accept, then maybe cancel), and the point of this assignment was to stop relying on the caller to remember that order.

## Team

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
|Tumurzaev Rustam | IT2505 | validated factory, states, ledger, order desk | factory, state and sequence tests | this README |

## Environment

- .NET SDK 8.0 or newer (projects target `net8.0`, `RollForward=LatestMajor`).
- Working directory for every command: the folder that contains `DeliveryQuotes.sln`. The default data paths are relative to it.
- Internet is needed once to restore the xUnit packages.

```
dotnet build
dotnet test
dotnet run --project src/DeliveryQuotes
dotnet run --project src/DeliveryQuotes -- data/invalid_requests.csv data/zones.csv
dotnet run --project src/DeliveryQuotes -- data/empty_requests.csv data/zones.csv
```

Latest test outcome: run `dotnet test` and paste the summary line here (`Passed: N, Failed: 0`). I did not want to leave a number in the README that I had not seen myself.

## What the program does

Input is two CSV files.

`data/requests.csv`: `id,customer,distanceKm,weightGrams,zone`

`data/zones.csv`: `code,name,capacityGrams` (the weight a zone can carry per day)

The program does three things with them:

1. **Batch quotes** (this is the Assignment 5 pipeline, unchanged): filter by distance, price with a policy, summarise. Run once with the Standard policy and once with Express.
2. **Order desk**: every request goes through validation, then a quote is issued, then the quote is accepted if the zone still has room.
3. **Sequence checks**: accept something that was never quoted, accept twice, cancel something that was only quoted, cancel an accepted order and accept again.

Domain rules (checked by the factory):

| Rule | Reason text starts with |
|---|---|
| id is not blank | `Request id is required.` |
| customer is not blank | `Customer name is required.` |
| distance is between 1 and 50 km (inclusive) | `Distance ... is outside the allowed range` |
| weight is between 1 and 30000 g | `Weight ... is outside the allowed range` |
| zone exists | `Zone 'X' does not exist.` |
| one parcel is not heavier than the whole zone capacity | `Parcel of ... is heavier than the whole daily capacity` |

Accept rule: the weight of the parcel must fit into what is left of the zone capacity (accepted orders only, quotes do not reserve anything). A parcel that fits exactly is accepted.

## Task 1 - the hidden dependency (unsafe "before" version)

This is how I would have written it without thinking about order. It is **not** part of the project, only an illustration.

```csharp
public class Order
{
    public DeliveryRequest Request;
    public decimal Price;
    public bool IsValid;
    public bool IsAccepted;

    public void Validate()  => IsValid = Request.DistanceKm is >= 1 and <= 50 && Zones.ContainsKey(Request.ZoneCode);
    public void Quote(PricePolicy p) => Price = p.PriceForDistance(Request.DistanceKm);
    public void Accept()    => IsAccepted = true;
}

var order = new Order { Request = request };
order.Accept();          // succeeds although nobody validated or priced anything
// order.Price is 0, IsValid is false, but IsAccepted is true
```

- Prerequisite: `Validate()` must run before `Quote()`, and `Quote()` before `Accept()`.
- Order changed or step skipped: the order is "accepted" with price 0, for a 75 km trip to a zone that does not exist, and the zone capacity is never checked.
- Nothing in the API tells the caller about this order. You have to know it.

Safe version, the one in the project:

```csharp
var placement = OrderDesk.Place(ledger, request, policy, limits);
```

`Place` is the only thing a caller needs. Inside it, `ValidatedRequest.Create` returns a value that `Ledger.Issue` requires, and `Ledger.Accept` only works on an order that is in the `Quoted` state. There is no call order left to remember.

## Task 2 - the factory

`ValidatedRequest.Create(request, zones, limits)` in `ValidatedRequest.cs`.

- It checks all six rules from the table and returns **all** reasons at once, not just the first one.
- `ValidatedRequest` has a **private constructor** and properties with **only getters**. From outside the class there is no way to build one without going through `Create`, and no way to change one afterwards (a `with` expression does not compile either, because there are no init setters).
- It is used by the real workflow: `OrderDesk.Place` calls it for every row of the input file.

## Task 3 - the cases

I used closed record hierarchies (private base constructor, nested sealed cases), so nobody outside can add a new case.

| Type | Cases | Meaning |
|---|---|---|
| `RequestValidation` | `Valid(ValidatedRequest)`, `Invalid(reasons)` | result of the factory |
| `QuoteState` | `Quoted(request, price)`, `Accepted(request, price)`, `Cancelled(request, refund)` | life of one order |
| `Transition` | `Applied(next ledger, state)`, `Refused(reasons)` | result of a ledger operation |
| `Placement` | `Rejected(reasons)`, `QuoteOnly(ledger, quote, reasons)`, `Accepted(ledger, order)` | result of the whole desk for one request |

## Task 4 - pattern matching

Switch expressions decide the outcome in:

- `Ledger.Accept` and `Ledger.Cancel` (what is allowed depends on the current `QuoteState`),
- `OrderDesk.Place` and its helpers (they decide what the caller sees),
- `ConsolePresenter.FormatPlacement` (how it is printed).

C# cannot prove that a class hierarchy is exhaustive, so every switch has a `_` branch. That branch **refuses**; it never treats an unknown case as valid. There are no casts and no `!` operators in the matching code.

## Task 5 - safe operations

- `Ledger` is immutable. `Issue`, `Accept` and `Cancel` return a **new** ledger inside `Applied`, or `Refused` with reasons. A refusal has no ledger inside it, so the caller can only keep using the old one, which was never touched.
- Order of operations is enforced by states. `Accept` needs a `Quoted` order, `Cancel` needs an `Accepted` one.
- Repeated operations: **rejected with a reason** (`already accepted`, `already cancelled`, `A quote for 'R1' already exists`).
- Missing prerequisite: `Accept("R999")` without a quote gives `There is no quote 'R999'. Issue a quote first.`
- One thing that is a legitimate business order: cancelling an accepted order frees its weight, so a quote that was blocked before can be accepted afterwards (`R5` in the demo).

## Expected output of the normal run

Batch quotes (10 rows in the file, `R4` and `R6` are outside the distance limits):

| Policy | Order of quotes | Count | Total |
|---|---|---|---|
| Standard | R5 600, R1 700, R9 1000, R7 1300, R2 1500, R10 1700, R8 2500, R3 5500 | 8 | 14800 |
| Express | R5 950, R1 1100, R9 1550, R7 2000, R2 2300, R10 2600, R8 3800, R3 8300 | 8 | 22600 |

Order desk, Standard policy:

```
R1   ACCEPTED   Alice, zone A, price 700
R2   ACCEPTED   Bob, zone A, price 1500
R3   ACCEPTED   Carol, zone B, price 5500
R4   REJECTED   Distance 75 km is outside the allowed range 1-50 km.
R5   QUOTED ONLY price 600, not accepted: Zone A has 300 g free, but 500 g are needed.
R6   REJECTED   Distance 0 km is outside the allowed range 1-50 km.
R7   ACCEPTED   Grace, zone B, price 1300
R8   ACCEPTED   Heidi, zone B, price 2500
R9   ACCEPTED   Ivan, zone C, price 1000
R10  ACCEPTED   Judy, zone C, price 1700
```

Sequence checks: `Accept R999` refused, `Accept R1 again` refused, `Cancel R5` refused (only quoted), `Cancel R2` done, then `Accept R5` done.

Failure demo: `data/invalid_requests.csv` prints 5 rejected lines and exits with code 1. Empty demo: `data/empty_requests.csv` prints "No quotes." for both policies and an empty desk.

## Requirement map

| Requirement | Where |
|---|---|
| Hidden dependency example | this README, "Task 1" |
| Factory with two or more checks, used by the app | `ValidatedRequest.Create`, called in `OrderDesk.Place` |
| No unchecked construction | private constructor + getter-only properties in `ValidatedRequest.cs` |
| Distinct cases | `RequestValidation.cs`, `QuoteState.cs`, `Transition.cs`, `Placement.cs` |
| Pattern matching that affects results | `Ledger.Accept`, `Ledger.Cancel`, `OrderDesk.cs` |
| Explicit prerequisites | `Ledger.Issue(ValidatedRequest, price)`, `Ledger.Accept` needs a `Quoted` state |
| Pure decisions, no partial updates | `Ledger.cs` (immutable), `OrderDesk.cs`; file reading only in `Program.cs` |
| Earlier pipeline kept | `QuotePipeline.cs`, `PricePolicies.cs` (unchanged from Assignment 5) |

## Tests

Files: `tests/DeliveryQuotes.Tests/OrderDeskTests.cs` (new) and `QuotePipelineTests.cs` (Assignment 5 tests, updated for the two new fields).

| Required case | Test |
|---|---|
| Factory success | `Factory_ValidRequest_CreatesUsableValue` |
| Factory rejection (each rule) | `Factory_RejectsEachRule_AndSaysWhy` (7 rows), `Factory_ReportsAllReasonsAtOnce`, `Factory_NullRequest_IsInvalidNotAnException` |
| Distinct cases | `Placement_CoversRejectedQuoteOnlyAndAccepted`, `Cancel_OnlyAcceptedOrdersCanBeCancelled_AndOnlyOnce` |
| Normal operation | `NormalWorkflow_ValidRequest_BecomesAcceptedOrderWithPrice` |
| Missing prerequisite | `Accept_WithoutQuote_IsRefused` |
| Repeated operation | `Accept_Twice_SecondCallIsRefusedAndNothingChanges`, `Issue_SameIdTwice_IsRefused` |
| Boundary value | `Factory_ExactLimits_AreAccepted`, `Capacity_ExactlyFull_IsAccepted_OneGramMoreIsNot` |
| State preservation | `FailedTransition_LeavesOriginalLedgerUnchanged`, `CancelFreesCapacityForLaterAcceptance` |
| Zone file parsing | `ZoneParser_ReadsZonesAndRejectsBadLines` |
| Assignment 5 behaviour | everything in `QuotePipelineTests` |

## Limits of this stage

- Files are still read with `File.ReadAllLines` after a `File.Exists` check. A proper exception boundary is planned for Assignment 7.
- Reasons are plain strings for now. In Assignment 7 they will turn into structured errors.
