using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

public static class MessagingEntityFrameworkInboxExtensions
{
    /// <summary>
    /// Makes each consume attempt one transaction on <typeparamref name="TContext"/> (which must map
    /// <see cref="InboxRecord"/> with <c>ApplyModulusMessagingInbox</c>), and skips messages a consumer already handled.
    /// Call after <c>AddModulusMessaging</c>.
    /// </summary>
    public static IServiceCollection AddModulusMessagingEntityFrameworkInbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IConsumeUnitOfWork>(sp => new EfConsumeUnitOfWork<TContext>(sp.GetRequiredService<TContext>()));
        services.AddScoped<IMessageInbox>(sp => new EfMessageInbox<TContext>(sp.GetRequiredService<TContext>()));
        return services;
    }
}
