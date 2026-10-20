using System.Data;
using EduShelf.Data;
using EduShelf.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Services;

public sealed record RoleChangeResult(bool Succeeded, string? Error = null);

/// <summary>Changes roles atomically and serializes changes across every application instance.</summary>
public sealed class UserAdministrationService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
{
    public static bool IsAllowedRole(string? role) => role is AppRoles.Student or AppRoles.Teacher or AppRoles.Admin;

    public static bool WouldRemoveLastAdministrator(bool isAdministrator, string newRole, int administratorCount)
        => isAdministrator && newRole != AppRoles.Admin && administratorCount <= 1;

    public async Task<RoleChangeResult> ChangeRoleAsync(string actorId, string userId, string role, CancellationToken cancellationToken = default)
    {
        if (!IsAllowedRole(role))
            return new(false, "Рөл дұрыс таңдалмаған.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            // Transaction-owned SQL application locks also protect against concurrent requests on other servers.
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock @Resource = N'EduShelf.RoleAdministration',
                    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
                IF @lockResult < 0 THROW 51000, 'Role administration lock unavailable.', 1;
                """, cancellationToken);

            var administratorRoleId = await db.Roles.Where(x => x.NormalizedName == "ADMIN")
                .Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
            if (administratorRoleId is null || !await db.UserRoles.AnyAsync(x => x.UserId == actorId && x.RoleId == administratorRoleId, cancellationToken))
                return new RoleChangeResult(false, "Бұл әрекетті орындау құқығыңыз жоқ. Қайта кіріңіз.");

            if (!await db.Roles.AnyAsync(x => x.NormalizedName == role.ToUpperInvariant(), cancellationToken))
                return new RoleChangeResult(false, "Таңдалған рөл жүйеде дайындалмаған. Әкімшіге хабарласыңыз.");

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return new RoleChangeResult(false, "Қолданушы табылмады.");

            var currentRoles = await userManager.GetRolesAsync(user);
            var administratorCount = await db.UserRoles.CountAsync(x => x.RoleId == administratorRoleId, cancellationToken);
            if (WouldRemoveLastAdministrator(currentRoles.Contains(AppRoles.Admin), role, administratorCount))
                return new RoleChangeResult(false, "Соңғы әкімшінің рөлін өзгертуге болмайды. Алдымен басқа әкімші тағайындаңыз.");

            if (currentRoles.Count == 1 && currentRoles[0] == role)
                return new RoleChangeResult(true);

            if (currentRoles.Count > 0 && !(await userManager.RemoveFromRolesAsync(user, currentRoles)).Succeeded)
                return new RoleChangeResult(false, "Рөлді өзгерту мүмкін болмады. Бетті жаңартып, қайталаңыз.");

            if (!(await userManager.AddToRoleAsync(user, role)).Succeeded || !(await userManager.UpdateSecurityStampAsync(user)).Succeeded)
                return new RoleChangeResult(false, "Рөлді сақтау мүмкін болмады. Қайталап көріңіз.");

            await transaction.CommitAsync(cancellationToken);
            return new RoleChangeResult(true);
        });
    }
}
