using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public required DbSet<LogEntryEntity> Logs { get; set; }
    public required DbSet<LogMetadataEntity> LogMetadata { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var log = modelBuilder.Entity<LogEntryEntity>();

        log.HasKey(x => x.Id);

        log.Property(x => x.Timestamp).IsRequired();
        log.Property(x => x.Level).HasMaxLength(50);
        log.Property(x => x.DeviceId).HasMaxLength(100);
        log.Property(x => x.Source).HasMaxLength(100);

        log.Property(x => x.Message).IsRequired();
        log.Property(x => x.RawLine).IsRequired();

        // Indexes (IMPORTANT)
        log.HasIndex(x => x.Timestamp);
        log.HasIndex(x => x.Level);
        log.HasIndex(x => x.DeviceId);

        // Relationship
        log.HasMany(x => x.Metadata)
           .WithOne(x => x.LogEntry)
           .HasForeignKey(x => x.LogEntryId)
           .OnDelete(DeleteBehavior.Cascade);

        var meta = modelBuilder.Entity<LogMetadataEntity>();

        meta.HasKey(x => x.Id);

        meta.Property(x => x.Key).IsRequired();
        meta.Property(x => x.Value).IsRequired();
    }
}