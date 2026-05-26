namespace Thymus.Poc.Models;

public record ThreadSummary(
    string Title,
    string Board,
    string Url,
    string? LastPostBy,
    string? LastPostAt
);
