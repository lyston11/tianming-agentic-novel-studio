using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Scripts.Migration;

/// <summary>
/// Verification tool to check migration results and data integrity
/// </summary>
class VerificationTool
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Migration Verification Tool ===");
        Console.WriteLine();

        var databasePath = args.Length > 0
            ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "../../Web/NovelAgentWeb/App_Data/novel_agent.db");

        if (!File.Exists(databasePath))
        {
            Console.WriteLine($"✗ Database file not found: {databasePath}");
            return 1;
        }

        try
        {
            var services = ConfigureServices(databasePath);
            var serviceProvider = services.BuildServiceProvider();
            var dbContext = serviceProvider.GetRequiredService<NovelAgentDbContext>();

            Console.WriteLine($"Database: {databasePath}");
            Console.WriteLine();

            // Verify tables exist and have data
            Console.WriteLine("=== Record Counts ===");
            var users = await dbContext.Users.CountAsync();
            var projects = await dbContext.NovelProjects.CountAsync();
            var volumes = await dbContext.Volumes.CountAsync();
            var chapters = await dbContext.Chapters.CountAsync();
            var characters = await dbContext.Characters.CountAsync();
            var foreshadows = await dbContext.Foreshadows.CountAsync();
            var worldSettings = await dbContext.WorldSettings.CountAsync();
            var userSettings = await dbContext.UserSettings.CountAsync();
            var agentMemories = await dbContext.AgentMemories.CountAsync();
            var agentSessions = await dbContext.AgentSessions.CountAsync();

            Console.WriteLine($"Users:          {users}");
            Console.WriteLine($"Projects:       {projects}");
            Console.WriteLine($"Volumes:        {volumes}");
            Console.WriteLine($"Chapters:       {chapters}");
            Console.WriteLine($"Characters:     {characters}");
            Console.WriteLine($"Foreshadows:    {foreshadows}");
            Console.WriteLine($"World Settings: {worldSettings}");
            Console.WriteLine($"User Settings:  {userSettings}");
            Console.WriteLine($"Agent Memories: {agentMemories}");
            Console.WriteLine($"Agent Sessions: {agentSessions}");
            Console.WriteLine();

            // Check foreign key integrity
            Console.WriteLine("=== Foreign Key Integrity ===");
            var orphanedProjects = await dbContext.NovelProjects
                .Where(p => !dbContext.Users.Any(u => u.Id == p.UserId))
                .CountAsync();
            Console.WriteLine($"Orphaned Projects: {orphanedProjects} {(orphanedProjects == 0 ? "✓" : "✗")}");

            var orphanedChapters = await dbContext.Chapters
                .Where(c => !dbContext.NovelProjects.Any(p => p.Id == c.ProjectId))
                .CountAsync();
            Console.WriteLine($"Orphaned Chapters: {orphanedChapters} {(orphanedChapters == 0 ? "✓" : "✗")}");

            var orphanedCharacters = await dbContext.Characters
                .Where(c => !dbContext.NovelProjects.Any(p => p.Id == c.ProjectId))
                .CountAsync();
            Console.WriteLine($"Orphaned Characters: {orphanedCharacters} {(orphanedCharacters == 0 ? "✓" : "✗")}");

            var orphanedForeshadows = await dbContext.Foreshadows
                .Where(f => !dbContext.NovelProjects.Any(p => p.Id == f.ProjectId))
                .CountAsync();
            Console.WriteLine($"Orphaned Foreshadows: {orphanedForeshadows} {(orphanedForeshadows == 0 ? "✓" : "✗")}");
            Console.WriteLine();

            // Check admin user
            Console.WriteLine("=== Admin User ===");
            var adminUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == "admin");
            if (adminUser != null)
            {
                Console.WriteLine($"✓ Admin user exists");
                Console.WriteLine($"  ID: {adminUser.Id}");
                Console.WriteLine($"  Email: {adminUser.Email}");
                Console.WriteLine($"  Role: {adminUser.Role}");
                Console.WriteLine($"  Active: {adminUser.IsActive}");
                Console.WriteLine($"  Created: {adminUser.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                Console.WriteLine("✗ Admin user not found");
            }
            Console.WriteLine();

            // List projects
            Console.WriteLine("=== Projects ===");
            var projectList = await dbContext.NovelProjects
                .Include(p => p.User)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync();

            foreach (var project in projectList)
            {
                var chapterCount = await dbContext.Chapters.CountAsync(c => c.ProjectId == project.Id);
                var characterCount = await dbContext.Characters.CountAsync(c => c.ProjectId == project.Id);

                Console.WriteLine($"- {project.Title} ({project.Genre})");
                Console.WriteLine($"  ID: {project.Id}");
                Console.WriteLine($"  Owner: {project.User.Username}");
                Console.WriteLine($"  Status: {project.Status}");
                Console.WriteLine($"  Storage: {project.StorageProjectName}");
                Console.WriteLine($"  Chapters: {chapterCount}");
                Console.WriteLine($"  Characters: {characterCount}");
                Console.WriteLine();
            }

            // Summary
            Console.WriteLine("=== Summary ===");
            var hasData = users > 0 || projects > 0;
            var hasIntegrityIssues = orphanedProjects > 0 || orphanedChapters > 0
                                     || orphanedCharacters > 0 || orphanedForeshadows > 0;

            if (!hasData)
            {
                Console.WriteLine("⚠️  Database is empty. Run migration first.");
                return 1;
            }

            if (hasIntegrityIssues)
            {
                Console.WriteLine("✗ Database has foreign key integrity issues!");
                return 1;
            }

            Console.WriteLine("✓ Verification passed! Database is healthy.");
            return 0;
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

    private static ServiceCollection ConfigureServices(string databasePath)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddDbContext<NovelAgentDbContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}");
        });

        return services;
    }
}
