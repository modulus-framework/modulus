using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Costing.Application;
using TradeFlow.Modules.Costing.Domain.Entities;
using TradeFlow.Modules.Costing.Domain.Repositories;
using TradeFlow.Modules.Costing.Domain.Services;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Costing.Application.BackgroundJobs;

/// <summary>
/// Runs the periodic landed-cost FX revaluation: builds the revaluation run,
/// persists it (audit trail), and lets the module dispatch the summary domain
/// event (→ integration event) on commit.
/// </summary>
public sealed class RunPeriodicRevaluationHandler(
    ILandedCostRevaluationService revaluationService,
    IRevaluationRunRepository runRepository,
    IUnitOfWork unitOfWork,
    ILogger<RunPeriodicRevaluationHandler> logger)
    : ICommandHandler<RunPeriodicRevaluationCommand, Result<RevaluationRun>>
{
    public async Task<Result<RevaluationRun>> HandleAsync(RunPeriodicRevaluationCommand request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation(
                "Starting landed-cost revaluation for tenant {TenantId}, period ending {PeriodEnd}",
                request.TenantId, request.PeriodEnd);

            RevaluationRun run = await revaluationService.RevaluatePeriodAsync(
                request.TenantId, request.PeriodEnd, request.CurrentFxRates, ct);

            await runRepository.AddAsync(run, ct);
            await unitOfWork.CommitAsync(ct);

            logger.LogInformation(
                "Revaluation run {RunId} completed: {Sheets} sheets scanned, {Variances} variances, FX gain/loss {GainLoss:N2} BDT",
                run.Id, run.SheetsScanned, run.Variances.Count, run.TotalFxGainLossBdt);

            return Result.Success(run);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Landed-cost revaluation failed for tenant {TenantId}", request.TenantId);
            return Result.Failure<RevaluationRun>(
                Error.Failure("Revaluation.Failed", $"Landed-cost revaluation failed: {ex.Message}"));
        }
    }
}