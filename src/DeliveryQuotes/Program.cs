using DeliveryQuotes;

var path = args.Length > 0 ? args[0] : Path.Combine("data", "requests.csv");

if (!File.Exists(path))
{
    Console.Error.WriteLine($"Input file not found: {path}");
    return 1;
}

string[] lines;
try
{
    lines = File.ReadAllLines(path);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Could not read {path}: {ex.Message}");
    return 1;
}

var dataset = RequestParser.ParseCsvLines(lines);

if (dataset.Errors.Length > 0)
{
    Console.Error.WriteLine(ConsolePresenter.FormatErrors(dataset.Errors));
    return 1;
}

var limits = new DeliveryLimits(MinDistanceKm: 1m, MaxDistanceKm: 50m);

foreach (var policy in PricePolicies.All)
{
    var summary = QuotePipeline.Run(dataset.Requests, limits, policy);
    Console.WriteLine(ConsolePresenter.FormatSummary(policy.Name, summary));
}

return 0;
