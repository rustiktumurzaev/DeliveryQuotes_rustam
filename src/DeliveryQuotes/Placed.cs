namespace DeliveryQuotes;

// Value that travels through the Result chain: the new ledger and the order that was just changed.
public sealed record Placed(Ledger Ledger, string Id, QuoteState State);

// What the dispatcher finally sees for one order.
public sealed record Receipt(string Id, string Customer, string ZoneCode, string Status, decimal Price);

public sealed record OrderOutcome(Ledger Ledger, Receipt Receipt);

// One request pushed through the workflow: the Result for it, and the ledger after it.
// On failure LedgerAfter is the same ledger that went in, so nothing is half-updated.
public sealed record ProcessedOrder(string RequestId, Result<Receipt> Result, Ledger LedgerAfter);
