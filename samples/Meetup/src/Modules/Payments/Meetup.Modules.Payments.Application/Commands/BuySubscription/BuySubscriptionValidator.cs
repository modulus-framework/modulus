using FluentValidation;

namespace Meetup.Modules.Payments.Application.Commands.BuySubscription;

public sealed class BuySubscriptionValidator : AbstractValidator<BuySubscriptionCommand>
{
    public BuySubscriptionValidator()
    {
        RuleFor(x => x.PayerLogin).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
