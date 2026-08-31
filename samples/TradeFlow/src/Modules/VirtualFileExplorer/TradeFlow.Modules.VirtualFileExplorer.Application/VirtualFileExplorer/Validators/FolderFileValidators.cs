using FluentValidation;
using TradeFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;

namespace TradeFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Validators;

public sealed class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);
    }
}

public sealed class RenameFolderCommandValidator : AbstractValidator<RenameFolderCommand>
{
    public RenameFolderCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);
    }
}

public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255);
        RuleFor(x => x.SizeBytes)
            .GreaterThanOrEqualTo(0);
    }
}

public sealed class RenameFileCommandValidator : AbstractValidator<RenameFileCommand>
{
    public RenameFileCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);
    }
}
