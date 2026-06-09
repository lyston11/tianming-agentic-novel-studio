using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Scripts.Migration;

namespace TM.Scripts.Migration;

/// <summary>
/// Console application for vector migration from JSON to Qdrant
/// </summary>
class VectorMigrationTool
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Vector Migration Tool: JSON to Qdrant ===\n");

        // Parse command line arguments
        var options = ParseArguments(args);

        if (options.ShowHelp)
        {
            ShowHelp();
            return 0;
        }

        try
        {
            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services, options);

            var serviceProvider = services.BuildServiceProvider();

            // Run migration
            var migrationService = serviceProvider.GetRequiredService<VectorMigrationService>();
            var result = await migrationService.MigrateAsync(
                createBackup: options.CreateBackup,
                force: options.Force);

            // Display results
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine(result);
            Console.WriteLine(new string('=', 60));

            // Test similarity search if requested
            if (options.TestSearch && result.Success && result.TotalVectorsImported > 0)
            {
                Console.WriteLine("\nTesting similarity search...");
                var firstProjectId = result.ProjectStats.Keys.FirstOrDefault();
                if (firstProjectId != null)
                {
                    var testVector = GenerateRandomVector(512);
                    var searchSuccess = await migrationService.TestSimilaritySearchAsync(firstProjectId, testVector);
                    Console.WriteLine($"Similarity search test: {(searchSuccess ? "PASSED" : "FAILED")}");
                }
            }

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nERROR: {ex.Message}");
            Console.WriteLine($"\nStack Trace:\n{ex.StackTrace}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void ConfigureServices(ServiceCollection services, VectorMigrationOptions options)
    {
        // Configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{options.Environment}.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
        });

        // Database context
        var connectionString = configuration.GetConnectionString("NovelAgentDb")
            ?? "Data Source=../Web/NovelAgentWeb/App_Data/Database/novelagent.db";

        services.AddDbContext<NovelAgentDbContext>(optionsBuilder =>
        {
            optionsBuilder.UseSqlite(connectionString);
        });

        // Vector store
        services.AddSingleton<IVectorStore, QdrantVectorStore>();

        // Migration service
        var appDataPath = options.AppDataPath
            ?? Path.GetFullPath("../Web/NovelAgentWeb/App_Data");

        var vectorDimension = configuration.GetValue<int>("Qdrant:VectorDimension", 512);
        var batchSize = configuration.GetValue<int>("Qdrant:BatchSize", 100);

        services.AddSingleton(sp => new VectorMigrationService(
            sp.GetRequiredService<NovelAgentDbContext>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetRequiredService<ILogger<VectorMigrationService>>(),
            appDataPath,
            vectorDimension,
            batchSize
        ));
    }

    private static VectorMigrationOptions ParseArguments(string[] args)
    {
        var options = new VectorMigrationOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;

                case "--force":
                case "-f":
                    options.Force = true;
                    break;

                case "--no-backup":
                    options.CreateBackup = false;
                    break;

                case "--verbose":
                case "-v":
                    options.Verbose = true;
                    break;

                case "--test-search":
                    options.TestSearch = true;
                    break;

                case "--environment":
                case "-e":
                    if (i + 1 < args.Length)
                    {
                        options.Environment = args[++i];
                    }
                    break;

                case "--app-data-path":
                    if (i + 1 < args.Length)
                    {
                        options.AppDataPath = args[++i];
                    }
                    break;
            }
        }

        return options;
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Vector Migration Tool - Migrate file-based vector embeddings to Qdrant

USAGE:
    dotnet run [OPTIONS]

OPTIONS:
    --help, -h              Show this help message
    --force, -f             Force migration even if collections already exist
    --no-backup             Skip creating backup of original vector files
    --verbose, -v           Enable verbose logging
    --test-search           Run similarity search test after migration
    --environment, -e ENV   Specify environment (Development, Production)
    --app-data-path PATH    Override App_Data path

EXAMPLES:
    # Standard migration with backup
    dotnet run

    # Force migration without backup
    dotnet run --force --no-backup

    # Verbose output with search test
    dotnet run --verbose --test-search

    # Custom app data path
    dotnet run --app-data-path /path/to/App_Data

DESCRIPTION:
    This tool migrates vector embeddings from JSON files to Qdrant:

    Source Files:
        - Config/guides/chapter_embeddings.json
        - Config/guides/chunk_embeddings.json

    Target:
        - Qdrant collections (project_{project_id})

    Features:
        - Automatic backup to App_Data/Backup/{timestamp}/VectorIndexes/
        - Chapter ID mapping from legacy to new database GUIDs
        - Batch insertion (100 vectors per batch)
        - Payload indexing (user_id, project_id, source_type)
        - Verification of vector counts
        - Similarity search testing

    Prerequisites:
        1. SQLite database initialized (Task 1.1)
        2. Qdrant running at localhost:6333 (Task 1.2)
        3. Data migrated to SQLite (Task 1.4)
        4. Vector embedding JSON files exist

EXIT CODES:
    0 - Success
    1 - Failure
");
    }

    private static float[] GenerateRandomVector(int dimension)
    {
        var random = new Random();
        var vector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = (float)random.NextDouble();
        }
        return vector;
    }
}

class VectorMigrationOptions
{
    public bool ShowHelp { get; set; }
    public bool Force { get; set; }
    public bool CreateBackup { get; set; } = true;
    public bool Verbose { get; set; }
    public bool TestSearch { get; set; }
    public string Environment { get; set; } = "Development";
    public string? AppDataPath { get; set; }
}
