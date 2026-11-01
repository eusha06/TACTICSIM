using Microsoft.EntityFrameworkCore;
using TacticSim.Core;

namespace TacticSim.Web.Data;

public sealed class MatchDbContext(DbContextOptions<MatchDbContext> options) : DbContext(options)
{
    public DbSet<BrowserSession> Sessions => Set<BrowserSession>();
    public DbSet<SavedMatch> Matches => Set<SavedMatch>();
    public DbSet<SavedUnit> Units => Set<SavedUnit>();
    public DbSet<SavedEvent> Events => Set<SavedEvent>();
    public DbSet<CommandReceipt> Receipts => Set<CommandReceipt>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var sessions = model.Entity<BrowserSession>();
        sessions.ToTable("GameSessions");
        sessions.HasKey(s => s.OwnerId);
        sessions.Property(s => s.Revision).IsConcurrencyToken();

        var matches = model.Entity<SavedMatch>();
        matches.ToTable("GameMatches");
        matches.HasKey(m => m.Id);
        matches.HasAlternateKey(m => new { m.OwnerId, m.Id });
        matches.HasIndex(m => new { m.OwnerId, m.CreatedAt });
        matches.Property(m => m.Version).IsConcurrencyToken();
        matches.Property(m => m.MapKey).HasMaxLength(32);
        matches.Property(m => m.CurrentSide).HasConversion<string>().HasMaxLength(16);
        matches.Property(m => m.Winner).HasConversion<string>().HasMaxLength(16);
        matches.HasOne<BrowserSession>().WithMany().HasForeignKey(m => m.OwnerId).OnDelete(DeleteBehavior.Restrict);
        matches.HasMany(m => m.Units).WithOne().HasForeignKey(u => u.MatchId);
        matches.HasMany(m => m.Events).WithOne().HasForeignKey(e => e.MatchId);

        var units = model.Entity<SavedUnit>();
        units.ToTable("MatchUnits");
        units.HasKey(u => new { u.MatchId, u.UnitId });
        units.Property(u => u.Type).HasMaxLength(16);
        units.Property(u => u.Side).HasConversion<string>().HasMaxLength(16);

        var commands = model.Entity<CommandReceipt>();
        commands.ToTable("MatchCommands");
        commands.HasKey(r => new { r.OwnerId, r.CommandId });
        commands.HasAlternateKey(r => new { r.MatchId, r.CommandId });
        commands.Property(r => r.Kind).HasMaxLength(24);
        commands.Property(r => r.Fingerprint).HasMaxLength(64);
        commands.HasOne<SavedMatch>().WithMany().HasForeignKey(r => new { r.OwnerId, r.MatchId })
            .HasPrincipalKey(m => new { m.OwnerId, m.Id }).OnDelete(DeleteBehavior.Restrict);

        var events = model.Entity<SavedEvent>();
        events.ToTable("MatchEvents");
        events.HasKey(e => new { e.MatchId, e.Sequence });
        events.Property(e => e.Side).HasConversion<string>().HasMaxLength(16);
        events.Property(e => e.Kind).HasMaxLength(24);
        // Imported old text events have no recoverable command link. New events always do.
        events.HasOne<CommandReceipt>().WithMany().HasForeignKey(e => new { e.MatchId, e.CommandId })
            .HasPrincipalKey(r => new { r.MatchId, r.CommandId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BrowserSession
{
    public Guid OwnerId { get; set; }
    public Guid? ActiveMatchId { get; set; }
    // Protects the active-match selection across New Match, Resume, and multiple tabs.
    public long Revision { get; set; }
}

public sealed class SavedMatch
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public long Version { get; set; }
    public string MapKey { get; set; } = "crossing-v1";
    public Side CurrentSide { get; set; }
    public int TurnNumber { get; set; }
    public Side? Winner { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long LastEventSequence { get; set; }
    public List<SavedUnit> Units { get; set; } = [];
    public List<SavedEvent> Events { get; set; } = [];
}

public sealed class SavedUnit
{
    public Guid MatchId { get; set; }
    public int UnitId { get; set; }
    public string Type { get; set; } = "";
    public Side Side { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Health { get; set; }
    public bool HasMoved { get; set; }
    public bool HasAttacked { get; set; }
}

public sealed class SavedEvent
{
    public Guid MatchId { get; set; }
    public long Sequence { get; set; }
    public Guid? CommandId { get; set; }
    public int Turn { get; set; }
    public Side Side { get; set; }
    public string Kind { get; set; } = "";
    public int? UnitId { get; set; }
    public int? TargetId { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Damage { get; set; }
    public bool Destroyed { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class CommandReceipt
{
    public Guid OwnerId { get; set; }
    public Guid CommandId { get; set; }
    public Guid MatchId { get; set; }
    public string Kind { get; set; } = "";
    public int? UnitId { get; set; }
    public int? TargetId { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public Guid? TargetMatchId { get; set; }
    public long? ExpectedMatchVersion { get; set; }
    public long ResultingMatchVersion { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public string Fingerprint { get; set; } = "";
    // An HTTP retry cache only. Game state is read from matches, units and events.
    public string ResponseJson { get; set; } = "";
}
