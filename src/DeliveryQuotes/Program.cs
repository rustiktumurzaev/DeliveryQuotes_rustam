using DeliveryQuotes;

var requestsPath = args.Length > 0 ? args[0] : Path.Combine("data", "requests.csv");
var zonesPath = args.Length > 1 ? args[1] : Path.Combine("data", "zones.csv");
var outputPath = args.Length > 2 ? args[2] : Path.Combine("data", "out", "accepted_orders.json");

var limits = new DeliveryLimits(MinDistanceKm: 1m, MaxDistanceKm: 50m);

// The boundary: both files are loaded as Results and chained. The first failure stops everything,
// and it is handled exactly once, here, with Match.
var loaded = DataFiles.LoadRequests(requestsPath)
    .Bind(requests => DataFiles.LoadZones(zonesPath).Map(zones => (Requests: requests, Zones: zones)));

return loaded.Match(
    data => Run(data.Requests, data.Zones),
    error =>
    {
        Console.Error.WriteLine(ConsolePresenter.FormatError(error));
        return 1;
    });

int Run(IReadOnlyList<DeliveryRequest> requests, IEnumerable<Zone> zones)
{
    // Workflow 1: batch quotes with the pipeline (Assignment 5).
    Console.WriteLine("== Batch quotes ==");
    foreach (var policy in PricePolicies.All)
    {
        Console.WriteLine(ConsolePresenter.FormatSummary(policy.Name, QuotePipeline.Run(requests, limits, policy)));
    }

    // Workflow 2: validate -> quote -> accept as one Result chain.
    Console.WriteLine("== Order desk (Standard policy) ==");
    var processed = OrderWorkflow.ProcessAll(Ledger.Create(zones), requests, PricePolicies.Standard, limits);
    foreach (var item in processed)
    {
        Console.WriteLine(ConsolePresenter.FormatProcessed(item));
    }

    var ledger = processed.IsEmpty ? Ledger.Create(zones) : processed[^1].LedgerAfter;

    // Workflow 3: sequence checks, lookups and cancellation.
    Console.WriteLine();
    Console.WriteLine("== Sequence checks ==");
    Console.WriteLine(ConsolePresenter.FormatStep("Accept R999 (never quoted)", ledger.Accept("R999")));
    Console.WriteLine(ConsolePresenter.FormatStep("Accept R1 again", ledger.Accept("R1")));
    Console.WriteLine(ConsolePresenter.FormatStep("Cancel R5 (never accepted)", ledger.Cancel("R5")));
    var cancelled = ledger.Cancel("R2");
    Console.WriteLine(ConsolePresenter.FormatStep("Cancel R2", cancelled));
    ledger = cancelled.Match(p => p.Ledger, _ => ledger);

    Console.WriteLine();
    Console.WriteLine("== Lookups ==");
    Console.WriteLine(ConsolePresenter.FormatLookup("R3", ledger.Find("R3")));
    Console.WriteLine(ConsolePresenter.FormatLookup("R404", ledger.Find("R404")));
    Console.WriteLine(ConsolePresenter.FormatGrams("A", ledger.AcceptedGrams("A")));
    Console.WriteLine(ConsolePresenter.FormatGrams("Z", ledger.AcceptedGrams("Z")));

    // Output boundary: save the accepted orders.
    var saved = DataFiles.SaveAcceptedOrders(outputPath, ledger);
    Console.WriteLine();
    Console.WriteLine(saved.Match(
        path => $"Saved accepted orders to {path}",
        ConsolePresenter.FormatError));

    return saved.Match(_ => 0, _ => 1);
}
