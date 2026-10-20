using System.Data;
using System.Security.Claims;
using EduShelf.Data;
using EduShelf.Models;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Services;

public class LibraryService(ApplicationDbContext db)
{
    public async Task<bool> ToggleFavoriteAsync(int resourceId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = ResourceAccess.UserId(user);
        if (user.Identity?.IsAuthenticated != true || userId is null) throw new UnauthorizedAccessException();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var lockName = $"EduShelf:favorite:{userId}:{resourceId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sp_getapplock @Resource={lockName}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 50001, 'Favorite lock unavailable', 1;", ct);
        if (!await db.Resources.AsNoTracking().VisibleTo(user).AnyAsync(r => r.Id == resourceId, ct)) throw new KeyNotFoundException();
        var existing = await db.Favorites.FindAsync([userId, resourceId], ct);
        var added = existing is null;
        if (existing is null) db.Favorites.Add(new Favorite { UserId = userId, ResourceId = resourceId });
        else db.Favorites.Remove(existing);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return added;
    }
}
