using System.Linq.Expressions;
using System.Security.Claims;
using EduShelf.Models;

namespace EduShelf.Services;

public static class ResourceAccess
{
    public static string? UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);
    public static bool CanManage(ClaimsPrincipal user, Resource resource) => user.IsInRole(AppRoles.Admin) ||
        (user.IsInRole(AppRoles.Teacher) && resource.OwnerId == UserId(user));
    public static bool CanRead(ClaimsPrincipal user, Resource resource) => user.IsInRole(AppRoles.Admin) ||
        (user.Identity?.IsAuthenticated == true && resource.OwnerId == UserId(user)) ||
        (resource.Status == ResourceStatus.Published && (resource.AccessLevel == AccessLevel.Public || user.Identity?.IsAuthenticated == true));
    public static IQueryable<Resource> VisibleTo(this IQueryable<Resource> resources, ClaimsPrincipal user, bool catalogOnly = false)
    {
        var signedIn = user.Identity?.IsAuthenticated == true;
        var userId = UserId(user);
        if (catalogOnly) return resources.Where(r => r.Status == ResourceStatus.Published && (r.AccessLevel == AccessLevel.Public || signedIn));
        if (user.IsInRole(AppRoles.Admin)) return resources;
        return resources.Where(r => (signedIn && r.OwnerId == userId) ||
            (r.Status == ResourceStatus.Published && (r.AccessLevel == AccessLevel.Public || signedIn)));
    }
}
