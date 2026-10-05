using Microsoft.EntityFrameworkCore;
namespace Aquarella.Data;
public sealed class AquarellaDbContext(DbContextOptions<AquarellaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<AccountLoginSession> AccountLoginSessions => Set<AccountLoginSession>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CalendarEntry> CalendarEntries => Set<CalendarEntry>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(u => u.Username).IsUnique();
        model.Entity<User>().HasIndex(u => u.NormalizedEmail).IsUnique();
        model.Entity<User>().Property(u => u.Email).HasMaxLength(254);
        model.Entity<User>().Property(u => u.NormalizedEmail).HasMaxLength(254);
        model.Entity<User>().Property(u => u.FirstName).HasMaxLength(100);
        model.Entity<User>().Property(u => u.LastName).HasMaxLength(100);
        model.Entity<AccountToken>().HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AccountLoginSession>().HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<User>().Property(u => u.Username).HasMaxLength(120).IsRequired();
        model.Entity<Business>().HasOne(b => b.User).WithOne(u => u.Business).HasForeignKey<Business>(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Business>().OwnsOne(b => b.Profile, p => {
            p.Property(x => x.Name).HasMaxLength(120).IsRequired();
            p.Property(x => x.Description).HasMaxLength(1000);
            p.Property(x => x.LogoDataUrl).HasMaxLength(3_000_000);
            p.Property(x => x.PrimaryColor).HasMaxLength(7).IsRequired();
            p.Property(x => x.SecondaryColor).HasMaxLength(7).IsRequired();
            p.Property(x => x.TertiaryColor).HasMaxLength(7).IsRequired();
        });
        model.Entity<Business>().Navigation(b => b.Profile).IsRequired();
        model.Entity<Product>().HasOne(p => p.Business).WithMany().HasForeignKey(p => p.BusinessId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Product>().Property(p => p.Name).HasMaxLength(120).IsRequired();
        model.Entity<Product>().Property(p => p.Cost).HasPrecision(18, 2);
        model.Entity<Product>().Property(p => p.SalePrice).HasPrecision(18, 2);
        model.Entity<Product>().Property(p => p.DesiredProfitPercent).HasPrecision(8, 2);
        model.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Product_Quantity", "Quantity >= 0"));
        model.Entity<CalendarEntry>().HasOne(n => n.Business).WithMany().HasForeignKey(n => n.BusinessId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<CalendarEntry>().HasIndex(n => new { n.BusinessId, n.Date });
        model.Entity<CalendarEntry>().Property(n => n.Title).HasMaxLength(120).IsRequired();
        model.Entity<CalendarEntry>().Property(n => n.Content).HasMaxLength(4000).IsRequired();
        model.Entity<CalendarEntry>().Property(n => n.Time).HasMaxLength(5);
    }
}
