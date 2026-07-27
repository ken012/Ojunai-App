using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[Route("api/staff")]
public class StaffController : OjunaiBaseController
{
    private readonly AppDbContext _db;
    private readonly PlanGuard _planGuard;
    private readonly IWhatsAppService _whatsApp;
    private readonly IAlertService _alerts;
    private readonly IActivityLogger _activity;

    public StaffController(AppDbContext db, PlanGuard planGuard, IWhatsAppService whatsApp, IAlertService alerts, IActivityLogger activity)
    {
        _db = db; _planGuard = planGuard; _whatsApp = whatsApp; _alerts = alerts; _activity = activity;
    }

    private async Task EmitStaffChangeAlertAsync(Models.User staff, bool added)
    {
        var biz = await _db.Businesses.FindAsync(BusinessId);
        if (biz == null || !biz.AlertDashboardStaffChanges) return;
        await _alerts.CreateAsync(
            BusinessId, userId: null,
            type: added ? Models.AlertType.StaffAdded : Models.AlertType.StaffRemoved,
            severity: Models.AlertSeverity.Info,
            title: added ? $"{staff.FullName} added to your team" : $"{staff.FullName} removed from your team",
            body: added
                ? $"Role: {staff.Role}. They can now sign in and use the dashboard or WhatsApp bot."
                : "They can no longer access the account.",
            linkUrl: "/settings#team",
            dedupeKey: $"staff-change:{staff.Id}:{(added ? "add" : "remove")}:{DateTime.UtcNow:yyyyMMddHH}");
    }

