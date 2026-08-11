using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ProcureFlow.Modules.Finance.Infrastructure;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef</c> /
/// <c>modulus migrate</c>) to construct <see cref="FinanceDbContext"/>
/// without the application's DI container or an HTTP request. At runtime the app
/// still builds the context through DI (see <see cref="FinanceModule"/>);
/// this type is only used when scaffolding or applying migrations.
/// </summary>
/// <remarks>
/// The connection string is read from the <c>FINANCE_CONNECTION</c>
/// environment variable when set â€” so CI/CD can point migrations at the real
/// database â€” otherwise it falls back to the module's default design-time
/// connection string. The tenant/user/dispatcher arguments are design-time stubs
/// from <see cref="DesignTimeContext"/>: migrations only build the model, never
/// live request state.
/// </remarks>
public sealed class FinanceDbContextFactory
    : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FINANCE_CONNECTION")
            ?? "Host=localhost;Database=procureflow_finance;Username=pf;Password=Pf-dev-1234";

        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FinanceDbContext(
            options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
