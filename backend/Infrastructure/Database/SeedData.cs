using IncidentManagement.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Infrastructure.Database;

/// <summary>
/// Idempotent seed: runs on every app start in dev. Categories are static seed
/// (HasData) so migrations are reproducible. Test users + a couple of sample
/// tickets are inserted only if the table is empty, so this is safe to re-run.
/// </summary>
public static class SeedData
{
    /// <summary>Static seed for EF HasData. Ids are fixed ints so migrations don't
    /// re-seed the table on every model change.</summary>
    public static readonly Category[] CategoriesStatic = new[]
    {
        new Category { Id = 1,  Name = "Login/Portal Access",          Description = "Login failures, password resets, portal access issues" },
        new Category { Id = 2,  Name = "Payment/Disbursement",          Description = "Payment processing, disbursement delays or failures" },
        new Category { Id = 3,  Name = "KYC/Verification",              Description = "KYC document verification issues" },
        new Category { Id = 4,  Name = "Bank Details/IFSC/Penny Drop",  Description = "Bank account, IFSC, penny drop verification" },
        new Category { Id = 5,  Name = "Lead/Application Status",       Description = "Lead or application status queries" },
        new Category { Id = 6,  Name = "App/Technical Error",           Description = "App crashes, bugs, technical errors" },
        new Category { Id = 7,  Name = "Call/Connectivity",             Description = "Phone/connectivity issues" },
        new Category { Id = 8,  Name = "Document/Upload",               Description = "Document upload failures or issues" },
        new Category { Id = 9,  Name = "Other/General Query",           Description = "Any other general query" },
    };

    public static void Run(AppDbContext db)
    {
        // Categories are seeded via HasData (above). For test users we use runtime
        // seed-if-empty, because they reference a GUID primary key which doesn't
        // play well with HasData migration diffing.
        if (!db.Users.Any())
        {
            var now = DateTime.UtcNow;
            // Mobile is stored in the normalized form (digits only, no + or 91 prefix)
            // so it matches what the login Normalize() produces.
            db.Users.AddRange(
                Make("9822011234", "Akash",  "Verma",   "akash@example.com",  UserRole.Reporter, now),
                Make("9988776543", "Priya",  "Sharma",  "priya@example.com",  UserRole.Reporter, now),
                Make("9023455678", "Rohit",  "Mehta",   "rohit@example.com",  UserRole.Reporter, now),
                Make("8765422110", "Sneha",  "Iyer",    "sneha@example.com",  UserRole.Reporter, now),
                Make("9123455667", "Vikram", "Reddy",   "vikram@example.com", UserRole.Reporter, now),

                Make("9000000001", "Darshan",  "Patil",   "darshan@example.com",  UserRole.Resolver, now),
                Make("9000000002", "Vamshi",   "K",       "vamshi@example.com",   UserRole.Resolver, now),
                Make("9000000003", "Ganesh",   "Gupta",   "ganesh@example.com",   UserRole.Resolver, now),
                Make("9000000004", "Shivam",   "Singh",   "shivam@example.com",   UserRole.Resolver, now),
                Make("9000000005", "Sumit",    "Kumar",   "sumit@example.com",    UserRole.Resolver, now),
                Make("9000000006", "Ravindra", "Patwa",   "ravindra@example.com", UserRole.Resolver, now),

                Make("9000000099", "Nilesh",  "Gaidhani","nilesh@example.com",   UserRole.Admin,    now)
            );
            db.SaveChanges();
        }

        // A couple of sample incidents so the UI isn't empty on first run.
        if (!db.Incidents.Any())
        {
            var reporter = db.Users.First(u => u.Mobile == "9822011234");
            var resolver = db.Users.First(u => u.Mobile == "9000000001");
            var admin    = db.Users.First(u => u.Mobile == "9000000099");

            var i1 = new Incident
            {
                TicketRef = "INC-1001",
                ReporterId = reporter.Id,
                CategoryId = 6, // App/Technical Error
                Description = "App crashes when submitting KYC form on Android.",
                Status = IncidentStatus.InProgress,
                CurrentAssigneeId = resolver.Id,
                CreatedAt = DateTime.UtcNow.AddHours(-6),
            };
            var a1 = new IncidentAssignment
            {
                IncidentId = i1.Id,
                ResolverId = resolver.Id,
                AssignmentType = AssignmentType.SelfPicked,
                AssignedAt = i1.CreatedAt.AddMinutes(15),
            };
            db.Incidents.Add(i1);
            db.IncidentAssignments.Add(a1);

            var i2 = new Incident
            {
                TicketRef = "INC-1002",
                ReporterId = reporter.Id,
                CategoryId = 2, // Payment
                Description = "Disbursement for July pending since 5 days.",
                Status = IncidentStatus.Open,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
            };
            db.Incidents.Add(i2);

            db.SaveChanges();
            _ = admin; // admin not yet used in seed beyond having an account
        }
    }

    private static User Make(string mobile, string first, string last, string email,
        UserRole role, DateTime now) => new()
    {
        Mobile = mobile,
        FirstName = first,
        LastName = last,
        Email = email,
        Role = role,
        Status = UserStatus.Active,
        CreatedAt = now,
    };
}
