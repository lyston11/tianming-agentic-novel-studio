# Data Migration Tool

This tool migrates existing JSON-based data to the new SQLite database with transaction protection and backup.

## Features

- Migrates projects from `projects.json`
- Extracts story bible data (characters, foreshadows, world settings, volumes)
- Imports chapter metadata from Markdown files
- Migrates user settings
- Imports agent memories and sessions
- Creates default admin user
- Transaction protection (all-or-nothing)
- Automatic backup before migration

## Usage

### Basic Usage

```bash
cd Scripts/Migration
dotnet run --project MigrationTool.csproj
```

### Verify Migration Results

```bash
cd Scripts/Migration
dotnet run --project VerificationTool.csproj
```

### Command Line Options

```
-h, --help              Show help message
-d, --database <path>   Database file path (default: App_Data/novel_agent.db)
-a, --appdata <path>    App_Data directory path (default: Web/NovelAgentWeb/App_Data)
--no-backup             Skip creating backup before migration
--force                 Force migration even if database already has data
-y, --yes               Skip confirmation prompt
```

### Examples

```bash
# Run with default settings (will prompt for confirmation)
dotnet run --project MigrationTool.csproj

# Specify custom paths
dotnet run --project MigrationTool.csproj -- -a /path/to/App_Data -d /path/to/database.db

# Force migration without backup (use with caution)
dotnet run --project MigrationTool.csproj -- --force --no-backup -y

# Run from project root
dotnet run --project Scripts/Migration/MigrationTool.csproj
```

## Default Admin Credentials

After successful migration, a default admin user is created:

- **Username:** `admin`
- **Password:** `admin123`

⚠️ **Important:** Change the admin password immediately after first login!

## Data Sources

The migration tool reads from the following locations:

- `App_Data/Projects/AgenticNovelStudio/NovelProjects/projects.json` - Project list
- `App_Data/Projects/{ProjectName}/Services/Framework/AI/NovelAgent/story_bible.json` - Story bible
- `App_Data/Projects/{ProjectName}/Chapters/*.md` - Chapter content files
- `App_Data/Projects/AgenticNovelStudio/Settings/user_settings.json` - User settings
- `App_Data/Projects/{ProjectName}/Agent/*.json` - Agent memories
- `App_Data/Projects/{ProjectName}/Agent/sessions.json` - Agent sessions

## Backup Location

Backups are created at: `App_Data/Backup/{timestamp}/`

Format: `yyyyMMdd_HHmmss` (e.g., `20260608_143025`)

## Migration Results

After completion, the tool displays:

- Number of users created
- Number of projects migrated
- Number of volumes created
- Number of chapters imported
- Number of characters migrated
- Number of foreshadows migrated
- Number of world settings migrated
- Number of agent memories imported
- Number of agent sessions imported
- User settings migration status
- Total duration

## Error Handling

- **Transaction Protection:** If any step fails, all changes are rolled back
- **Validation:** Checks if database already contains data (use `--force` to override)
- **Backup:** Original files are backed up before migration (disable with `--no-backup`)
- **Logging:** Detailed logs are written to console

## Idempotency

The migration tool checks if data already exists in the database. To re-run migration:

```bash
# Delete the database file and run again
rm Web/NovelAgentWeb/App_Data/novel_agent.db
dotnet run --project Scripts/Migration/MigrationTool.csproj

# OR use --force to override
dotnet run --project Scripts/Migration/MigrationTool.csproj --force
```

## Build

```bash
cd Scripts/Migration
dotnet build MigrationTool.csproj
dotnet build VerificationTool.csproj
```

## Troubleshooting

### "Database already contains data"

Use `--force` flag to override:
```bash
dotnet run --force
```

### Missing source files

Ensure the App_Data directory structure exists:
```
App_Data/
  Projects/
    AgenticNovelStudio/
      NovelProjects/
        projects.json
      Settings/
        user_settings.json
      Agent/
        sessions.json
```

### Database locked error

Close any applications accessing the database file and try again.

### BCrypt.Net package missing

Restore packages:
```bash
dotnet restore
```

## Next Steps

After successful migration:

1. Verify data in database using SQLite browser or EF Core queries
2. Test foreign key relationships
3. Compare record counts with source JSON files
4. Change admin password on first login
5. Proceed with Task 1.5: Vector migration to Qdrant
