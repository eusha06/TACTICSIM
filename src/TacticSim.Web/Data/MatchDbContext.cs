using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using TacticSim.Core;

namespace TacticSim.Web.Data;

public sealed class MatchDbContext(DbContextOptions<MatchDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<BrowserSession> Sessions => Set<BrowserSession>();
    public DbSet<SavedMatch> Matches => Set<SavedMatch>();
    public DbSet<SavedUnit> Units => Set<SavedUnit>();
    public DbSet<SavedEvent> Events => Set<SavedEvent>();
    public DbSet<CommandReceipt> Receipts => Set<CommandReceipt>();
    // v2 "Rivergate Command" tables. Additive: v1 tables above are untouched.
    public DbSet<MatchV2> MatchesV2 => Set<MatchV2>();
    public DbSet<TurnOrdersV2> TurnOrdersV2 => Set<TurnOrdersV2>();
    public DbSet<TurnResultV2> TurnResultsV2 => Set<TurnResultV2>();

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
        matches.Property(m => m.Mission).HasConversion<string>().HasMaxLength(24).HasDefaultValue(MissionType.Elimination);
        matches.Property(m => m.HeldRounds).HasDefaultValue(0);
        matches.Property(m => m.LastEvaluatedComputerTurn).HasDefaultValue(0);
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

        var v2 = model.Entity<MatchV2>();
        v2.ToTable("matches_v2");
        v2.HasKey(m => m.Id);
        v2.HasIndex(m => new { m.OwnerId, m.CreatedAt });
        v2.Property(m => m.Version).IsConcurrencyToken();
        v2.Property(m => m.Scenario).HasMaxLength(32);
        v2.Property(m => m.Difficulty).HasMaxLength(16);
        v2.Property(m => m.RulesVersion).HasMaxLength(16);
        v2.Property(m => m.Status).HasMaxLength(16);

        var orders = model.Entity<TurnOrdersV2>();
        orders.ToTable("turn_orders_v2");
        orders.HasKey(o => new { o.MatchId, o.Turn, o.Side });
        orders.Property(o => o.Side).HasMaxLength(8);
        orders.Property(o => o.Orders).HasColumnType("jsonb");
        orders.Property(o => o.Fingerprint).HasMaxLength(64);
        // A command id is unique per owner (Red's AI rows have none; PostgreSQL treats NULLs as distinct).
        orders.HasIndex(o => new { o.OwnerId, o.CommandId }).IsUnique();
        orders.HasOne<MatchV2>().WithMany().HasForeignKey(o => o.MatchId).OnDelete(DeleteBehavior.Restrict);

        var results = model.Entity<TurnResultV2>();
        results.ToTable("turn_results_v2");
        results.HasKey(r => new { r.MatchId, r.Turn });
        results.Property(r => r.Replay).HasColumnType("jsonb");
        results.Property(r => r.EndSnapshot).HasColumnType("jsonb");
        results.HasOne<MatchV2>().WithMany().HasForeignKey(r => r.MatchId).OnDelete(DeleteBehavior.Restrict);

        // The v2 tables use snake_case columns, as in the spec.
        foreach (var entity in new[] { v2.Metadata, orders.Metadata, results.Metadata })
            foreach (var property in entity.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}

public sealed class MatchV2
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Scenario { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string RulesVersion { get; set; } = "";
    public int AiSeed { get; set; }
    /// <summary>The turn being planned (the engine's MatchState.Turn).</summary>
    public int Turn { get; set; }
    /// <summary>active, blueWins, redWins or draw.</summary>
    public string Status { get; set; } = "active";
    public int VpBlue { get; set; }
    public int VpRed { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TurnOrdersV2
{
    public Guid MatchId { get; set; }
    public int Turn { get; set; }
    public string Side { get; set; } = "";
    public Guid OwnerId { get; set; }
    public Guid? CommandId { get; set; }
    public string? Fingerprint { get; set; }
    public string Orders { get; set; } = "[]";
    public DateTimeOffset SubmittedAt { get; set; }
}

public sealed class TurnResultV2
{
    public Guid MatchId { get; set; }
    public int Turn { get; set; }
    /// <summary>The full, unfiltered tick log. Always fog-filtered before it is sent to a player.</summary>
    public string Replay { get; set; } = "{}";
    public string EndSnapshot { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
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
    public MissionType Mission { get; set; } = MissionType.Elimination;
    public int? ObjectiveX { get; set; }
    public int? ObjectiveY { get; set; }
    public int HeldRounds { get; set; }
    public int LastEvaluatedComputerTurn { get; set; }
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
    public int? ObjectiveProgress { get; set; }
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
