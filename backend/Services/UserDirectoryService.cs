using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Admin operations on the user directory: create resolvers/admins, list users,
/// change roles, disable accounts. Reporters are NOT created here — they
/// self-register via the OTP flow (PRD section 4).
/// </summary>
public class UserDirectoryService
{
    private readonly AppDbContext _db;
    public UserDirectoryService(AppDbContext db) { _db = db; }

    public Task<List<User>> ListAsync(UserRole? role, CancellationToken ct = default)
    {
        var q = _db.Users.AsQueryable();
        if (role.HasValue) q = q.Where(u => u.Role == role.Value);
        return q.OrderBy(u => u.Role).ThenBy(u => u.FirstName).ToListAsync(ct);
    }

    public async Task<User> CreateStaffAsync(string mobile, string firstName, string lastName,
        string? email, UserRole role, CancellationToken ct = default)
    {
        // Validate inputs
        if (role == UserRole.Reporter)
            throw new ArgumentException("Reporters self-register via OTP; use that flow instead.");
        
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required and cannot be blank.");
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required and cannot be blank.");
        if (firstName.Trim().Length < 2)
            throw new ArgumentException("First name must be at least 2 characters.");
        if (lastName.Trim().Length < 2)
            throw new ArgumentException("Last name must be at least 2 characters.");
            
        var norm = AuthService.Normalize(mobile)
            ?? throw new ArgumentException("Invalid mobile number. Use 10-digit Indian format (e.g., 9876543210 or +91 9876543210).");
        if (await _db.Users.AnyAsync(u => u.Mobile == norm, ct))
            throw new InvalidOperationException("A user with this mobile number already exists.");
        var u = new User
        {
            Mobile = norm, FirstName = firstName.Trim(), LastName = lastName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Role = role, Status = UserStatus.Active,
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync(ct);
        return u;
    }

    public async Task DisableAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new KeyNotFoundException();
        u.Status = UserStatus.Disabled;
        await _db.SaveChangesAsync(ct);
    }
}
