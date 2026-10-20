using DeliveryQuotes;

var requestsPath = args.Length > 0 ? args[0] : Path.Combine("data", "requests.csv");
var zonesPath = args.Length > 1 ? args[1] : Path.Combine("data", "zones.csv");

if (!File.Exists(requestsPath) || !File.Exists(zonesPath))
{
    Console.Error.WriteLine($"Input file not found: {(File.Exists(requestsPath) ? zonesPath : requestsPath)}");
    return 1;
}

var dataset = RequestParser.ParseCsvLines(File.ReadAllLines(requestsPath));
var zoneData = ZoneParser.ParseCsvLines(File.ReadAllLines(zonesPath));

if (dataset.Errors.Length > 0 || zoneData.Errors.Length > 0)
{
    Console.Error.WriteLine(ConsolePresenter.FormatErrors(dataset.Errors.AddRange(zoneData.Errors)));
    return 1;
}

var limits = new DeliveryLimits(MinDistanceKm: 1m, MaxDistanceKm: 50m);

// Workflow 1 (Assignment 5): batch quotes with the pipeline, one run per pricing policy.
Console.WriteLine("== Batch quotes ==");
foreach (var policy in PricePolicies.All)
{
    var summary = QuotePipeline.Run(dataset.Requests, limits, policy);
    Console.WriteLine(ConsolePresenter.FormatSummary(policy.Name, summary));
}

// Workflow 2 (Assignment 6): every request goes through validation -> quote -> accept.
Console.WriteLine("== Order desk (Standard policy) ==");
var ledger = Ledger.Create(zoneData.Zones);
foreach (var request in dataset.Requests)
{
    var placement = OrderDesk.Place(ledger, request, PricePolicies.Standard, limits);
    Console.WriteLine(ConsolePresenter.FormatPlacement(request.Id, placement));

    ledger = placement switch
    {
        Placement.Accepted a => a.Next,
        Placement.QuoteOnly q => q.Next,
        _ => ledger
    };
}

// Workflow 3: invalid sequences and repeated operations are refused, state stays consistent.
Console.WriteLine();
Console.WriteLine("== Sequence checks ==");
Console.WriteLine(ConsolePresenter.FormatTransition("Accept R999 (never quoted)", ledger.Accept("R999")));
Console.WriteLine(ConsolePresenter.FormatTransition("Accept R1 again", ledger.Accept("R1")));
Console.WriteLine(ConsolePresenter.FormatTransition("Cancel R5 (only quoted)", ledger.Cancel("R5")));

if (ledger.Cancel("R2") is Transition.Applied cancelled)
{
    Console.WriteLine(ConsolePresenter.FormatTransition("Cancel R2", cancelled));
    ledger = cancelled.Next;
    Console.WriteLine(ConsolePresenter.FormatTransition("Accept R5 after R2 freed capacity", ledger.Accept("R5")));
}

return 0;
