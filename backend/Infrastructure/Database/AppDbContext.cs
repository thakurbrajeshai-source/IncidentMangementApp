using IncidentManagement.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Infrastructure.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentAssignment> IncidentAssignments => Set<IncidentAssignment>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowInput> WorkflowInputs => Set<WorkflowInput>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowStepResult> WorkflowStepResults => Set<WorkflowStepResult>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Mobile).IsUnique();
            e.Property(x => x.Mobile).HasMaxLength(20).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Role).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasData(SeedData.CategoriesStatic); // applied as static seed; runtime seed below is for test users
        });

        b.Entity<Incident>(e =>
        {
            e.ToTable("Incidents");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TicketRef).IsUnique();
            e.Property(x => x.TicketRef).HasMaxLength(20).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.Property(x => x.RejectionReason).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne(x => x.Reporter).WithMany(r => r.ReportedIncidents)
                .HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CurrentAssignee).WithMany(r => r.AssignedIncidents)
                .HasForeignKey(x => x.CurrentAssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.RejectedBy).WithMany()
                .HasForeignKey(x => x.RejectedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<IncidentAssignment>(e =>
        {
            e.ToTable("IncidentAssignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.AssignmentType).HasConversion<int>();
            e.HasOne(x => x.Incident).WithMany(a => a.Assignments)
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Resolver).WithMany()
                .HasForeignKey(x => x.ResolverId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IncidentId, x.AssignedAt });
        });

        b.Entity<Comment>(e =>
        {
            e.ToTable("Comments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Message).IsRequired();
            e.Property(x => x.TaggedUserIds).HasMaxLength(2000); // delimited Guid strings
            e.HasOne(x => x.Incident).WithMany(c => c.Comments)
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany()
                .HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IncidentId, x.CreatedAt });
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Incident).WithMany()
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.ReadAt });
        });

        b.Entity<Workflow>(e =>
        {
            e.ToTable("Workflows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.CreatedBy).WithMany()
                .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<WorkflowStep>(e =>
        {
            e.ToTable("WorkflowSteps");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            e.Property(x => x.UrlTemplate).IsRequired();
            e.Property(x => x.HeadersJson).HasMaxLength(8000).IsRequired();
            e.Property(x => x.BodyTemplate).HasMaxLength(8000);
            e.Property(x => x.AuthType).HasConversion<int>();
            e.Property(x => x.AuthConfigEncrypted).HasMaxLength(8000);
            e.HasOne(x => x.Workflow).WithMany(w => w.Steps)
                .HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorkflowId, x.StepOrder }).IsUnique();
        });

        b.Entity<WorkflowInput>(e =>
        {
            e.ToTable("WorkflowInputs");
            e.HasKey(x => x.Id);
            e.Property(x => x.FieldName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Workflow).WithMany(w => w.Inputs)
                .HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorkflowId, x.FieldName }).IsUnique();
        });

        b.Entity<WorkflowRun>(e =>
        {
            e.ToTable("WorkflowRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.HasOne(x => x.Workflow).WithMany(w => w.Runs)
                .HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TriggeredBy).WithMany()
                .HasForeignKey(x => x.TriggeredById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Incident).WithMany()
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.StartedAt });
            e.HasIndex(x => x.IncidentId);
        });

        b.Entity<WorkflowStepResult>(e =>
        {
            e.ToTable("WorkflowStepResults");
            e.HasKey(x => x.Id);
            e.Property(x => x.StepName).HasMaxLength(200).IsRequired();
            e.Property(x => x.RequestPayload).HasMaxLength(10000);
            e.Property(x => x.ResponsePayload).HasMaxLength(100000);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.HasOne(x => x.Run).WithMany(r => r.StepResults)
                .HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RunId, x.StepOrder });
        });
    }
}
