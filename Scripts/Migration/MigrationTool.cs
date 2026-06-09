using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Data;
using TM.Scripts.Migration;

namespace TM.Scripts.Migration;

/// <summary>
/// Console application to execute data migration from JSON to SQLite
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Novel Agent Data Migration Tool ===");
        Console.WriteLine();

        // Parse command line arguments
        var options = ParseArguments(args);

        if (options.ShowHelp)
        {
            ShowHelp();
            return 0;
        }

        try
        {
            // Setup services
            var services = ConfigureServices(options);
            var serviceProvider = services.BuildServiceProvider();

            // Get migration service
            var migrationService = serviceProvider.GetRequiredService<DataMigrationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Confirm before proceeding
            if (!options.NoConfirm)
            {
                Console.WriteLine($"Database: {options.DatabasePath}");
                Console.WriteLine($"App Data: {options.AppDataPath}");
                Console.WriteLine($"Backup: {(options.NoBackup ? "No" : "Yes")}");
                Console.WriteLine($"Force: {(options.Force ? "Yes" : "No")}");
                Console.WriteLine();
                Console.Write("Proceed with migration? (y/n): ");

                var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (confirm != "y" && confirm != "yes")
                {
                    Console.WriteLine("Migration cancelled.");
                    return 0;
                }
            }

            Console.WriteLine();
            Console.WriteLine("Starting migration...");
            Console.WriteLine();

            // Run migration
            var result = await migrationService.MigrateAsync(
                createBackup: !options.NoBackup,
                force: options.Force);

            // Display results
            Console.WriteLine();
            Console.WriteLine(result.ToString());
            Console.WriteLine();

            if (result.Success)
            {
                Console.WriteLine("✓ Migration completed successfully!");
                Console.WriteLine();
                Console.WriteLine("Default admin credentials:");
                Console.WriteLine("  Username: admin");
                Console.WriteLine("  Password: admin123");
                Console.WriteLine();
                Console.WriteLine("⚠️  Please change the admin password on first login!");
                return 0;
            }
            else
            {
                Console.WriteLine("✗ Migration failed!");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack trace:");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static MigrationOptions ParseArguments(string[] args)
    {
        var options = new MigrationOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;

                case "-d":
                case "--database":
                    if (i + 1 < args.Length)
                    {
                        options.DatabasePath = args[++i];
                    }
                    break;

                case "-a":
                case "--appdata":
                    if (i + 1 < args.Length)
                    {
                        options.AppDataPath = args[++i];
                    }
                    break;

                case "--no-backup":
                    options.NoBackup = true;
                    break;

                case "--force":
                    options.Force = true;
                    break;

                case "-y":
                case "--yes":
                    options.NoConfirm = true;
                    break;
            }
        }

        return options;
    }

    private static ServiceCollection ConfigureServices(MigrationOptions options)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Database context
        var databasePath = options.DatabasePath ??
            Path.Combine(options.AppDataPath, "novel_agent.db");

        services.AddDbContext<NovelAgentDbContext>(optionsBuilder =>
        {
            optionsBuilder.UseSqlite($"Data Source={databasePath}");
        });

        // Migration service
        services.AddScoped<DataMigrationService>(sp =>
        {
            var dbContext = sp.GetRequiredService<NovelAgentDbContext>();
            var logger = sp.GetRequiredService<ILogger<DataMigrationService>>();
            return new DataMigrationService(dbContext, logger, options.AppDataPath);
        });

        return services;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: MigrationTool [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help              Show this help message");
        Console.WriteLine("  -d, --database <path>   Database file path (default: App_Data/novel_agent.db)");
        Console.WriteLine("  -a, --appdata <path>    App_Data directory path (default: Web/NovelAgentWeb/App_Data)");
        Console.WriteLine("  --no-backup             Skip creating backup before migration");
        Console.WriteLine("  --force                 Force migration even if database already has data");
        Console.WriteLine("  -y, --yes               Skip confirmation prompt");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  MigrationTool");
        Console.WriteLine("  MigrationTool -a /path/to/App_Data");
        Console.WriteLine("  MigrationTool --force --no-backup -y");
    }
}

internal class MigrationOptions
{
    public string AppDataPath { get; set; } =
        Path.Combine(Directory.GetCurrentDirectory(), "Web/NovelAgentWeb/App_Data");

    public string? DatabasePath { get; set; }
    public bool NoBackup { get; set; }
    public bool Force { get; set; }
    public bool NoConfirm { get; set; }
    public bool ShowHelp { get; set; }
}
