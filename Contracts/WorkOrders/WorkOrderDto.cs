using System;

namespace Contracts.WorkOrders;

/// <summary>
/// Represents a work order in a lightweight form that is suitable for UI consumption.
/// </summary>
public sealed record WorkOrderDto(
    string Id,
    string Number,
    string? Line,
    string? PartNo,
    string? Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? DueUtc);
