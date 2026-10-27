using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

public class ThriveLinkDbContext(DbContextOptions<ThriveLinkDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<LinkedDevice> LinkedDevices => Set<LinkedDevice>();
    public DbSet<ActivityDay> ActivityDays => Set<ActivityDay>();
    public DbSet<PointsEntry> PointsEntries => Set<PointsEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.Entity<Member>(e =>
        {
            e.HasIndex(m => m.MemberNumber).IsUnique();
            e.Property(m => m.MemberNumber).HasMaxLength(20);
            e.Property(m => m.PolicyNumber).HasMaxLength(20);
            e.Property(m => m.FirstName).HasMaxLength(100);
            e.Property(m => m.LastName).HasMaxLength(100);
        });

        b.Entity<LinkedDevice>(e =>
        {
            e.Property(d => d.Provider).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(d => new { d.MemberId, d.Provider });
        });

        b.Entity<ActivityDay>(e =>
        {
            e.Property(a => a.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.FlagReason).HasMaxLength(200);
            e.HasIndex(a => new { a.MemberId, a.Date }).IsUnique();
            e.HasIndex(a => a.Date);
        });

        b.Entity<PointsEntry>(e =>
        {
            e.Property(p => p.Category).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(p => new { p.MemberId, p.Date, p.Category }).IsUnique();
            e.HasIndex(p => p.Date);
        });
    }
}
