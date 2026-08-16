using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Database;
using IncidentManagement.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Infrastructure.Jobs;

/// <summary>
/// Background sweep that closes Resolved tickets the reporter never confirmed.
/// Fires every Incident:AutoCloseCheckMinutes (default 10), closes any ticket in
/// Resolved state older than Incident:AutoCloseAfterHours (default 48, per PRD).
/// Uses a fresh DI scope per sweep so scoped services (AppDbContext) resolve correctly.
/// </summary>
public class AutoCloseBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AutoCloseBackgroundService> _log;

    public AutoCloseBackgroundService(
        IServiceScopeFactory scopeFactory, IConfiguration cfg, ILogger<AutoCloseBackgroundService> log)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkMinutes = _cfg.GetValue("Incident:AutoCloseCheckMinutes", 10);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(checkMinutes));
        do
        {
            try { await CloseExpiredAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { _log.LogError(ex, "Auto-close sweep failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CloseExpiredAsync(CancellationToken ct)
    {
        var hours = _cfg.GetValue("Incident:AutoCloseAfterHours", 48);
        var cutoff = DateTime.UtcNow.AddHours(-hours);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IncidentService>();

        var ids = await db.Incidents
            .Where(i => i.Status == IncidentStatus.Resolved && i.ResolvedAt != null && i.ResolvedAt < cutoff)
            .Select(i => i.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            // No-op if the reporter already confirmed (AutoCloseAsync re-checks status).
            try { await svc.AutoCloseAsync(id, ct); _log.LogInformation("Auto-closed incident {Id}", id); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { _log.LogWarning(ex, "Auto-close failed for incident {Id}", id); }
        }
    }
}
