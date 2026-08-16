using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IncidentManagement.Api.Infrastructure.Database;

/// <summary>
/// Used by `dotnet ef migrations add ...` so EF can construct a DbContext at design time
/// without booting the whole host. Reads config from the current directory.
/// </summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("DESIGN_CONN")
            ?? "Data Source=incident_management.db";
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        return new AppDbContext(opts);
    }
}
