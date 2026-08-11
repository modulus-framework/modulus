using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ProcureFlow.Modules.SupplierManagement.Infrastructure;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef</c> /
/// <c>modulus migrate</c>) to construct <see cref="SupplierManagementDbContext"/>
/// without the application's DI container or an HTTP request. At runtime the app
/// still builds the context through DI (see <see cref="SupplierManagementModule"/>);
/// this type is only used when scaffolding or applying migrations.
/// </summary>
/// <remarks>
/// The connection string is read from the <c>SUPPLIERMANAGEMENT_CONNECTION</c>
/// environment variable when set â€” so CI/CD can point migrations at the real
/// database â€” otherwise it falls back to the module's default design-time
/// connection string. The tenant/user/dispatcher arguments are design-time stubs
/// from <see cref="DesignTimeContext"/>: migrations only build the model, never
/// live request state.
/// </remarks>
public sealed class SupplierManagementDbContextFactory
    : IDesignTimeDbContextFactory<SupplierManagementDbContext>
{
    public SupplierManagementDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SUPPLIERMANAGEMENT_CONNECTION")
            ?? "Host=localhost;Database=procureflow_supplier_management;Username=pf;Password=Pf-dev-1234";

        var options = new DbContextOptionsBuilder<SupplierManagementDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SupplierManagementDbContext(
            options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
