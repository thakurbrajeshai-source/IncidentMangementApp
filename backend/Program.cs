using IncidentManagement.Api.Hubs;
using IncidentManagement.Api.Infrastructure;
using IncidentManagement.Api.Infrastructure.Auth;
using IncidentManagement.Api.Infrastructure.Database;
using IncidentManagement.Api.Infrastructure.RealTime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ----- Configuration --------------------------------------------------------
var cfg = builder.Configuration;
var jwtSection = cfg.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key not set");

// ----- Services -------------------------------------------------------------
builder.Services.AddControllers()
    // The API serializes EF entities (Incident -> Reporter/Assignee -> Incidents)
    // directly; those navigation references form cycles, so ignore them instead
    // of throwing the default JsonException.
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // Enums as strings ("Open", "InProgress", "Reporter"...) — matches the
        // frontend's TS types and keeps the API human-readable.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// EF Core: provider is chosen at runtime by Database:Provider
var dbProvider = cfg["Database:Provider"] ?? "Sqlite";
var connString = cfg.GetConnectionString(dbProvider)
    ?? throw new InvalidOperationException($"Connection string for {dbProvider} not found");

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseProvider(dbProvider, connString));

// Auth: JWT bearer tokens
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
        // SignalR: accept the JWT from the query string for WebSocket connections
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    opts.AddPolicy("ResolverOrAdmin", p => p.RequireRole("Resolver", "Admin"));
    opts.AddPolicy("AnyRole", p => p.RequireRole("Reporter", "Resolver", "Admin"));
});

// OTP abstraction: swap implementations via config
if (cfg.GetValue("Auth:UseTestOtp", true))
    builder.Services.AddSingleton<IOtpSender, TestOtpSender>();
else
{
    // TODO(Phase 2 production): swap to SmsOtpSender or WhatsAppOtpSender here.
    // Pick a provider that offers BOTH SMS and WhatsApp Business API so Phase 3
    // notifications can reuse the same vendor (Gupshup or Kaleyra recommended in PRD).
    throw new NotImplementedException(
        "Real OTP provider not wired up yet. Set Auth:UseTestOtp=true for dev, " +
        "or implement an SmsOtpSender/WhatsAppOtpSender and remove this throw.");
}

// Real-time notifications
builder.Services.AddSingleton<INotificationDispatcher, SignalRNotificationDispatcher>();
builder.Services.AddSignalR();

// Auto-close sweep for Resolved tickets no one confirmed (see Infrastructure/Jobs)
builder.Services.AddHostedService<IncidentManagement.Api.Infrastructure.Jobs.AutoCloseBackgroundService>();

// App services
builder.Services.AddAppServices();

// CORS — restrict in production to known origins
builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(policy =>
    {
        var origins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (origins is { Length: > 0 })
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// ----- Pipeline -------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Auto-migrate + seed in dev. In production, run migrations as a separate step.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SeedData.Run(db);
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

app.Run();

// Expose Program for WebApplicationFactory-based integration tests later
public partial class Program { }
