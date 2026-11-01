namespace Inventory.Api.Services.Treasury;

/// <summary>Persisted allocation data needed to calculate a voucher's effective invoice settlements.</summary>
internal sealed class InvoiceSettlementSource
{
    public int VoucherId { get; init; }
    public int InvoiceId { get; init; }
    public decimal Amount { get; init; }
    public decimal VoucherTotal { get; init; }
}

/// <summary>
/// Applies bounced/cancelled cheque reversals to invoice allocations. Unallocated voucher value absorbs
/// reversals first; any remaining reversal is apportioned proportionally across that voucher's invoices.
/// </summary>
internal static class InvoiceSettlementMath
{
    public static Dictionary<int, decimal> Calculate(
        IEnumerable<InvoiceSettlementSource> sources,
        IReadOnlyDictionary<int, decimal> reversedChequeAmounts)
    {
        var result = new Dictionary<int, decimal>();

        foreach (var voucherGroup in sources
                     .Where(s => s.VoucherId > 0 && s.InvoiceId > 0 && s.Amount > 0)
                     .GroupBy(s => s.VoucherId))
        {
            var allocations = voucherGroup
                .GroupBy(s => s.InvoiceId)
                .Select(g => new AllocationShare(g.Key, g.Sum(s => s.Amount)))
                .Where(a => a.Amount > 0)
                .OrderBy(a => a.InvoiceId)
                .ToList();

            if (allocations.Count == 0) continue;

            var allocatedTotal = allocations.Sum(a => a.Amount);
            var voucherTotal = Math.Max(0, voucherGroup.First().VoucherTotal);
            var reversedAmount = reversedChequeAmounts.TryGetValue(voucherGroup.Key, out var reversed)
                ? Math.Max(0, reversed)
                : 0;

            // Unallocated/prepayment value absorbs a returned cheque before invoice allocations are reopened.
            var effectiveTotal = Math.Min(allocatedTotal, Math.Max(0, voucherTotal - reversedAmount));
            if (effectiveTotal >= allocatedTotal)
            {
                foreach (var allocation in allocations)
                    result[allocation.InvoiceId] = result.GetValueOrDefault(allocation.InvoiceId) + allocation.Amount;
                continue;
            }

            // Largest-remainder apportionment keeps proportional reductions exact to the currency's cent.
            var ratio = effectiveTotal / allocatedTotal;
            foreach (var allocation in allocations)
            {
                var exact = allocation.Amount * ratio;
                allocation.EffectiveAmount = Math.Floor(exact * 100m) / 100m;
                allocation.Fraction = exact - allocation.EffectiveAmount;
            }

            var remainder = effectiveTotal - allocations.Sum(a => a.EffectiveAmount);
            foreach (var allocation in allocations.OrderByDescending(a => a.Fraction).ThenBy(a => a.InvoiceId))
            {
                if (remainder < 0.01m) break;
                if (allocation.EffectiveAmount + 0.01m > allocation.Amount) continue;
                allocation.EffectiveAmount += 0.01m;
                remainder -= 0.01m;
            }

            // Defensive fallback for unusual precision/data, still preserving the voucher's exact total.
            if (remainder > 0)
            {
                foreach (var allocation in allocations.OrderBy(a => a.InvoiceId))
                {
                    var extra = Math.Min(remainder, allocation.Amount - allocation.EffectiveAmount);
                    if (extra <= 0) continue;
                    allocation.EffectiveAmount += extra;
                    remainder -= extra;
                    if (remainder <= 0) break;
                }
            }

            foreach (var allocation in allocations)
                result[allocation.InvoiceId] = result.GetValueOrDefault(allocation.InvoiceId) + allocation.EffectiveAmount;
        }

        return result;
    }

    private sealed class AllocationShare(int invoiceId, decimal amount)
    {
        public int InvoiceId { get; } = invoiceId;
        public decimal Amount { get; } = amount;
        public decimal EffectiveAmount { get; set; }
        public decimal Fraction { get; set; }
    }
}
