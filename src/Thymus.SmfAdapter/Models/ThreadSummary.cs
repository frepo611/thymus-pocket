namespace Thymus.SmfAdapter.Models;

public sealed record ThreadSummary(
    string Title,
    string Board,
    string Url,
    string? LastPostBy,
    string? LastPostAt);