    [HttpGet]
    [RequirePermission(Permission.ManageStaff)]
    public async Task<ActionResult<ApiResponse<List<StaffDto>>>> GetAll()
    {
        var staff = await _db.Users
            .Where(u => u.BusinessId == BusinessId && u.IsActive)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .Select(u => new StaffDto
            {
                Id = u.Id,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                Email = u.Email,
                Role = u.Role.ToString(),
                IsActive = u.IsActive,
                Permissions = RolePermissions.GetPermissions(u.Role).ToList(),
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync();

        // Attach each staff member's raw location assignments in one round-trip (no N+1). Scoped to this
        // business's locations so a stale cross-business row can never surface.
        var staffIds = staff.Select(s => s.Id).ToList();
        var assignments = await _db.UserLocations
            .Where(ul => staffIds.Contains(ul.UserId) && ul.Location.BusinessId == BusinessId)
            .Select(ul => new { ul.UserId, ul.LocationId })
            .ToListAsync();
        var byUser = assignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Select(a => a.LocationId).ToList());
        foreach (var s in staff)
            if (byUser.TryGetValue(s.Id, out var locs)) s.AssignedLocationIds = locs;

        return Ok(ApiResponse<List<StaffDto>>.Ok(staff));
    }

    /// <summary>
    /// Multi-location: replace a staff member's location assignments (the allow-list of branches/warehouses
    /// they can see and act on). Empty list = unassign → default-location-only by policy. Only restricted
    /// roles can be assigned; Owner/Admin are all-access so assignment is rejected as a no-op. Takes effect on
    /// the user's next request (access is resolved server-side per request, not from their JWT).
    /// </summary>
    [HttpPut("{id:guid}/locations")]
    [RequirePermission(Permission.ManageStaff)]
    public async Task<ActionResult<ApiResponse<object>>> UpdateLocations(Guid id, [FromBody] UpdateStaffLocationsRequest request)
    {
        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.BusinessId == BusinessId && u.IsActive);
        if (staff == null) return NotFound(ApiResponse<object>.Fail("Staff not found."));
        if (staff.Role is UserRole.Owner or UserRole.Admin)
            return BadRequest(ApiResponse<object>.Fail("Owners and admins already have access to every location."));

        var requested = (request.LocationIds ?? new List<Guid>()).Distinct().ToList();

        // Validate every requested location belongs to THIS business and is active — no assigning someone to a
        // foreign or deactivated location.
        if (requested.Count > 0)
        {
            var validIds = await _db.Locations
                .Where(l => l.BusinessId == BusinessId && l.IsActive && requested.Contains(l.Id))
                .Select(l => l.Id)
                .ToListAsync();
            if (validIds.Count != requested.Count)
                return BadRequest(ApiResponse<object>.Fail("One or more locations are invalid or inactive."));
        }

        // Replace the assignment set: drop existing rows, add the new ones.
        var existing = await _db.UserLocations.Where(ul => ul.UserId == staff.Id).ToListAsync();
        _db.UserLocations.RemoveRange(existing);
        foreach (var locId in requested)
            _db.UserLocations.Add(new UserLocation { UserId = staff.Id, LocationId = locId });

        await _activity.LogAsync(BusinessId, "staff.locations_updated", "Staff", staff.Id, staff.FullName,
            requested.Count == 0
                ? $"set {staff.FullName} to the default location only"
                : $"assigned {staff.FullName} to {requested.Count} location(s)");
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(null!, "Location access updated."));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<StaffDto>>> AddStaff([FromBody] AddStaffRequest request)
    {
        // Only Owner and Admin can add staff
        var currentUser = await _db.Users.FindAsync(UserId);
        if (currentUser == null || !RolePermissions.HasPermission(currentUser.Role, Permission.ManageStaff))
            return Unauthorized(ApiResponse<StaffDto>.Fail("You don't have permission to manage staff."));

        var normalizedPhone = WhatsAppService.NormalizePhone(request.PhoneNumber);
        if (string.IsNullOrEmpty(normalizedPhone))
            return BadRequest(ApiResponse<StaffDto>.Fail("Valid phone number required."));

        var normalizedEmail = request.Email?.Trim().ToLowerInvariant();

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role) || role == UserRole.Owner)
            return BadRequest(ApiResponse<StaffDto>.Fail("Invalid role. Use: Admin, Sales, Bookkeeper, or Viewer."));

        // Transaction with serializable isolation to prevent race on staff limit
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var staffLimit = await _planGuard.CheckStaffLimitAsync(BusinessId);
        if (staffLimit != null) return BadRequest(ApiResponse<StaffDto>.Fail(staffLimit));

        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone);
        if (existingUser != null && existingUser.IsActive)
            return BadRequest(ApiResponse<StaffDto>.Fail("Phone number already registered."));

        // If the phone belongs to a deactivated user at a DIFFERENT business, free it up by swapping
        // their phone to a placeholder. The deactivated row stays for audit history — only the phone
        // changes so the unique index no longer blocks reuse. This handles the real-world case where
        // someone leaves one business and joins another.
        if (existingUser != null && !existingUser.IsActive && existingUser.BusinessId != BusinessId)
        {
            existingUser.PhoneNumber = $"x{existingUser.Id.ToString("N")[..18]}";
            await _db.SaveChangesAsync();
            existingUser = null;
        }

        User user;
        if (existingUser != null && !existingUser.IsActive && existingUser.BusinessId == BusinessId)
        {
            // Reactivating a previously-removed staff member. Bump TokenVersion and clear lockout state
            // so any stale JWT or lockout from the prior deactivation doesn't affect the fresh account.
            existingUser.IsActive = true;
            existingUser.FullName = request.FullName.Trim();
            existingUser.Email = normalizedEmail;
            existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            existingUser.Role = role;
            existingUser.MustChangePassword = true;
            existingUser.TokenVersion++;
            existingUser.FailedLoginAttempts = 0;
            existingUser.LockoutEndsAtUtc = null;
            existingUser.PasswordResetCode = null;
            existingUser.PasswordResetCodeExpiresAtUtc = null;
            user = existingUser;
        }
        else
        {
            user = new User
            {
                BusinessId = BusinessId,
                FullName = request.FullName.Trim(),
                PhoneNumber = normalizedPhone,
                Email = normalizedEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = role,
                IsActive = true,
                MustChangePassword = true
            };
            _db.Users.Add(user);
        }
        var business = await _db.Businesses.FindAsync(BusinessId);
        await _activity.LogAsync(BusinessId, "staff.added", "Staff", user.Id, user.FullName,
            $"added {user.Role} “{user.FullName}”");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Send welcome WhatsApp to the new staff member
        _ = Task.Run(async () =>
        {
            try
            {
                await _whatsApp.SendMessageAsync($"whatsapp:{normalizedPhone}",
                    $"👋 Welcome to *Ojunai*!\n\n" +
                    $"You've been added as *{role}* at *{business?.Name ?? "a business"}*.\n\n" +
                    $"*Dashboard login:*\n" +
                    $"🌐 app.ojunai.com\n" +
                    $"📞 Phone: {normalizedPhone}\n" +
                    $"🔑 Your password was set by your manager — ask them for it.\n" +
                    $"⚠️ You'll be asked to change it on first login.\n\n" +
                    $"You can also start using Ojunai right here on WhatsApp! Try:\n" +
                    $"• \"Sold 5 bags of rice at 3000\"\n" +
                    $"• \"Check stock\"\n" +
                    $"• \"Today's sales\"");
            }
            catch { /* best effort */ }
        });

        await EmitStaffChangeAlertAsync(user, added: true);

        return Ok(ApiResponse<StaffDto>.Ok(new StaffDto
        {
            Id = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            Role = user.Role.ToString(),
            IsActive = true,
            Permissions = RolePermissions.GetPermissions(user.Role).ToList(),
            CreatedAtUtc = user.CreatedAtUtc
        }, "Staff member added."));
    }

    /// <summary>
    /// Deactivates a staff member (soft delete). Immediately invalidates their existing JWT tokens
    /// and wipes any pending password reset codes so they can't regain access after being removed.
    /// Owner cannot be removed. Current user cannot deactivate themselves.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Remove(Guid id)
    {
        var currentUser = await _db.Users.FindAsync(UserId);
        if (currentUser == null || !RolePermissions.HasPermission(currentUser.Role, Permission.ManageStaff))
            return Unauthorized(ApiResponse<object>.Fail("You don't have permission to manage staff."));

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.BusinessId == BusinessId);
        if (staff == null) return NotFound(ApiResponse<object>.Fail("Staff not found."));
        if (staff.Role == UserRole.Owner) return BadRequest(ApiResponse<object>.Fail("Cannot remove the business owner."));
        if (staff.Id == UserId) return BadRequest(ApiResponse<object>.Fail("You cannot deactivate yourself."));

        staff.IsActive = false;
        // Invalidate any existing JWTs — the ActiveUserMiddleware will reject requests whose token version no longer matches.
        staff.TokenVersion++;
        // Wipe any pending password reset code so it can't be used to regain access after deactivation.
        staff.PasswordResetCode = null;
        staff.PasswordResetCodeExpiresAtUtc = null;
        await _activity.LogAsync(BusinessId, "staff.removed", "Staff", staff.Id, staff.FullName,
            $"removed {staff.Role} “{staff.FullName}”");
        await _db.SaveChangesAsync();

        await EmitStaffChangeAlertAsync(staff, added: false);

        return Ok(ApiResponse<object>.Ok(null!, $"{staff.FullName} has been deactivated."));
    }

    /// <summary>
    /// Manager-initiated password reset for a staff member. Only works on active staff —
    /// deactivated staff cannot be "reset" as a way to regain access.
    /// Invalidates their existing sessions by bumping TokenVersion.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [RequirePermission(Permission.ManageStaff)]
    public async Task<ActionResult<ApiResponse<object>>> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        // Only active staff can have their password reset. Deactivated staff stay locked out.
        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.BusinessId == BusinessId && u.IsActive);
        if (staff == null) return NotFound(ApiResponse<object>.Fail("Staff not found."));

        // The Owner has their own self-reset path — they can't be reset by anyone else, even
        // a peer admin. Prevents lateral takeover within a business.
        if (staff.Role == UserRole.Owner)
            return BadRequest(ApiResponse<object>.Fail("Owners reset their own password — they cannot be reset by another user."));

        var (pwOk, pwReason) = Common.PasswordPolicy.Validate(request.NewPassword);
        if (!pwOk) return BadRequest(ApiResponse<object>.Fail(pwReason!));

        staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        staff.MustChangePassword = true;
        // Invalidate all existing tokens for this user — they'll need to log in fresh with the new password.
        staff.TokenVersion++;
        await _activity.LogAsync(BusinessId, "staff.password_reset", "Staff", staff.Id, staff.FullName,
            $"reset the password for “{staff.FullName}”");
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(null!, $"Password reset for {staff.FullName}. They will be asked to set a new password on next login."));
    }

    /// <summary>
    /// Changes an active staff member's role. Can't change the owner's role. Can't reactivate a deactivated user
    /// via this endpoint (must use Add Staff flow, which goes through the proper reactivation path).
    /// Bumps TokenVersion so the user's next request re-reads their new role from the database.
    /// </summary>
    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<ApiResponse<StaffDto>>> UpdateRole(Guid id, [FromBody] UpdateRoleRequest request)
    {
        var currentUser = await _db.Users.FindAsync(UserId);
        if (currentUser == null || !RolePermissions.HasPermission(currentUser.Role, Permission.ManageStaff))
            return Unauthorized(ApiResponse<object>.Fail("You don't have permission to manage staff."));

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.BusinessId == BusinessId && u.IsActive);
        if (staff == null) return NotFound(ApiResponse<object>.Fail("Staff not found."));
        if (staff.Role == UserRole.Owner) return BadRequest(ApiResponse<object>.Fail("Cannot change the owner's role."));

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role) || role == UserRole.Owner)
            return BadRequest(ApiResponse<object>.Fail("Invalid role."));

        var oldRole = staff.Role;
        staff.Role = role;
        // Invalidate any existing tokens so the user's next request re-reads their new role.
        staff.TokenVersion++;
        await _activity.LogAsync(BusinessId, "staff.role_changed", "Staff", staff.Id, staff.FullName,
            $"changed {staff.FullName}’s role {oldRole} → {role}");
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<StaffDto>.Ok(new StaffDto
        {
            Id = staff.Id,
            FullName = staff.FullName,
            PhoneNumber = staff.PhoneNumber,
            Email = staff.Email,
            Role = staff.Role.ToString(),
            IsActive = staff.IsActive,
            Permissions = RolePermissions.GetPermissions(staff.Role).ToList(),
            CreatedAtUtc = staff.CreatedAtUtc
        }, "Role updated."));
    }
}

public class UpdateRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
