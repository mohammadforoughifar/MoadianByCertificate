using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Api.Infrastructure;
using MoudianMiddleware.Api.Services;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.Api.Controllers;

public sealed record UserCreateRequest(string UserName, string FullName, string Password, int RoleId);
public sealed record UserView(int Id, string UserName, string FullName, bool IsActive, string RoleName);
public sealed record RoleCreateRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
public sealed record RoleView(int Id, string Name, IReadOnlyList<string> Permissions);

[Authorize, Route("api/users")]
public sealed class UsersController(MoudianDbContext db, ITenantProvider tenant, IPasswordHasher passwords) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Permissions.UserView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserView>>>> ListAsync(CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var users = await db.UserTenants.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .Select(x => new UserView(x.UserId, x.User!.UserName, x.User.FullName, x.User.IsActive, x.Role!.Name)).ToListAsync(ct).ConfigureAwait(false);
        return Ok(Success<IReadOnlyList<UserView>>(users));
    }
    [HttpPost, Authorize(Policy = Permissions.UserEdit)]
    public async Task<ActionResult<ApiResponse<UserView>>> CreateAsync(UserCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12 || string.IsNullOrWhiteSpace(request.FullName)) throw new FluentValidation.ValidationException("Username, full name, and a 12-character password are required.");
        var normalized = request.UserName.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(x => x.NormalizedUserName == normalized, ct).ConfigureAwait(false)) throw new MoudianMiddleware.CoreLayer.Common.Exceptions.BusinessConflictException("Username already exists.");
        var role = await db.Roles.Include(x => x.Permissions).SingleOrDefaultAsync(x => x.Id == request.RoleId, ct).ConfigureAwait(false) ?? throw new MoudianMiddleware.CoreLayer.Common.Exceptions.EntityNotFoundException(nameof(Role), request.RoleId);
        var user = new User { UserName = request.UserName.Trim(), NormalizedUserName = normalized, FullName = request.FullName.Trim(), PasswordHash = passwords.Hash(request.Password) };
        user.UserTenants.Add(new UserTenant { TenantId = RequireTenant(), RoleId = role.Id, Role = role, IsActive = true });
        db.Users.Add(user); await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return StatusCode(201, Success(new UserView(user.Id, user.UserName, user.FullName, user.IsActive, role.Name), ApiResponseCodes.Created));
    }
    private int RequireTenant() => tenant.TenantId is > 0 ? tenant.TenantId.Value : throw new UnauthorizedAccessException();
}

[Authorize(Policy = Permissions.RoleManage), Route("api/roles")]
public sealed class RolesController(MoudianDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleView>>>> ListAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Include(x => x.Permissions).OrderBy(x => x.Name)
            .Select(x => new RoleView(x.Id, x.Name, x.Permissions.Select(permission => permission.Permission).ToArray())).ToListAsync(ct).ConfigureAwait(false);
        return Ok(Success<IReadOnlyList<RoleView>>(roles));
    }
    [HttpPost]
    public async Task<ActionResult<ApiResponse<RoleView>>> CreateAsync(RoleCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Permissions is null) throw new FluentValidation.ValidationException("A role must provide a permission list.");
        var allowed = Permissions.All.ToHashSet(StringComparer.Ordinal);
        var requested = request.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        if (string.IsNullOrWhiteSpace(request.Name) || requested.Any(permission => !allowed.Contains(permission))) throw new FluentValidation.ValidationException("Role name or permission code is invalid.");
        var normalized = request.Name.Trim().ToUpperInvariant();
        if (await db.Roles.AnyAsync(x => x.NormalizedName == normalized, ct).ConfigureAwait(false)) throw new MoudianMiddleware.CoreLayer.Common.Exceptions.BusinessConflictException("Role already exists.");
        var role = new Role { Name = request.Name.Trim(), NormalizedName = normalized, Description = request.Description?.Trim() };
        foreach (var permission in requested) role.Permissions.Add(new RolePermission { Permission = permission });
        db.Roles.Add(role); await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return StatusCode(201, Success(new RoleView(role.Id, role.Name, requested), ApiResponseCodes.Created));
    }
}
