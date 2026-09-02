using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.BackgroundJobs;

/// <summary>
/// Triggers the periodic landed-cost FX revaluation for a period close.
/// Invoked by a scheduler (or an operator endpoint); produces a persisted
/// <see cref="RevaluationRun"/> audit trail and the P&amp;L summary event.
/// </summary>
public sealed record RunPeriodicRevaluationCommand(
    Guid TenantId,
    DateOnly PeriodEnd,
    IReadOnlyDictionary<string, decimal> CurrentFxRates)
    : Modulus.Mediator.Abstractions.ICommand<Result<RevaluationRun>>;