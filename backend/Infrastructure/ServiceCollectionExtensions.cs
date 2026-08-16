using IncidentManagement.Api.Infrastructure.Auth;
using IncidentManagement.Api.Infrastructure.RealTime;

namespace IncidentManagement.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection s)
    {
        s.AddSingleton<JwtService>();
        s.AddScoped<Services.IncidentService>();
        s.AddScoped<Services.CommentService>();
        s.AddScoped<Services.NotificationService>();
        s.AddScoped<Services.UserDirectoryService>();
        s.AddScoped<Services.AuthService>();
        s.AddSingleton<Auth.IAuthConfigProtector, Auth.AesAuthConfigProtector>();
        s.AddScoped<Services.WorkflowService>();
        s.AddHttpClient<Services.WorkflowExecutionService>(c =>
        {
            // Workflow steps may call any host the builder points at. 30s cap is
            // enforced per request inside the engine; this is just the client default.
            c.Timeout = TimeSpan.FromSeconds(60);
        });
        return s;
    }
}
