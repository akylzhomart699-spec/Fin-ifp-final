using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Color).HasMaxLength(9);
            e.Property(x => x.Keywords).HasMaxLength(1000);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(300);
            e.HasIndex(x => x.Date);
            e.HasOne(x => x.Account).WithMany(a => a.Transactions)
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: a category cannot be deleted while it still has transactions
            e.HasOne(x => x.Category).WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Budget>(e =>
        {
            e.HasIndex(x => new { x.CategoryId, x.Year, x.Month }).IsUnique();
            e.HasOne(x => x.Category).WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Entity>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = DateTime.UtcNow;

        return base.SaveChangesAsync(cancellationToken);
    }
}
