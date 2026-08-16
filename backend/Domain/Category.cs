namespace IncidentManagement.Api.Domain;

/// <summary>
/// Seeded with the 9 categories from the PRD. Kept as a table (not an enum) so admins
/// can add or rename categories later without a code change / migration.
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}
