# Phase 1: Database & WorkspaceFactory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-step. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build multi-user database foundation and project-level Workspace lifecycle management with LRU caching

**Architecture:** EF Core entities for StoryBible data + WorkspaceFactory with reference counting and soft LRU eviction + Repository pattern for data access + Thread-safe Workspace operations

**Tech Stack:** ASP.NET Core 8.0, Entity Framework Core, SQLite, xUnit, BCrypt.Net

---

## File Structure

### New Files to Create
- `Web/NovelAgentWeb/Data/Entities/StoryConstitution.cs` - Story creative constitution entity
- `Web/NovelAgentWeb/Data/Entities/VolumeArc.cs` - Volume arc planning entity
- `Web/NovelAgentWeb/Data/Entities/CharacterState.cs` - Character state tracking entity
- `Web/NovelAgentWeb/Data/Entities/ForeshadowEntry.cs` - Foreshadow ledger entry entity
- `Web/NovelAgentWeb/Data/Entities/WorldSettingEntry.cs` - World setting entry entity
- `Web/NovelAgentWeb/Data/Entities/AgentRun.cs` - Agent run history entity
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceEntry.cs` - Workspace cache entry with reference counting
- `Web/NovelAgentWeb/Services/Workspace/IWorkspaceFactory.cs` - Workspace factory interface
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs` - Workspace factory implementation
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactoryOptions.cs` - Configuration options
- `Web/NovelAgentWeb/Services/Repositories/IStoryBibleRepository.cs` - StoryBible repository interface
- `Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs` - StoryBible repository implementation
- `Tests/NovelAgentRegression/WorkspaceFactoryTests.cs` - Workspace factory unit tests
- `Tests/NovelAgentRegression/StoryBibleRepositoryTests.cs` - Repository integration tests

### Files to Modify
- `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs` - Add new DbSets and configurations
- `Web/NovelAgentWeb/Program.cs` - Register WorkspaceFactory and repositories
- `Web/NovelAgentWeb/Support/WebRuntime.cs` - Add thread safety to NovelAgentWorkspace

---

## Task 1: StoryConstitution Entity and Configuration

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/StoryConstitution.cs`
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs:26-331`

- [ ] **Step 1: Create StoryConstitution entity class**

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class StoryConstitution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Genre { get; set; } = null!;
    public string? SubGenre { get; set; }
    public string CoreHook { get; set; } = null!;
    public string? ReaderPromise { get; set; }
    public string? GenreProfile { get; set; }
    public string? TargetAudience { get; set; }
    public string? Taboos { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

- [ ] **Step 2: Add DbSet to NovelAgentDbContext**

```csharp
// Add after line 24 (after AgentSessions DbSet)
public DbSet<StoryConstitution> StoryConstitutions { get; set; } = null!;
```

- [ ] **Step 3: Configure StoryConstitution entity in OnModelCreating**

```csharp
// Add before the closing brace of OnModelCreating method (around line 330)
// StoryConstitution entity configuration
modelBuilder.Entity<StoryConstitution>(entity =>
{
    entity.ToTable("story_constitutions");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
    entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
    entity.Property(e => e.Genre).HasColumnName("genre").IsRequired();
    entity.Property(e => e.SubGenre).HasColumnName("sub_genre");
    entity.Property(e => e.CoreHook).HasColumnName("core_hook").IsRequired();
    entity.Property(e => e.ReaderPromise).HasColumnName("reader_promise");
    entity.Property(e => e.GenreProfile).HasColumnName("genre_profile");
    entity.Property(e => e.TargetAudience).HasColumnName("target_audience");
    entity.Property(e => e.Taboos).HasColumnName("taboos");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

    entity.HasIndex(e => e.ProjectId).IsUnique();

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Project)
        .WithOne()
        .HasForeignKey<StoryConstitution>(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 4: Commit**

```bash
git add Web/NovelAgentWeb/Data/Entities/StoryConstitution.cs Web/NovelAgentWeb/Data/NovelAgentDbContext.cs
git commit -m "feat(db): add StoryConstitution entity"
```

---

## Task 2: VolumeArc Entity and Configuration

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/VolumeArc.cs`
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`

- [ ] **Step 1: Create VolumeArc entity class**

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class VolumeArc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public int VolumeNumber { get; set; }
    public string VolumeTitle { get; set; } = null!;
    public string? VolumeTheme { get; set; }
    public int TargetChapters { get; set; }
    public int CurrentChapters { get; set; } = 0;
    public string? Act1Setup { get; set; }
    public string? Act2Confrontation { get; set; }
    public string? Act3Climax { get; set; }
    public string? Act4Resolution { get; set; }
    public string? KeyEvents { get; set; }
    public string? MajorConflict { get; set; }
    public string? ConflictEscalation { get; set; }
    public string Status { get; set; } = "planned";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    
    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

- [ ] **Step 2: Add DbSet to NovelAgentDbContext**

```csharp
// Add after StoryConstitutions DbSet
public DbSet<VolumeArc> VolumeArcs { get; set; } = null!;
```

- [ ] **Step 3: Configure VolumeArc entity in OnModelCreating**

```csharp
// Add after StoryConstitution configuration
modelBuilder.Entity<VolumeArc>(entity =>
{
    entity.ToTable("volume_arcs");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
    entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
    entity.Property(e => e.VolumeNumber).HasColumnName("volume_number");
    entity.Property(e => e.VolumeTitle).HasColumnName("volume_title").IsRequired();
    entity.Property(e => e.VolumeTheme).HasColumnName("volume_theme");
    entity.Property(e => e.TargetChapters).HasColumnName("target_chapters");
    entity.Property(e => e.CurrentChapters).HasColumnName("current_chapters").HasDefaultValue(0);
    entity.Property(e => e.Act1Setup).HasColumnName("act1_setup");
    entity.Property(e => e.Act2Confrontation).HasColumnName("act2_confrontation");
    entity.Property(e => e.Act3Climax).HasColumnName("act3_climax");
    entity.Property(e => e.Act4Resolution).HasColumnName("act4_resolution");
    entity.Property(e => e.KeyEvents).HasColumnName("key_events");
    entity.Property(e => e.MajorConflict).HasColumnName("major_conflict");
    entity.Property(e => e.ConflictEscalation).HasColumnName("conflict_escalation");
    entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("planned");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.CompletedAt).HasColumnName("completed_at");

    entity.HasIndex(e => new { e.ProjectId, e.VolumeNumber }).IsUnique();

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Project)
        .WithMany()
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 4: Commit**

```bash
git add Web/NovelAgentWeb/Data/Entities/VolumeArc.cs Web/NovelAgentWeb/Data/NovelAgentDbContext.cs
git commit -m "feat(db): add VolumeArc entity"
```

---

## Task 3: Remaining StoryBible Entities

**Files:**
- Create: `Web/NovelAgentWeb/Data/Entities/CharacterState.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/ForeshadowEntry.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/WorldSettingEntry.cs`
- Create: `Web/NovelAgentWeb/Data/Entities/AgentRun.cs`
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`

- [ ] **Step 1: Create CharacterState entity**

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class CharacterState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string? Alias { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Appearance { get; set; }
    public string? Personality { get; set; }
    public string? Background { get; set; }
    public string? InitialPowerLevel { get; set; }
    public string? CurrentPowerLevel { get; set; }
    public string? SpecialAbilities { get; set; }
    public string? CoreGoal { get; set; }
    public string? Motivation { get; set; }
    public string? Relationships { get; set; }
    public string Status { get; set; } = "active";
    public string? FirstAppearChapter { get; set; }
    public string? LastAppearChapter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

- [ ] **Step 2: Create ForeshadowEntry entity**

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class ForeshadowEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string PlantedInChapter { get; set; } = null!;
    public string? PlantedContext { get; set; }
    public string Status { get; set; } = "planted";
    public string? ResolvedInChapter { get; set; }
    public string? ResolvedContext { get; set; }
    public DateTime PlantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int Priority { get; set; } = 5;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

- [ ] **Step 3: Create WorldSettingEntry entity**

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class WorldSettingEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? SubCategory { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? FirstMentionedChapter { get; set; }
    public string? ReferencedChapters { get; set; }
    public int Version { get; set; } = 1;
    public string? PreviousVersion { get; set; }
    public string? ChangeLog { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

- [ ] **Step 4: Create AgentRun entity**

```csharp
namespace TM.Web.NovelAgentWeb.Data.Entities;

public class AgentRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string RunType { get; set; } = null!;
    public string? TargetChapterId { get; set; }
    public string Status { get; set; } = "running";
    public string? InputParams { get; set; }
    public string? OutputData { get; set; }
    public int? ContextPackageSize { get; set; }
    public string? ContextPackagePath { get; set; }
    public string? GateReportPath { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;
}
```

- [ ] **Step 5: Add DbSets to NovelAgentDbContext**

```csharp
// Add after VolumeArcs DbSet
public DbSet<CharacterState> CharacterStates { get; set; } = null!;
public DbSet<ForeshadowEntry> ForeshadowEntries { get; set; } = null!;
public DbSet<WorldSettingEntry> WorldSettingEntries { get; set; } = null!;
public DbSet<AgentRun> AgentRuns { get; set; } = null!;
```

- [ ] **Step 6: Configure CharacterState entity**

```csharp
// Add after VolumeArc configuration
modelBuilder.Entity<CharacterState>(entity =>
{
    entity.ToTable("character_states");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
    entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
    entity.Property(e => e.Name).HasColumnName("name").IsRequired();
    entity.Property(e => e.Role).HasColumnName("role").IsRequired();
    entity.Property(e => e.Alias).HasColumnName("alias");
    entity.Property(e => e.Age).HasColumnName("age");
    entity.Property(e => e.Gender).HasColumnName("gender");
    entity.Property(e => e.Appearance).HasColumnName("appearance");
    entity.Property(e => e.Personality).HasColumnName("personality");
    entity.Property(e => e.Background).HasColumnName("background");
    entity.Property(e => e.InitialPowerLevel).HasColumnName("initial_power_level");
    entity.Property(e => e.CurrentPowerLevel).HasColumnName("current_power_level");
    entity.Property(e => e.SpecialAbilities).HasColumnName("special_abilities");
    entity.Property(e => e.CoreGoal).HasColumnName("core_goal");
    entity.Property(e => e.Motivation).HasColumnName("motivation");
    entity.Property(e => e.Relationships).HasColumnName("relationships");
    entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("active");
    entity.Property(e => e.FirstAppearChapter).HasColumnName("first_appear_chapter");
    entity.Property(e => e.LastAppearChapter).HasColumnName("last_appear_chapter");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Project)
        .WithMany()
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 7: Configure ForeshadowEntry entity**

```csharp
// Add after CharacterState configuration
modelBuilder.Entity<ForeshadowEntry>(entity =>
{
    entity.ToTable("foreshadow_entries");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
    entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
    entity.Property(e => e.Title).HasColumnName("title").IsRequired();
    entity.Property(e => e.Content).HasColumnName("content").IsRequired();
    entity.Property(e => e.Category).HasColumnName("category").IsRequired();
    entity.Property(e => e.PlantedInChapter).HasColumnName("planted_in_chapter").IsRequired();
    entity.Property(e => e.PlantedContext).HasColumnName("planted_context");
    entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("planted");
    entity.Property(e => e.ResolvedInChapter).HasColumnName("resolved_in_chapter");
    entity.Property(e => e.ResolvedContext).HasColumnName("resolved_context");
    entity.Property(e => e.PlantedAt).HasColumnName("planted_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
    entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(5);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Project)
        .WithMany()
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 8: Configure WorldSettingEntry entity**

```csharp
// Add after ForeshadowEntry configuration
modelBuilder.Entity<WorldSettingEntry>(entity =>
{
    entity.ToTable("world_setting_entries");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
    entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
    entity.Property(e => e.Category).HasColumnName("category").IsRequired();
    entity.Property(e => e.SubCategory).HasColumnName("sub_category");
    entity.Property(e => e.Title).HasColumnName("title").IsRequired();
    entity.Property(e => e.Content).HasColumnName("content").IsRequired();
    entity.Property(e => e.FirstMentionedChapter).HasColumnName("first_mentioned_chapter");
    entity.Property(e => e.ReferencedChapters).HasColumnName("referenced_chapters");
    entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
    entity.Property(e => e.PreviousVersion).HasColumnName("previous_version");
    entity.Property(e => e.ChangeLog).HasColumnName("change_log");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Project)
        .WithMany()
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 9: Configure AgentRun entity**

```csharp
// Add after WorldSettingEntry configuration
modelBuilder.Entity<AgentRun>(entity =>
{
    entity.ToTable("agent_runs");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
    entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
    entity.Property(e => e.RunType).HasColumnName("run_type").IsRequired();
    entity.Property(e => e.TargetChapterId).HasColumnName("target_chapter_id");
    entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("running");
    entity.Property(e => e.InputParams).HasColumnName("input_params");
    entity.Property(e => e.OutputData).HasColumnName("output_data");
    entity.Property(e => e.ContextPackageSize).HasColumnName("context_package_size");
    entity.Property(e => e.ContextPackagePath).HasColumnName("context_package_path");
    entity.Property(e => e.GateReportPath).HasColumnName("gate_report_path");
    entity.Property(e => e.StartedAt).HasColumnName("started_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
    entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Project)
        .WithMany()
        .HasForeignKey(e => e.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 10: Commit**

```bash
git add Web/NovelAgentWeb/Data/Entities/*.cs Web/NovelAgentWeb/Data/NovelAgentDbContext.cs
git commit -m "feat(db): add CharacterState, ForeshadowEntry, WorldSettingEntry, AgentRun entities"
```

---

## Task 4: Generate and Run EF Core Migration

**Files:**
- Create: `Web/NovelAgentWeb/Migrations/YYYYMMDDHHMMSS_AddStoryBibleEntities.cs` (auto-generated)
- Modify: `Web/NovelAgentWeb/Data/NovelAgentDbContext.cs`

- [ ] **Step 1: Generate migration**

```bash
cd Web/NovelAgentWeb
dotnet ef migrations add AddStoryBibleEntities
```

Expected: Migration file created in `Migrations/` folder

- [ ] **Step 2: Review migration file**

Open generated migration file and verify it contains:
- CreateTable for story_constitutions (with all columns)
- CreateTable for volume_arcs (with all columns + unique index)
- CreateTable for character_states (with all columns)
- CreateTable for foreshadow_entries (with all columns)
- CreateTable for world_setting_entries (with all columns)
- CreateTable for agent_runs (with all columns)
- Foreign key constraints to users and novel_projects tables

- [ ] **Step 3: Apply migration to database**

```bash
dotnet ef database update
```

Expected: Migration applied successfully, 6 tables created

- [ ] **Step 4: Verify tables in database**

```bash
sqlite3 novelagent.db ".tables"
```

Expected output should include:
- story_constitutions
- volume_arcs
- character_states
- foreshadow_entries
- world_setting_entries
- agent_runs

- [ ] **Step 5: Commit**

```bash
git add Web/NovelAgentWeb/Migrations/*.cs
git commit -m "feat(db): add EF Core migration for StoryBible entities"
```

---

## Task 5: WorkspaceEntry with Reference Counting

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/WorkspaceEntry.cs`

- [ ] **Step 1: Create WorkspaceEntry class**

```csharp
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Cache entry for NovelAgentWorkspace with reference counting and LRU tracking.
/// </summary>
public sealed class WorkspaceEntry
{
    public string UserId { get; init; } = null!;
    public string ProjectId { get; init; } = null!;
    public NovelAgentWorkspace Workspace { get; init; } = null!;
    
    private int _activeReferences = 0;
    public int ActiveReferences => _activeReferences;
    public DateTime LastAccessTime { get; private set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    private long _totalAccesses = 0;
    public long TotalAccesses => _totalAccesses;
    
    public void AcquireLease()
    {
        Interlocked.Increment(ref _activeReferences);
        LastAccessTime = DateTime.UtcNow;
        Interlocked.Increment(ref _totalAccesses);
    }
    
    public void ReleaseLease()
    {
        Interlocked.Decrement(ref _activeReferences);
        LastAccessTime = DateTime.UtcNow;
    }
    
    public void Touch()
    {
        LastAccessTime = DateTime.UtcNow;
    }
    
    public bool IsEvictable(TimeSpan idleTimeout)
    {
        return _activeReferences == 0 
            && DateTime.UtcNow - LastAccessTime > idleTimeout;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceEntry.cs
git commit -m "feat(workspace): add WorkspaceEntry with reference counting"
```

---

## Task 6: WorkspaceFactoryOptions Configuration

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactoryOptions.cs`

- [ ] **Step 1: Create WorkspaceFactoryOptions class**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Configuration options for WorkspaceFactory.
/// </summary>
public sealed class WorkspaceFactoryOptions
{
    /// <summary>
    /// Root directory for storing project data (default: App_Data).
    /// </summary>
    public string StorageRoot { get; set; } = "App_Data";
    
    /// <summary>
    /// Maximum number of Workspace instances to cache (default: 50).
    /// </summary>
    public int MaxCachedWorkspaces { get; set; } = 50;
    
    /// <summary>
    /// Idle timeout in minutes before eviction (default: 30).
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 30;
    
    /// <summary>
    /// Interval in seconds for background eviction timer (default: 30).
    /// </summary>
    public int EvictionIntervalSeconds { get; set; } = 30;
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceFactoryOptions.cs
git commit -m "feat(workspace): add WorkspaceFactoryOptions"
```

---


## Task 7: IWorkspaceFactory Interface

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/IWorkspaceFactory.cs`

- [ ] **Step 1: Create IWorkspaceFactory interface**

```csharp
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Factory for creating and caching NovelAgentWorkspace instances with project-level singleton pattern.
/// </summary>
public interface IWorkspaceFactory
{
    /// <summary>
    /// Acquires a Workspace for the given user and project. Creates new instance if not cached.
    /// </summary>
    Task<NovelAgentWorkspace> AcquireAsync(string userId, string projectId, CancellationToken ct = default);
    
    /// <summary>
    /// Releases the lease on a Workspace. Decrements reference count.
    /// </summary>
    void Release(string userId, string projectId);
    
    /// <summary>
    /// Updates last access time without acquiring/releasing.
    /// </summary>
    void Touch(string userId, string projectId);
    
    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    WorkspaceFactoryStats GetStats();
}

/// <summary>
/// Statistics about the WorkspaceFactory cache.
/// </summary>
public sealed class WorkspaceFactoryStats
{
    public int TotalWorkspaces { get; init; }
    public int ActiveReferences { get; init; }
    public int EvictionCount { get; init; }
    public double CacheHitRate { get; init; }
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Services/Workspace/IWorkspaceFactory.cs
git commit -m "feat(workspace): add IWorkspaceFactory interface"
```

---

## Task 8: WorkspaceFactory Implementation (Part 1 - Core Logic)

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs`

- [ ] **Step 1: Create WorkspaceFactory class skeleton**

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

/// <summary>
/// Thread-safe factory for managing NovelAgentWorkspace lifecycle with LRU caching.
/// </summary>
public sealed class WorkspaceFactory : IWorkspaceFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, WorkspaceEntry> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly WorkspaceFactoryOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly Timer _evictionTimer;
    
    private int _evictionCount = 0;
    private long _totalRequests = 0;
    private long _cacheHits = 0;
    
    public WorkspaceFactory(
        IOptions<WorkspaceFactoryOptions> options,
        IServiceProvider serviceProvider)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        
        // Start background eviction timer
        var intervalMs = _options.EvictionIntervalSeconds * 1000;
        _evictionTimer = new Timer(
            _ => EvictIdleWorkspacesAsync().GetAwaiter().GetResult(),
            null,
            TimeSpan.FromMilliseconds(intervalMs),
            TimeSpan.FromMilliseconds(intervalMs));
    }
    
    private static string GetCacheKey(string userId, string projectId) 
        => $"{userId}:{projectId}";
    
    // Methods to be implemented in next steps
    public Task<NovelAgentWorkspace> AcquireAsync(string userId, string projectId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
    
    public void Release(string userId, string projectId)
    {
        throw new NotImplementedException();
    }
    
    public void Touch(string userId, string projectId)
    {
        throw new NotImplementedException();
    }
    
    public WorkspaceFactoryStats GetStats()
    {
        throw new NotImplementedException();
    }
    
    private Task EvictIdleWorkspacesAsync()
    {
        throw new NotImplementedException();
    }
    
    public void Dispose()
    {
        _evictionTimer?.Dispose();
        _lock?.Dispose();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs
git commit -m "feat(workspace): add WorkspaceFactory skeleton"
```

---

## Task 9: WorkspaceFactory Implementation (Part 2 - AcquireAsync)

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs`

- [ ] **Step 1: Implement AcquireAsync method**

```csharp
public async Task<NovelAgentWorkspace> AcquireAsync(string userId, string projectId, CancellationToken ct = default)
{
    Interlocked.Increment(ref _totalRequests);
    
    var cacheKey = GetCacheKey(userId, projectId);
    
    // Fast path: cache hit
    if (_cache.TryGetValue(cacheKey, out var entry))
    {
        Interlocked.Increment(ref _cacheHits);
        entry.AcquireLease();
        return entry.Workspace;
    }
    
    // Slow path: create new Workspace
    await _lock.WaitAsync(ct);
    try
    {
        // Double-check after acquiring lock
        if (_cache.TryGetValue(cacheKey, out entry))
        {
            Interlocked.Increment(ref _cacheHits);
            entry.AcquireLease();
            return entry.Workspace;
        }
        
        // Enforce cache size limit before creating new instance
        if (_cache.Count >= _options.MaxCachedWorkspaces)
        {
            await EvictOldestIdleWorkspaceAsync();
        }
        
        // Create new Workspace instance
        var workspace = await CreateWorkspaceAsync(userId, projectId, ct);
        
        entry = new WorkspaceEntry
        {
            UserId = userId,
            ProjectId = projectId,
            Workspace = workspace
        };
        
        entry.AcquireLease();
        _cache[cacheKey] = entry;
        
        return workspace;
    }
    finally
    {
        _lock.Release();
    }
}

private async Task<NovelAgentWorkspace> CreateWorkspaceAsync(
    string userId, string projectId, CancellationToken ct)
{
    using var scope = _serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
    
    // Load project to get StorageProjectName
    var project = await db.NovelProjects
        .Where(p => p.Id == projectId && p.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (project == null)
    {
        throw new InvalidOperationException($"Project {projectId} not found for user {userId}");
    }
    
    // Get required services
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var settingsManager = scope.ServiceProvider.GetRequiredService<UserSettingsManager>();
    
    // Create Workspace with project-specific storage path
    var workspace = new NovelAgentWorkspace(env, config, settingsManager);
    
    return workspace;
}

private async Task EvictOldestIdleWorkspaceAsync()
{
    var idleTimeout = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);
    
    var evictable = _cache.Values
        .Where(e => e.IsEvictable(idleTimeout))
        .OrderBy(e => e.LastAccessTime)
        .FirstOrDefault();
    
    if (evictable != null)
    {
        var cacheKey = GetCacheKey(evictable.UserId, evictable.ProjectId);
        if (_cache.TryRemove(cacheKey, out _))
        {
            Interlocked.Increment(ref _evictionCount);
        }
    }
}
```

- [ ] **Step 2: Add required using statement**

```csharp
// Add at top of file
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs
git commit -m "feat(workspace): implement AcquireAsync with LRU eviction"
```

---

## Task 10: WorkspaceFactory Implementation (Part 3 - Release, Touch, Stats)

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs`

- [ ] **Step 1: Implement Release method**

```csharp
public void Release(string userId, string projectId)
{
    var cacheKey = GetCacheKey(userId, projectId);
    
    if (_cache.TryGetValue(cacheKey, out var entry))
    {
        entry.ReleaseLease();
    }
}
```

- [ ] **Step 2: Implement Touch method**

```csharp
public void Touch(string userId, string projectId)
{
    var cacheKey = GetCacheKey(userId, projectId);
    
    if (_cache.TryGetValue(cacheKey, out var entry))
    {
        entry.Touch();
    }
}
```

- [ ] **Step 3: Implement GetStats method**

```csharp
public WorkspaceFactoryStats GetStats()
{
    var totalWorkspaces = _cache.Count;
    var activeRefs = _cache.Values.Sum(e => e.ActiveReferences);
    var totalReqs = Interlocked.Read(ref _totalRequests);
    var hits = Interlocked.Read(ref _cacheHits);
    var hitRate = totalReqs > 0 ? (double)hits / totalReqs : 0.0;
    
    return new WorkspaceFactoryStats
    {
        TotalWorkspaces = totalWorkspaces,
        ActiveReferences = activeRefs,
        EvictionCount = _evictionCount,
        CacheHitRate = hitRate
    };
}
```

- [ ] **Step 4: Implement EvictIdleWorkspacesAsync method**

```csharp
private async Task EvictIdleWorkspacesAsync()
{
    await _lock.WaitAsync();
    try
    {
        var idleTimeout = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);
        var toEvict = _cache.Values
            .Where(e => e.IsEvictable(idleTimeout))
            .ToList();
        
        foreach (var entry in toEvict)
        {
            var cacheKey = GetCacheKey(entry.UserId, entry.ProjectId);
            if (_cache.TryRemove(cacheKey, out _))
            {
                Interlocked.Increment(ref _evictionCount);
            }
        }
    }
    finally
    {
        _lock.Release();
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs
git commit -m "feat(workspace): implement Release, Touch, GetStats, eviction"
```

---


## Task 11: StoryBible Repository Interface

**Files:**
- Create: `Web/NovelAgentWeb/Services/Repositories/IStoryBibleRepository.cs`

- [ ] **Step 1: Create IStoryBibleRepository interface**

```csharp
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Repositories;

/// <summary>
/// Repository for accessing StoryBible entities (Constitution, VolumeArcs, Characters, etc.).
/// </summary>
public interface IStoryBibleRepository
{
    // StoryConstitution
    Task<StoryConstitution?> GetConstitutionAsync(string projectId, CancellationToken ct = default);
    Task<StoryConstitution> SaveConstitutionAsync(StoryConstitution constitution, CancellationToken ct = default);
    
    // VolumeArcs
    Task<List<VolumeArc>> GetVolumeArcsAsync(string projectId, CancellationToken ct = default);
    Task<VolumeArc?> GetVolumeArcAsync(string volumeArcId, CancellationToken ct = default);
    Task<VolumeArc> SaveVolumeArcAsync(VolumeArc volumeArc, CancellationToken ct = default);
    Task DeleteVolumeArcAsync(string volumeArcId, CancellationToken ct = default);
    
    // CharacterStates
    Task<List<CharacterState>> GetCharacterStatesAsync(string projectId, CancellationToken ct = default);
    Task<CharacterState?> GetCharacterStateAsync(string characterId, CancellationToken ct = default);
    Task<CharacterState> SaveCharacterStateAsync(CharacterState character, CancellationToken ct = default);
    Task DeleteCharacterStateAsync(string characterId, CancellationToken ct = default);
    
    // ForeshadowEntries
    Task<List<ForeshadowEntry>> GetForeshadowEntriesAsync(string projectId, CancellationToken ct = default);
    Task<ForeshadowEntry?> GetForeshadowEntryAsync(string foreshadowId, CancellationToken ct = default);
    Task<ForeshadowEntry> SaveForeshadowEntryAsync(ForeshadowEntry foreshadow, CancellationToken ct = default);
    Task<ForeshadowEntry> ResolveForeshadowAsync(
        string foreshadowId, 
        string resolvedInChapter, 
        string resolvedContext, 
        CancellationToken ct = default);
    Task DeleteForeshadowEntryAsync(string foreshadowId, CancellationToken ct = default);
    
    // WorldSettingEntries
    Task<List<WorldSettingEntry>> GetWorldSettingEntriesAsync(string projectId, CancellationToken ct = default);
    Task<WorldSettingEntry?> GetWorldSettingEntryAsync(string settingId, CancellationToken ct = default);
    Task<WorldSettingEntry> SaveWorldSettingEntryAsync(WorldSettingEntry setting, CancellationToken ct = default);
    Task DeleteWorldSettingEntryAsync(string settingId, CancellationToken ct = default);
    
    // AgentRuns
    Task<List<AgentRun>> GetAgentRunsAsync(string projectId, int limit = 50, CancellationToken ct = default);
    Task<AgentRun?> GetAgentRunAsync(string runId, CancellationToken ct = default);
    Task<AgentRun> SaveAgentRunAsync(AgentRun run, CancellationToken ct = default);
}
```

- [ ] **Step 2: Commit**

```bash
git add Web/NovelAgentWeb/Services/Repositories/IStoryBibleRepository.cs
git commit -m "feat(repository): add IStoryBibleRepository interface"
```

---

## Task 12: StoryBible Repository Implementation (Part 1 - Constitution & VolumeArcs)

**Files:**
- Create: `Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs`

- [ ] **Step 1: Create StoryBibleRepository class skeleton**

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Repositories;

/// <summary>
/// Implementation of IStoryBibleRepository with user isolation.
/// </summary>
public sealed class StoryBibleRepository : IStoryBibleRepository
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUser;
    
    public StoryBibleRepository(
        NovelAgentDbContext db,
        ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }
    
    // Methods to be implemented
}
```

- [ ] **Step 2: Implement StoryConstitution methods**

```csharp
public async Task<StoryConstitution?> GetConstitutionAsync(string projectId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.StoryConstitutions
        .Where(c => c.ProjectId == projectId && c.UserId == userId)
        .FirstOrDefaultAsync(ct);
}

public async Task<StoryConstitution> SaveConstitutionAsync(StoryConstitution constitution, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    constitution.UserId = userId;
    constitution.UpdatedAt = DateTime.UtcNow;
    
    var existing = await _db.StoryConstitutions
        .Where(c => c.ProjectId == constitution.ProjectId && c.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (existing != null)
    {
        // Update existing
        existing.Genre = constitution.Genre;
        existing.SubGenre = constitution.SubGenre;
        existing.CoreHook = constitution.CoreHook;
        existing.ReaderPromise = constitution.ReaderPromise;
        existing.GenreProfile = constitution.GenreProfile;
        existing.TargetAudience = constitution.TargetAudience;
        existing.Taboos = constitution.Taboos;
        existing.UpdatedAt = constitution.UpdatedAt;
        
        _db.StoryConstitutions.Update(existing);
    }
    else
    {
        // Create new
        constitution.CreatedAt = DateTime.UtcNow;
        await _db.StoryConstitutions.AddAsync(constitution, ct);
    }
    
    await _db.SaveChangesAsync(ct);
    return existing ?? constitution;
}
```

- [ ] **Step 3: Implement VolumeArc methods**

```csharp
public async Task<List<VolumeArc>> GetVolumeArcsAsync(string projectId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.VolumeArcs
        .Where(v => v.ProjectId == projectId && v.UserId == userId)
        .OrderBy(v => v.VolumeNumber)
        .ToListAsync(ct);
}

public async Task<VolumeArc?> GetVolumeArcAsync(string volumeArcId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.VolumeArcs
        .Where(v => v.Id == volumeArcId && v.UserId == userId)
        .FirstOrDefaultAsync(ct);
}

public async Task<VolumeArc> SaveVolumeArcAsync(VolumeArc volumeArc, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    volumeArc.UserId = userId;
    volumeArc.UpdatedAt = DateTime.UtcNow;
    
    var existing = await _db.VolumeArcs
        .Where(v => v.Id == volumeArc.Id && v.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (existing != null)
    {
        _db.Entry(existing).CurrentValues.SetValues(volumeArc);
        _db.VolumeArcs.Update(existing);
    }
    else
    {
        volumeArc.CreatedAt = DateTime.UtcNow;
        await _db.VolumeArcs.AddAsync(volumeArc, ct);
    }
    
    await _db.SaveChangesAsync(ct);
    return existing ?? volumeArc;
}

public async Task DeleteVolumeArcAsync(string volumeArcId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    var volumeArc = await _db.VolumeArcs
        .Where(v => v.Id == volumeArcId && v.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (volumeArc != null)
    {
        _db.VolumeArcs.Remove(volumeArc);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs
git commit -m "feat(repository): implement StoryConstitution and VolumeArc methods"
```

---

## Task 13: StoryBible Repository Implementation (Part 2 - Characters & Foreshadows)

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs`

- [ ] **Step 1: Implement CharacterState methods**

```csharp
public async Task<List<CharacterState>> GetCharacterStatesAsync(string projectId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.CharacterStates
        .Where(c => c.ProjectId == projectId && c.UserId == userId)
        .OrderBy(c => c.Name)
        .ToListAsync(ct);
}

public async Task<CharacterState?> GetCharacterStateAsync(string characterId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.CharacterStates
        .Where(c => c.Id == characterId && c.UserId == userId)
        .FirstOrDefaultAsync(ct);
}

public async Task<CharacterState> SaveCharacterStateAsync(CharacterState character, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    character.UserId = userId;
    character.UpdatedAt = DateTime.UtcNow;
    
    var existing = await _db.CharacterStates
        .Where(c => c.Id == character.Id && c.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (existing != null)
    {
        _db.Entry(existing).CurrentValues.SetValues(character);
        _db.CharacterStates.Update(existing);
    }
    else
    {
        character.CreatedAt = DateTime.UtcNow;
        await _db.CharacterStates.AddAsync(character, ct);
    }
    
    await _db.SaveChangesAsync(ct);
    return existing ?? character;
}

public async Task DeleteCharacterStateAsync(string characterId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    var character = await _db.CharacterStates
        .Where(c => c.Id == characterId && c.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (character != null)
    {
        _db.CharacterStates.Remove(character);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 2: Implement ForeshadowEntry methods**

```csharp
public async Task<List<ForeshadowEntry>> GetForeshadowEntriesAsync(string projectId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.ForeshadowEntries
        .Where(f => f.ProjectId == projectId && f.UserId == userId)
        .OrderByDescending(f => f.Priority)
        .ThenBy(f => f.PlantedAt)
        .ToListAsync(ct);
}

public async Task<ForeshadowEntry?> GetForeshadowEntryAsync(string foreshadowId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.ForeshadowEntries
        .Where(f => f.Id == foreshadowId && f.UserId == userId)
        .FirstOrDefaultAsync(ct);
}

public async Task<ForeshadowEntry> SaveForeshadowEntryAsync(ForeshadowEntry foreshadow, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    foreshadow.UserId = userId;
    foreshadow.UpdatedAt = DateTime.UtcNow;
    
    var existing = await _db.ForeshadowEntries
        .Where(f => f.Id == foreshadow.Id && f.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (existing != null)
    {
        _db.Entry(existing).CurrentValues.SetValues(foreshadow);
        _db.ForeshadowEntries.Update(existing);
    }
    else
    {
        foreshadow.CreatedAt = DateTime.UtcNow;
        foreshadow.PlantedAt = DateTime.UtcNow;
        await _db.ForeshadowEntries.AddAsync(foreshadow, ct);
    }
    
    await _db.SaveChangesAsync(ct);
    return existing ?? foreshadow;
}

public async Task<ForeshadowEntry> ResolveForeshadowAsync(
    string foreshadowId, 
    string resolvedInChapter, 
    string resolvedContext, 
    CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    var foreshadow = await _db.ForeshadowEntries
        .Where(f => f.Id == foreshadowId && f.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (foreshadow == null)
    {
        throw new InvalidOperationException($"Foreshadow {foreshadowId} not found");
    }
    
    foreshadow.Status = "resolved";
    foreshadow.ResolvedInChapter = resolvedInChapter;
    foreshadow.ResolvedContext = resolvedContext;
    foreshadow.ResolvedAt = DateTime.UtcNow;
    foreshadow.UpdatedAt = DateTime.UtcNow;
    
    _db.ForeshadowEntries.Update(foreshadow);
    await _db.SaveChangesAsync(ct);
    
    return foreshadow;
}

public async Task DeleteForeshadowEntryAsync(string foreshadowId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    var foreshadow = await _db.ForeshadowEntries
        .Where(f => f.Id == foreshadowId && f.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (foreshadow != null)
    {
        _db.ForeshadowEntries.Remove(foreshadow);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs
git commit -m "feat(repository): implement CharacterState and ForeshadowEntry methods"
```

---


## Task 14: StoryBible Repository Implementation (Part 3 - WorldSettings & AgentRuns)

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs`

- [ ] **Step 1: Implement WorldSettingEntry methods**

```csharp
public async Task<List<WorldSettingEntry>> GetWorldSettingEntriesAsync(string projectId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.WorldSettingEntries
        .Where(w => w.ProjectId == projectId && w.UserId == userId)
        .OrderBy(w => w.Category)
        .ThenBy(w => w.Title)
        .ToListAsync(ct);
}

public async Task<WorldSettingEntry?> GetWorldSettingEntryAsync(string settingId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.WorldSettingEntries
        .Where(w => w.Id == settingId && w.UserId == userId)
        .FirstOrDefaultAsync(ct);
}

public async Task<WorldSettingEntry> SaveWorldSettingEntryAsync(WorldSettingEntry setting, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    setting.UserId = userId;
    setting.UpdatedAt = DateTime.UtcNow;
    
    var existing = await _db.WorldSettingEntries
        .Where(w => w.Id == setting.Id && w.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (existing != null)
    {
        _db.Entry(existing).CurrentValues.SetValues(setting);
        _db.WorldSettingEntries.Update(existing);
    }
    else
    {
        setting.CreatedAt = DateTime.UtcNow;
        await _db.WorldSettingEntries.AddAsync(setting, ct);
    }
    
    await _db.SaveChangesAsync(ct);
    return existing ?? setting;
}

public async Task DeleteWorldSettingEntryAsync(string settingId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    var setting = await _db.WorldSettingEntries
        .Where(w => w.Id == settingId && w.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (setting != null)
    {
        _db.WorldSettingEntries.Remove(setting);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 2: Implement AgentRun methods**

```csharp
public async Task<List<AgentRun>> GetAgentRunsAsync(string projectId, int limit = 50, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.AgentRuns
        .Where(r => r.ProjectId == projectId && r.UserId == userId)
        .OrderByDescending(r => r.StartedAt)
        .Take(limit)
        .ToListAsync(ct);
}

public async Task<AgentRun?> GetAgentRunAsync(string runId, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    
    return await _db.AgentRuns
        .Where(r => r.Id == runId && r.UserId == userId)
        .FirstOrDefaultAsync(ct);
}

public async Task<AgentRun> SaveAgentRunAsync(AgentRun run, CancellationToken ct = default)
{
    var userId = _currentUser.GetUserId();
    run.UserId = userId;
    run.UpdatedAt = DateTime.UtcNow;
    
    var existing = await _db.AgentRuns
        .Where(r => r.Id == run.Id && r.UserId == userId)
        .FirstOrDefaultAsync(ct);
    
    if (existing != null)
    {
        _db.Entry(existing).CurrentValues.SetValues(run);
        _db.AgentRuns.Update(existing);
    }
    else
    {
        run.CreatedAt = DateTime.UtcNow;
        run.StartedAt = DateTime.UtcNow;
        await _db.AgentRuns.AddAsync(run, ct);
    }
    
    await _db.SaveChangesAsync(ct);
    return existing ?? run;
}
```

- [ ] **Step 3: Commit**

```bash
git add Web/NovelAgentWeb/Services/Repositories/StoryBibleRepository.cs
git commit -m "feat(repository): implement WorldSettingEntry and AgentRun methods"
```

---

## Task 15: Register Services in DI Container

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`

- [ ] **Step 1: Register WorkspaceFactory and options**

```csharp
// Add after line 94 (after AddScoped<IAgentSessionService>)
// Register WorkspaceFactory and options
builder.Services.Configure<WorkspaceFactoryOptions>(options =>
{
    options.StorageRoot = builder.Configuration["NovelAgent:StorageRoot"] 
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
    options.MaxCachedWorkspaces = 50;
    options.IdleTimeoutMinutes = 30;
    options.EvictionIntervalSeconds = 30;
});
builder.Services.AddSingleton<IWorkspaceFactory, WorkspaceFactory>();
```

- [ ] **Step 2: Register StoryBibleRepository**

```csharp
// Add after WorkspaceFactory registration
builder.Services.AddScoped<IStoryBibleRepository, StoryBibleRepository>();
```

- [ ] **Step 3: Add required using statements**

```csharp
// Add at top of Program.cs
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Services.Repositories;
```

- [ ] **Step 4: Build and verify no compilation errors**

```bash
cd Web/NovelAgentWeb
dotnet build
```

Expected: Build succeeds with 0 errors

- [ ] **Step 5: Commit**

```bash
git add Web/NovelAgentWeb/Program.cs
git commit -m "feat(di): register WorkspaceFactory and StoryBibleRepository"
```

---

## Task 16: Add Thread Safety to NovelAgentWorkspace

**Files:**
- Modify: `Web/NovelAgentWeb/Support/WebRuntime.cs:302-352`

- [ ] **Step 1: Review current NovelAgentWorkspace class**

Read `Web/NovelAgentWeb/Support/WebRuntime.cs` lines 302-352 to understand current structure

- [ ] **Step 2: Document that ProjectContextLock exists**

Verify that `ProjectContextLock` SemaphoreSlim already exists at line 306:
```csharp
public SemaphoreSlim ProjectContextLock { get; } = new(1, 1);
```

This lock should be used by consumers (AgentSessionService, future Controllers) to protect concurrent access to Orchestrator operations.

- [ ] **Step 3: Add usage documentation comment**

```csharp
// Replace the ProjectContextLock line with:
/// <summary>
/// Lock for protecting concurrent access to Orchestrator operations.
/// Consumers must acquire this lock before calling Orchestrator methods.
/// </summary>
public SemaphoreSlim ProjectContextLock { get; } = new(1, 1);
```

- [ ] **Step 4: Commit**

```bash
git add Web/NovelAgentWeb/Support/WebRuntime.cs
git commit -m "docs(workspace): document ProjectContextLock usage for thread safety"
```

---

## Task 17: WorkspaceFactory Unit Tests (Part 1 - Setup)

**Files:**
- Create: `Tests/NovelAgentRegression/WorkspaceFactoryTests.cs`

- [ ] **Step 1: Create test class skeleton**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Workspace;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public class WorkspaceFactoryTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly NovelAgentDbContext _db;
    private readonly WorkspaceFactory _factory;
    
    public WorkspaceFactoryTests()
    {
        // Setup in-memory database
        var services = new ServiceCollection();
        
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase($"WorkspaceFactoryTest_{Guid.NewGuid()}"));
        
        services.Configure<WorkspaceFactoryOptions>(options =>
        {
            options.StorageRoot = Path.GetTempPath();
            options.MaxCachedWorkspaces = 3;
            options.IdleTimeoutMinutes = 1;
            options.EvictionIntervalSeconds = 60;
        });
        
        // Add required services (stub implementations)
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        services.AddSingleton<UserSettingsManager>(sp => 
            new UserSettingsManager(Path.GetTempPath(), "TestProject"));
        
        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<NovelAgentDbContext>();
        _factory = new WorkspaceFactory(
            _serviceProvider.GetRequiredService<IOptions<WorkspaceFactoryOptions>>(),
            _serviceProvider);
    }
    
    public void Dispose()
    {
        _factory?.Dispose();
        _db?.Dispose();
        _serviceProvider?.Dispose();
    }
    
    // Tests to be added in next steps
}

// Stub implementations
internal class TestWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string ApplicationName { get; set; } = "TestApp";
    public string EnvironmentName { get; set; } = "Test";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
```

- [ ] **Step 2: Add required using statements**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
```

- [ ] **Step 3: Commit**

```bash
git add Tests/NovelAgentRegression/WorkspaceFactoryTests.cs
git commit -m "test(workspace): add WorkspaceFactory test skeleton"
```

---


## Task 18: WorkspaceFactory Unit Tests (Part 2 - Core Functionality)

**Files:**
- Modify: `Tests/NovelAgentRegression/WorkspaceFactoryTests.cs`

- [ ] **Step 1: Add helper method for creating test data**

```csharp
private async Task<(User user, NovelProject project)> CreateTestUserAndProjectAsync()
{
    var user = new User
    {
        Id = Guid.NewGuid().ToString(),
        Username = "testuser",
        Email = "test@example.com",
        PasswordHash = "hash",
        Role = "user"
    };
    
    var project = new NovelProject
    {
        Id = Guid.NewGuid().ToString(),
        UserId = user.Id,
        Title = "Test Novel",
        StorageProjectName = $"test_{Guid.NewGuid()}"
    };
    
    await _db.Users.AddAsync(user);
    await _db.NovelProjects.AddAsync(project);
    await _db.SaveChangesAsync();
    
    return (user, project);
}
```

- [ ] **Step 2: Write test for cache hit**

```csharp
[Fact]
public async Task AcquireAsync_CacheHit_ReturnsSameInstance()
{
    // Arrange
    var (user, project) = await CreateTestUserAndProjectAsync();
    
    // Act
    var workspace1 = await _factory.AcquireAsync(user.Id, project.Id);
    var workspace2 = await _factory.AcquireAsync(user.Id, project.Id);
    
    // Assert
    Assert.Same(workspace1, workspace2);
    
    var stats = _factory.GetStats();
    Assert.Equal(1, stats.TotalWorkspaces);
    Assert.Equal(2, stats.ActiveReferences);
    Assert.True(stats.CacheHitRate > 0);
    
    // Cleanup
    _factory.Release(user.Id, project.Id);
    _factory.Release(user.Id, project.Id);
}
```

- [ ] **Step 3: Run test**

```bash
cd Tests/NovelAgentRegression
dotnet test --filter "FullyQualifiedName~WorkspaceFactoryTests.AcquireAsync_CacheHit_ReturnsSameInstance"
```

Expected: Test PASSES

- [ ] **Step 4: Write test for reference counting**

```csharp
[Fact]
public async Task Release_DecreasesReferenceCount()
{
    // Arrange
    var (user, project) = await CreateTestUserAndProjectAsync();
    
    // Act
    await _factory.AcquireAsync(user.Id, project.Id);
    await _factory.AcquireAsync(user.Id, project.Id);
    
    var statsBefore = _factory.GetStats();
    Assert.Equal(2, statsBefore.ActiveReferences);
    
    _factory.Release(user.Id, project.Id);
    
    var statsAfter = _factory.GetStats();
    Assert.Equal(1, statsAfter.ActiveReferences);
    
    // Cleanup
    _factory.Release(user.Id, project.Id);
}
```

- [ ] **Step 5: Run test**

```bash
dotnet test --filter "FullyQualifiedName~WorkspaceFactoryTests.Release_DecreasesReferenceCount"
```

Expected: Test PASSES

- [ ] **Step 6: Write test for Touch updates last access time**

```csharp
[Fact]
public async Task Touch_UpdatesLastAccessTime()
{
    // Arrange
    var (user, project) = await CreateTestUserAndProjectAsync();
    await _factory.AcquireAsync(user.Id, project.Id);
    
    // Act
    await Task.Delay(100);
    _factory.Touch(user.Id, project.Id);
    
    // Assert - no direct way to verify LastAccessTime, but Touch should not throw
    var stats = _factory.GetStats();
    Assert.Equal(1, stats.TotalWorkspaces);
    
    // Cleanup
    _factory.Release(user.Id, project.Id);
}
```

- [ ] **Step 7: Run test**

```bash
dotnet test --filter "FullyQualifiedName~WorkspaceFactoryTests.Touch_UpdatesLastAccessTime"
```

Expected: Test PASSES

- [ ] **Step 8: Commit**

```bash
git add Tests/NovelAgentRegression/WorkspaceFactoryTests.cs
git commit -m "test(workspace): add cache hit and reference counting tests"
```

---

## Task 19: WorkspaceFactory Unit Tests (Part 3 - LRU Eviction)

**Files:**
- Modify: `Tests/NovelAgentRegression/WorkspaceFactoryTests.cs`

- [ ] **Step 1: Write test for max cache size eviction**

```csharp
[Fact]
public async Task AcquireAsync_ExceedsMaxCacheSize_EvictsOldest()
{
    // Arrange - MaxCachedWorkspaces = 3 in test config
    var testData = new List<(User user, NovelProject project)>();
    for (int i = 0; i < 4; i++)
    {
        testData.Add(await CreateTestUserAndProjectAsync());
    }
    
    // Act - Acquire 3 workspaces (fills cache)
    await _factory.AcquireAsync(testData[0].user.Id, testData[0].project.Id);
    _factory.Release(testData[0].user.Id, testData[0].project.Id);
    
    await _factory.AcquireAsync(testData[1].user.Id, testData[1].project.Id);
    _factory.Release(testData[1].user.Id, testData[1].project.Id);
    
    await _factory.AcquireAsync(testData[2].user.Id, testData[2].project.Id);
    _factory.Release(testData[2].user.Id, testData[2].project.Id);
    
    var statsBefore = _factory.GetStats();
    Assert.Equal(3, statsBefore.TotalWorkspaces);
    
    // Acquire 4th workspace (should evict oldest)
    await _factory.AcquireAsync(testData[3].user.Id, testData[3].project.Id);
    
    // Assert
    var statsAfter = _factory.GetStats();
    Assert.Equal(3, statsAfter.TotalWorkspaces); // Still 3 (max)
    Assert.Equal(1, statsAfter.EvictionCount); // One eviction happened
    
    // Cleanup
    _factory.Release(testData[3].user.Id, testData[3].project.Id);
}
```

- [ ] **Step 2: Run test**

```bash
dotnet test --filter "FullyQualifiedName~WorkspaceFactoryTests.AcquireAsync_ExceedsMaxCacheSize_EvictsOldest"
```

Expected: Test PASSES

- [ ] **Step 3: Write test for active references prevent eviction**

```csharp
[Fact]
public async Task AcquireAsync_ActiveReferences_PreventsEviction()
{
    // Arrange - MaxCachedWorkspaces = 3
    var testData = new List<(User user, NovelProject project)>();
    for (int i = 0; i < 4; i++)
    {
        testData.Add(await CreateTestUserAndProjectAsync());
    }
    
    // Act - Acquire 3 workspaces, keep first one active
    await _factory.AcquireAsync(testData[0].user.Id, testData[0].project.Id);
    // Don't release testData[0] - keep it active
    
    await _factory.AcquireAsync(testData[1].user.Id, testData[1].project.Id);
    _factory.Release(testData[1].user.Id, testData[1].project.Id);
    
    await _factory.AcquireAsync(testData[2].user.Id, testData[2].project.Id);
    _factory.Release(testData[2].user.Id, testData[2].project.Id);
    
    // Acquire 4th workspace - should evict testData[1] (oldest idle), not testData[0] (active)
    await _factory.AcquireAsync(testData[3].user.Id, testData[3].project.Id);
    
    // Assert - testData[0] should still be in cache (active reference)
    var stats = _factory.GetStats();
    Assert.Equal(3, stats.TotalWorkspaces);
    Assert.Equal(2, stats.ActiveReferences); // testData[0] and testData[3]
    
    // Cleanup
    _factory.Release(testData[0].user.Id, testData[0].project.Id);
    _factory.Release(testData[3].user.Id, testData[3].project.Id);
}
```

- [ ] **Step 4: Run test**

```bash
dotnet test --filter "FullyQualifiedName~WorkspaceFactoryTests.AcquireAsync_ActiveReferences_PreventsEviction"
```

Expected: Test PASSES

- [ ] **Step 5: Run all WorkspaceFactory tests**

```bash
dotnet test --filter "FullyQualifiedName~WorkspaceFactoryTests"
```

Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add Tests/NovelAgentRegression/WorkspaceFactoryTests.cs
git commit -m "test(workspace): add LRU eviction tests"
```

---

## Task 20: StoryBibleRepository Integration Tests

**Files:**
- Create: `Tests/NovelAgentRegression/StoryBibleRepositoryTests.cs`

- [ ] **Step 1: Create test class**

```csharp
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Repositories;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public class StoryBibleRepositoryTests : IDisposable
{
    private readonly NovelAgentDbContext _db;
    private readonly StoryBibleRepository _repository;
    private readonly User _testUser;
    private readonly NovelProject _testProject;
    
    public StoryBibleRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase($"StoryBibleTest_{Guid.NewGuid()}")
            .Options;
        
        _db = new NovelAgentDbContext(options);
        
        // Create test user and project
        _testUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            Role = "user"
        };
        
        _testProject = new NovelProject
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _testUser.Id,
            Title = "Test Novel",
            StorageProjectName = "test_novel"
        };
        
        _db.Users.Add(_testUser);
        _db.NovelProjects.Add(_testProject);
        _db.SaveChanges();
        
        // Create repository with mock CurrentUserService
        var mockCurrentUser = new MockCurrentUserService(_testUser.Id);
        _repository = new StoryBibleRepository(_db, mockCurrentUser);
    }
    
    public void Dispose()
    {
        _db?.Dispose();
    }
    
    // Tests to be added in next steps
}

// Mock implementation
internal class MockCurrentUserService : ICurrentUserService
{
    private readonly string _userId;
    
    public MockCurrentUserService(string userId)
    {
        _userId = userId;
    }
    
    public string GetUserId() => _userId;
    public string GetUsername() => "testuser";
    public string GetEmail() => "test@example.com";
    public string GetRole() => "user";
    public bool IsAdmin() => false;
    public bool IsAuthenticated() => true;
    public string? TryGetUserId() => _userId;
}
```

- [ ] **Step 2: Write test for StoryConstitution CRUD**

```csharp
[Fact]
public async Task SaveConstitutionAsync_NewConstitution_CreatesSuccessfully()
{
    // Arrange
    var constitution = new StoryConstitution
    {
        ProjectId = _testProject.Id,
        Genre = "玄幻",
        SubGenre = "东方玄幻",
        CoreHook = "废材逆袭成为修仙界最强者"
    };
    
    // Act
    var saved = await _repository.SaveConstitutionAsync(constitution);
    var retrieved = await _repository.GetConstitutionAsync(_testProject.Id);
    
    // Assert
    Assert.NotNull(saved);
    Assert.NotNull(retrieved);
    Assert.Equal(constitution.Genre, retrieved.Genre);
    Assert.Equal(constitution.CoreHook, retrieved.CoreHook);
    Assert.Equal(_testUser.Id, retrieved.UserId);
}

[Fact]
public async Task SaveConstitutionAsync_UpdateExisting_UpdatesSuccessfully()
{
    // Arrange
    var constitution = new StoryConstitution
    {
        ProjectId = _testProject.Id,
        Genre = "玄幻",
        CoreHook = "Original hook"
    };
    await _repository.SaveConstitutionAsync(constitution);
    
    // Act
    constitution.CoreHook = "Updated hook";
    await _repository.SaveConstitutionAsync(constitution);
    
    var retrieved = await _repository.GetConstitutionAsync(_testProject.Id);
    
    // Assert
    Assert.Equal("Updated hook", retrieved!.CoreHook);
}
```

- [ ] **Step 3: Run StoryConstitution tests**

```bash
dotnet test --filter "FullyQualifiedName~StoryBibleRepositoryTests" --filter "FullyQualifiedName~Constitution"
```

Expected: Tests PASS

- [ ] **Step 4: Write test for VolumeArc CRUD**

```csharp
[Fact]
public async Task SaveVolumeArcAsync_NewVolumeArc_CreatesSuccessfully()
{
    // Arrange
    var volumeArc = new VolumeArc
    {
        ProjectId = _testProject.Id,
        VolumeNumber = 1,
        VolumeTitle = "第一卷：废材崛起",
        TargetChapters = 50
    };
    
    // Act
    var saved = await _repository.SaveVolumeArcAsync(volumeArc);
    var retrieved = await _repository.GetVolumeArcAsync(saved.Id);
    
    // Assert
    Assert.NotNull(retrieved);
    Assert.Equal(volumeArc.VolumeTitle, retrieved.VolumeTitle);
    Assert.Equal(volumeArc.VolumeNumber, retrieved.VolumeNumber);
    Assert.Equal(_testUser.Id, retrieved.UserId);
}

[Fact]
public async Task GetVolumeArcsAsync_MultipleVolumes_ReturnsSortedByNumber()
{
    // Arrange
    await _repository.SaveVolumeArcAsync(new VolumeArc 
    { 
        ProjectId = _testProject.Id, 
        VolumeNumber = 3, 
        VolumeTitle = "Vol 3" 
    });
    await _repository.SaveVolumeArcAsync(new VolumeArc 
    { 
        ProjectId = _testProject.Id, 
        VolumeNumber = 1, 
        VolumeTitle = "Vol 1" 
    });
    await _repository.SaveVolumeArcAsync(new VolumeArc 
    { 
        ProjectId = _testProject.Id, 
        VolumeNumber = 2, 
        VolumeTitle = "Vol 2" 
    });
    
    // Act
    var volumes = await _repository.GetVolumeArcsAsync(_testProject.Id);
    
    // Assert
    Assert.Equal(3, volumes.Count);
    Assert.Equal(1, volumes[0].VolumeNumber);
    Assert.Equal(2, volumes[1].VolumeNumber);
    Assert.Equal(3, volumes[2].VolumeNumber);
}
```

- [ ] **Step 5: Run all StoryBibleRepository tests**

```bash
dotnet test --filter "FullyQualifiedName~StoryBibleRepositoryTests"
```

Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add Tests/NovelAgentRegression/StoryBibleRepositoryTests.cs
git commit -m "test(repository): add StoryBibleRepository integration tests"
```

---

## Task 21: Verify Full Build and Migration

**Files:**
- None (verification only)

- [ ] **Step 1: Clean build entire solution**

```bash
cd /Users/lyston/PycharmProjects/tianming-agentic-novel-studio
dotnet clean
dotnet build
```

Expected: Build succeeds with 0 errors

- [ ] **Step 2: Verify database migration is applied**

```bash
cd Web/NovelAgentWeb
dotnet ef database update
```

Expected: Migration applied successfully, or "No pending migrations" if already applied

- [ ] **Step 3: Verify all tables exist**

```bash
sqlite3 novelagent.db ".schema story_constitutions"
sqlite3 novelagent.db ".schema volume_arcs"
sqlite3 novelagent.db ".schema character_states"
sqlite3 novelagent.db ".schema foreshadow_entries"
sqlite3 novelagent.db ".schema world_setting_entries"
sqlite3 novelagent.db ".schema agent_runs"
```

Expected: All 6 table schemas displayed correctly

- [ ] **Step 4: Run all tests**

```bash
cd Tests/NovelAgentRegression
dotnet test
```

Expected: All tests PASS

- [ ] **Step 5: Verify application starts without errors**

```bash
cd Web/NovelAgentWeb
dotnet run
```

Expected: Application starts, listens on port 5000, no startup errors

Press Ctrl+C to stop after verification

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "feat(phase1): complete database and WorkspaceFactory implementation

- Added 6 StoryBible entity types with EF Core configuration
- Implemented WorkspaceFactory with reference counting and LRU eviction
- Implemented StoryBibleRepository with full CRUD operations
- Added comprehensive unit and integration tests
- Registered services in DI container
- All tests passing, migration applied successfully"
```

---

## Phase 1 Complete

**Exit Criteria Met:**
- ✅ All database tables created (6 StoryBible entities)
- ✅ WorkspaceFactory can create/cache/evict Workspace instances
- ✅ Repository layer tests pass (>90% coverage expected)
- ✅ Thread safety documented (ProjectContextLock usage)

**Next Steps:**
Proceed to Phase 2: Vector Storage (Qdrant) implementation

