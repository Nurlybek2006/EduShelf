using System.Security.Claims;
using EduShelf.Models;
using EduShelf.Services;

namespace EduShelf.Tests;

public class ResourceAccessTests
{
    internal static ClaimsPrincipal Principal(string? userId = null, string? role = null)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, userId is null ? null : "Test"));
    }

    public static TheoryData<string?, string?, ResourceStatus, AccessLevel, bool> ReadCases => new()
    {
        { null, null, ResourceStatus.Published, AccessLevel.Public, true },
        { null, null, ResourceStatus.Published, AccessLevel.Authenticated, false },
        { null, null, ResourceStatus.Draft, AccessLevel.Public, false },
        { null, null, ResourceStatus.Archived, AccessLevel.Public, false },
        { "student", AppRoles.Student, ResourceStatus.Published, AccessLevel.Authenticated, true },
        { "student", AppRoles.Student, ResourceStatus.Draft, AccessLevel.Public, false },
        { "other", AppRoles.Teacher, ResourceStatus.Draft, AccessLevel.Public, false },
        { "owner", AppRoles.Teacher, ResourceStatus.Draft, AccessLevel.Authenticated, true },
        { "owner", AppRoles.Teacher, ResourceStatus.Archived, AccessLevel.Authenticated, true },
        { "admin", AppRoles.Admin, ResourceStatus.Archived, AccessLevel.Authenticated, true },
    };

    [Theory]
    [MemberData(nameof(ReadCases))]
    public void Read_permission_and_visibility_query_agree(string? id, string? role, ResourceStatus status, AccessLevel access, bool expected)
    {
        var resource = new Resource { Id = 42, OwnerId = "owner", Status = status, AccessLevel = access };
        var user = Principal(id, role);
        Assert.Equal(expected, ResourceAccess.CanRead(user, resource));
        Assert.Equal(expected, new[] { resource }.AsQueryable().VisibleTo(user).Any());
    }

    [Fact]
    public void Anonymous_catalog_hides_private_metadata_and_unpublished_resources()
    {
        var resources = new[]
        {
            new Resource { Id = 1, Title = "Public", Status = ResourceStatus.Published, AccessLevel = AccessLevel.Public },
            new Resource { Id = 2, Title = "Private search phrase", Status = ResourceStatus.Published, AccessLevel = AccessLevel.Authenticated },
            new Resource { Id = 3, Title = "Draft search phrase", Status = ResourceStatus.Draft, AccessLevel = AccessLevel.Public },
            new Resource { Id = 4, Title = "Archived search phrase", Status = ResourceStatus.Archived, AccessLevel = AccessLevel.Public }
        }.AsQueryable();
        Assert.Equal([1], resources.VisibleTo(Principal(), catalogOnly: true).Select(x => x.Id).ToArray());
        Assert.Empty(resources.VisibleTo(Principal(), catalogOnly: true).Where(x => x.Title.Contains("search phrase")));
        Assert.Equal([1, 2], resources.VisibleTo(Principal("admin", AppRoles.Admin), catalogOnly: true).Select(x => x.Id).ToArray());
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("owner", AppRoles.Student, false)]
    [InlineData("other", AppRoles.Teacher, false)]
    [InlineData("owner", AppRoles.Teacher, true)]
    [InlineData("admin", AppRoles.Admin, true)]
    public void Only_owner_teacher_or_admin_can_manage(string? id, string? role, bool expected)
        => Assert.Equal(expected, ResourceAccess.CanManage(Principal(id, role), new Resource { OwnerId = "owner" }));

    [Theory]
    [InlineData(true, AppRoles.Student, 1, true)]
    [InlineData(true, AppRoles.Teacher, 1, true)]
    [InlineData(true, AppRoles.Admin, 1, false)]
    [InlineData(true, AppRoles.Teacher, 2, false)]
    [InlineData(false, AppRoles.Student, 1, false)]
    public void Last_administrator_is_protected(bool isAdmin, string newRole, int adminCount, bool blocked)
        => Assert.Equal(blocked, UserAdministrationService.WouldRemoveLastAdministrator(isAdmin, newRole, adminCount));

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_role_cannot_be_assigned(string? role) => Assert.False(UserAdministrationService.IsAllowedRole(role));
}
