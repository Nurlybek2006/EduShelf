using EduShelf.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceTag> ResourceTags => Set<ResourceTag>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ResourceView> ResourceViews => Set<ResourceView>();
    public DbSet<ResourceDownload> ResourceDownloads => Set<ResourceDownload>();
    public DbSet<ResourceAnalysis> ResourceAnalyses => Set<ResourceAnalysis>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Subject>().HasIndex(x => x.Name).IsUnique();
        b.Entity<ResourceType>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Tag>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Topic>().HasIndex(x => new { x.SubjectId, x.Name }).IsUnique();
        b.Entity<Topic>().HasAlternateKey(x => new { x.Id, x.SubjectId });
        b.Entity<Topic>().HasOne(x => x.Subject).WithMany(x => x.Topics).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        var r = b.Entity<Resource>();
        r.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne(x => x.Topic).WithMany().HasForeignKey(x => new { x.TopicId, x.SubjectId }).HasPrincipalKey(x => new { x.Id, x.SubjectId }).OnDelete(DeleteBehavior.Restrict);
        r.HasOne(x => x.ResourceType).WithMany().HasForeignKey(x => x.ResourceTypeId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        r.Property(x => x.AccessLevel).HasConversion<string>().HasMaxLength(20);
        r.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        r.HasIndex(x => new { x.Status, x.AccessLevel, x.CreatedAtUtc });
        r.HasIndex(x => new { x.OwnerId, x.Status });
        r.HasIndex(x => x.StoredFileName).IsUnique();
        r.HasIndex(x => x.FileHash);
        r.ToTable("Resources", t => { t.HasCheckConstraint("CK_Resources_FileSize", "[FileSize] > 0"); t.HasCheckConstraint("CK_Resources_Status", "[Status] IN ('Draft','Published','Archived')"); t.HasCheckConstraint("CK_Resources_AccessLevel", "[AccessLevel] IN ('Public','Authenticated')"); });
        b.Entity<ResourceTag>().HasKey(x => new { x.ResourceId, x.TagId });
        b.Entity<ResourceTag>().HasOne(x => x.Resource).WithMany(x => x.ResourceTags).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ResourceTag>().HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Favorite>().HasKey(x => new { x.UserId, x.ResourceId });
        b.Entity<Favorite>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Favorite>().HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ResourceView>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ResourceView>().HasOne(x => x.Resource).WithMany(x => x.Views).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ResourceView>().HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        b.Entity<ResourceDownload>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ResourceDownload>().HasOne(x => x.Resource).WithMany(x => x.Downloads).HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ResourceDownload>().HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        b.Entity<ResourceAnalysis>().HasKey(x => x.ResourceId);
        b.Entity<ResourceAnalysis>().HasOne(x => x.Resource).WithOne(x => x.Analysis).HasForeignKey<ResourceAnalysis>(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ResourceAnalysis>().Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}
