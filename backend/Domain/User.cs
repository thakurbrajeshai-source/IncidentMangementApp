using System.ComponentModel.DataAnnotations.Schema;

namespace IncidentManagement.Api.Domain;

/// <summary>
/// Auth identity for all roles. The PRD originally had `name` (single field).
/// Per project owner decision, reporters self-register with First/Last/Email/Mobile,
/// so the user record carries all four. Resolver/Admin are provisioned by an admin
/// and can be given a name + email by the admin who creates them.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Mobile is the login identity. Stored in E.164-ish form (digits, optional +).
    // Display formatting (+91 98xxx xxxxx) is a UI concern.
    public string Mobile { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public UserRole Role { get; set; } = UserRole.Reporter;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navigation
    public ICollection<Incident> ReportedIncidents { get; set; } = new List<Incident>();
    public ICollection<Incident> AssignedIncidents { get; set; } = new List<Incident>();
}
