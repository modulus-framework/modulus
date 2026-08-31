using TradeFlow.Modules.Procurement.Application.Dtos;
using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Application.Commands;

// ── Contract Commands ──

public sealed record CreateContractCommand(
    string ContractNumber,
    Guid VendorId,
    ContractType Type,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal CapValue,
    string? Notes,
    List<ContractLineInput> Lines) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record SubmitContractCommand(
    Guid ContractId) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record ApproveContractCommand(
    Guid ContractId) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record RenewContractCommand(
    Guid ContractId,
    DateOnly NewEndDate,
    decimal? NewCapValue,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record TerminateContractCommand(
    Guid ContractId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record CancelContractCommand(
    Guid ContractId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record RecordContractConsumptionCommand(
    Guid ContractId,
    decimal Amount) : Modulus.Mediator.Abstractions.ICommand<Result<ContractResponse>>;

public sealed record AddContractLineCommand(
    Guid ContractId,
    Guid? ItemId,
    string? FreeText,
    decimal UnitPrice,
    decimal? MinQuantity,
    string? EscalationJson,
    string Notes) : Modulus.Mediator.Abstractions.ICommand<Result<ContractLineResponse>>;

public sealed record AddContractDocumentCommand(
    Guid ContractId,
    string DocumentType,
    string S3Key,
    DateOnly? ExpiryDate) : Modulus.Mediator.Abstractions.ICommand<Result<ContractDocumentResponse>>;

public sealed record AddContractMilestoneCommand(
    Guid ContractId,
    string Title,
    DateOnly? DueDate,
    string? Deliverables,
    string? SlaJson) : Modulus.Mediator.Abstractions.ICommand<Result<ContractMilestoneResponse>>;

public sealed record CompleteContractMilestoneCommand(
    Guid ContractId,
    Guid MilestoneId) : Modulus.Mediator.Abstractions.ICommand<Result<ContractMilestoneResponse>>;

public record ContractLineInput(
    Guid? ItemId,
    string? FreeText,
    decimal UnitPrice,
    decimal? MinQuantity,
    string? EscalationJson,
    string Notes);
