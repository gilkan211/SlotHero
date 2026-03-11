using Microsoft.EntityFrameworkCore;
using SlotHero.Core.Models;

namespace SlotHero.Core;

/// <summary>
/// Central data access context for the SlotHero platform.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Registered businesses managing calendar appointments.
    /// </summary>
    public DbSet<Business> Businesses { get; set; } = null!;

    /// <summary>
    /// Clients waiting for available calendar slots.
    /// </summary>
    public DbSet<WaitlistEntry> WaitlistEntries { get; set; } = null!;

    /// <summary>
    /// Freed calendar slots offered to waitlisted clients.
    /// </summary>
    public DbSet<SlotAuction> SlotAuctions { get; set; } = null!;

    /// <summary>
    /// Operating windows that define when a business accepts appointments.
    /// </summary>
    public DbSet<BusinessHour> BusinessHours { get; set; } = null!;

    public AppDbContext() { }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Local SQLite fallback so EF migrations can run without an injected provider
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=slotHero.db");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Prevent duplicate business registrations from the same Google account
        modelBuilder.Entity<Business>()
            .HasIndex(b => b.GoogleId)
            .IsUnique();

        // Prevent duplicate waitlist sign-ups with the same phone number
        modelBuilder.Entity<WaitlistEntry>()
            .HasIndex(w => w.ClientPhone)
            .IsUnique();

        modelBuilder.Entity<BusinessHour>()
            .HasOne(bh => bh.Business)
            .WithMany(b => b.BusinessHours)
            .HasForeignKey(bh => bh.BusinessId)
            .IsRequired();
    }
}
