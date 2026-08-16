using IncidentManagement.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Infrastructure.Database;

/// <summary>
/// EF Core provider selector. The same domain model compiles for either provider;
/// we only swap the UseXxx() call here. Production reads appsettings.Production.json
/// (Database:Provider=SqlServer) and the connection string for SqlServer.
/// </summary>
public static class ProviderFactory
{
    public static void UseProvider(this DbContextOptionsBuilder opts, string provider, string connString)
    {
        switch (provider.ToLowerInvariant())
        {
            case "sqlite":
                opts.UseSqlite(connString);
                break;
            case "sqlserver":
                opts.UseSqlServer(connString);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown Database:Provider '{provider}'. Use 'Sqlite' (dev) or 'SqlServer' (production).");
        }
    }
}
