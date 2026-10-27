using Microsoft.EntityFrameworkCore;

using ThriveLink.Domain;

namespace ThriveLink.Data;

public class ThriveLinkDbContext(DbContextOptions<ThriveLinkDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<LinkedDevice> LinkedDevices => Set<LinkedDevice>();
    public DbSet<ActivityDay> ActivityDays => Set<ActivityDay>();
    public DbSet<PointsEntry> PointsEntries => Set<PointsEntry>();
    public DbSet<WeeklyReward> WeeklyRewards => Set<WeeklyReward>();
    public DbSet<HealthCheckResult> HealthCheckResults => Set<HealthCheckResult>();
    public DbSet<HealthCheckReject> HealthCheckRejects => Set<HealthCheckReject>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();

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

        b.Entity<WeeklyReward>(e =>
        {
            e.HasIndex(w => new { w.MemberId, w.WeekStart }).IsUnique();
            e.HasIndex(w => w.WeekStart);
            e.HasOne<Member>().WithMany().HasForeignKey(w => w.MemberId);
        });

        b.Entity<HealthCheckResult>(e =>
        {
            e.Property(h => h.Measure).HasMaxLength(40);
            e.Property(h => h.Partner).HasMaxLength(100);
            e.HasIndex(h => new { h.MemberId, h.CheckDate, h.Measure }).IsUnique();
            e.HasOne<Member>().WithMany().HasForeignKey(h => h.MemberId);
        });

        b.Entity<HealthCheckReject>(e =>
        {
            e.Property(r => r.MemberNumber).HasMaxLength(50);
            e.Property(r => r.CheckDate).HasMaxLength(50);
            e.Property(r => r.Measure).HasMaxLength(50);
            e.Property(r => r.Partner).HasMaxLength(100);
            e.Property(r => r.Reason).HasMaxLength(200);
        });

        b.Entity<Claim>(e =>
        {
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.HasIndex(c => new { c.Status, c.SubmittedAt });
            e.Property(c => c.ClaimNumber).HasMaxLength(20);
            e.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Recommendation).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.ProviderName).HasMaxLength(150);
            e.Property(c => c.Description).HasMaxLength(2000);
            e.Property(c => c.Amount).HasPrecision(10, 2);
            e.Property(c => c.RuleFlags).HasMaxLength(1000);
            e.Property(c => c.AiSummary).HasMaxLength(2000);
            e.Property(c => c.AiModel).HasMaxLength(50);
            e.Property(c => c.DecidedBy).HasMaxLength(100);
            e.Property(c => c.DecisionNote).HasMaxLength(1000);
            e.HasOne<Member>().WithMany().HasForeignKey(c => c.MemberId);
            e.HasMany(c => c.Documents).WithOne().HasForeignKey(d => d.ClaimId);
        });

        b.Entity<ClaimDocument>(e =>
        {
            e.Property(d => d.BlobName).HasMaxLength(300);
            e.Property(d => d.FileName).HasMaxLength(200);
            e.Property(d => d.ContentType).HasMaxLength(100);
        });
    }
}
