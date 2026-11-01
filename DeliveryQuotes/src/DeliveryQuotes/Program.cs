using System.Collections.Immutable;
using DeliveryQuotes;

var requestsPath = args.Length > 0 ? args[0] : Path.Combine("data", "requests.csv");
var zonesPath = args.Length > 1 ? args[1] : Path.Combine("data", "zones.csv");
var outputFolder = args.Length > 2 ? args[2] : Path.Combine("data", "out");

var limits = new DeliveryLimits(MinDistanceKm: 1m, MaxDistanceKm: 50m);

// Input boundary: both files are loaded as Results and chained. The first failure stops
// everything and is handled once, here.
var loaded = DataFiles.LoadRequests(requestsPath)
    .Bind(requests => DataFiles.LoadZones(zonesPath).Map(zones => (Requests: requests, Zones: zones)));

return loaded.Match(
    data => Run(data.Requests, data.Zones),
    error =>
    {
        Console.Error.WriteLine(ConsolePresenter.FormatError(error));
        return 1;
    });

int Run(ImmutableArray<DeliveryRequest> requests, ImmutableArray<Zone> zones)
{
    // Workflow 1: batch quotes with the pipeline (Assignment 5).
    Console.WriteLine("== Batch quotes ==");
    foreach (var policy in PricePolicies.All)
    {
        Console.WriteLine(ConsolePresenter.FormatSummary(policy.Name, QuotePipeline.Run(requests, limits, policy)));
    }

    // Workflow 2: validate -> native surcharge -> quote -> accept, processed lazily.
    // The weight surcharge comes from the C library through the adapter.
    Console.WriteLine("== Order desk (Standard policy + native weight surcharge) ==");
    var lazy = OrderWorkflow.ProcessLazily(Ledger.Create(zones), requests, PricePolicies.Standard, limits, new NativeWeightSurcharge());

    // Deliberate materialisation: the whole run happens here, once. Everything after this line
    // (printing, reports, saving) reads the same snapshot and does not process anything again.
    var processed = lazy.ToImmutableArray();

    foreach (var item in processed)
    {
        Console.WriteLine(ConsolePresenter.FormatProcessed(item));
    }

    var ledger = processed.IsEmpty ? Ledger.Create(zones) : processed[^1].LedgerAfter;

    // Workflow 3: sequence checks and cancellation.
    Console.WriteLine();
    Console.WriteLine("== Sequence checks ==");
    Console.WriteLine(ConsolePresenter.FormatStep("Accept R999 (never quoted)", ledger.Accept("R999")));
    Console.WriteLine(ConsolePresenter.FormatStep("Accept R1 again", ledger.Accept("R1")));
    var cancelled = ledger.Cancel("R5");
    Console.WriteLine(ConsolePresenter.FormatStep("Cancel R5 (never accepted)", cancelled));
    Console.WriteLine();

    // Workflow 4: reports on the accepted orders.
    var lines = ledger.AcceptedLines().ToImmutableArray();

    Console.WriteLine(ConsolePresenter.FormatDispatch(
        "Dispatch list, parcels from 4000 g, HeavyFirst scoring",
        Reports.DispatchList(lines, 4000, Scoring.HeavyFirst)));
    Console.WriteLine(ConsolePresenter.FormatDispatch(
        "Dispatch list, parcels from 4000 g, FarFirst scoring",
        Reports.DispatchList(lines, 4000, Scoring.FarFirst)));

    var zoneReport = Reports.RevenueByZone(lines);
    Console.WriteLine(ConsolePresenter.FormatZoneSummary(zoneReport));

    // Output boundary: export the report and save the accepted orders.
    var exportResult = DataFiles.WriteText(Path.Combine(outputFolder, "zone_revenue.csv"), Reports.ZoneRevenueCsv(zoneReport))
        .Bind(_ => DataFiles.SaveAcceptedOrders(Path.Combine(outputFolder, "accepted_orders.json"), ledger));

    Console.WriteLine(exportResult.Match(
        path => $"Saved zone_revenue.csv and {path}",
        ConsolePresenter.FormatError));

    return exportResult.Match(_ => 0, _ => 1);
}
