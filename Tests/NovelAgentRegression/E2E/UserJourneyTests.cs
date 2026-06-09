using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Auth;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Models.Projects;
using Xunit;

namespace TM.Tests.NovelAgentRegression.E2E;

/// <summary>
/// End-to-end tests that verify complete user journeys through the system.
/// Tests the full stack from authentication through project creation, chapter management, and data isolation.
/// </summary>
public class UserJourneyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserJourneyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Journey 1: Complete user flow from registration to semantic search.
    /// Tests: Register → Login → Create Project → Create Chapter → Verify Database State.
    /// </summary>
    [Fact]
    public async Task Journey1_RegisterToChapterCreation_FullWorkflow()
    {
        // Step 1: Register a new user
        var registerRequest = new RegisterRequest
        {
            Username = "journey1user",
            Email = "journey1@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.Equal("journey1user", authResponse.Username);
        Assert.NotEmpty(authResponse.Token);
        Assert.NotEmpty(authResponse.UserId);

        // Verify user and user settings were created in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var user = await db.Users.FindAsync(authResponse.UserId);
            Assert.NotNull(user);
            Assert.Equal("journey1user", user.Username);
            Assert.Equal("journey1@test.com", user.Email);
            Assert.Equal("User", user.Role);

            var userSettings = await db.UserSettings.FindAsync(authResponse.UserId);
            Assert.NotNull(userSettings);
            Assert.Equal(0.7f, userSettings.LlmTemperature);
        }

        // Step 2: Login with the new user
        var loginRequest = new LoginRequest
        {
            EmailOrUsername = "journey1user",
            Password = "SecurePass123!"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginAuthResponse = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginAuthResponse);
        Assert.NotEmpty(loginAuthResponse.Token);
        Assert.Equal(authResponse.UserId, loginAuthResponse.UserId);

        var token = loginAuthResponse.Token;

        // Step 3: Create a project with JWT authentication
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createProjectRequest = new CreateProjectRequest
        {
            Title = "Journey 1 Novel",
            Genre = "Fantasy",
            SubGenre = "Epic Fantasy",
            CoreHook = "A hero's journey to save the world"
        };

        var createProjectResponse = await _client.PostAsJsonAsync("/api/project", createProjectRequest);
        Assert.Equal(HttpStatusCode.Created, createProjectResponse.StatusCode);

        var projectResponse = await createProjectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(projectResponse);
        Assert.Equal("Journey 1 Novel", projectResponse.Title);
        Assert.Equal("Fantasy", projectResponse.Genre);
        Assert.NotEmpty(projectResponse.Id);

        // Verify project was created with correct userId
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var project = await db.NovelProjects.FindAsync(projectResponse.Id);
            Assert.NotNull(project);
            Assert.Equal(authResponse.UserId, project.UserId);
            Assert.Equal("Journey 1 Novel", project.Title);
            Assert.NotEmpty(project.StorageProjectName);
        }

        // Step 4: Create a chapter in the project
        var createChapterRequest = new CreateChapterRequest
        {
            ProjectId = projectResponse.Id,
            Title = "The Beginning",
            ChapterNumber = 1,
            Content = "In the beginning, there was a world full of magic and mystery. The hero awakens to find his destiny."
        };

        var createChapterResponse = await _client.PostAsJsonAsync("/api/chapters", createChapterRequest);
        Assert.Equal(HttpStatusCode.Created, createChapterResponse.StatusCode);

        var chapterResponse = await createChapterResponse.Content.ReadFromJsonAsync<ChapterResponse>();
        Assert.NotNull(chapterResponse);
        Assert.Equal("The Beginning", chapterResponse.Title);
        Assert.Equal(1, chapterResponse.ChapterNumber);
        Assert.NotEmpty(chapterResponse.Id);

        // Verify chapter was created in database with correct metadata
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var chapter = await db.Chapters.FindAsync(chapterResponse.Id);
            Assert.NotNull(chapter);
            Assert.Equal(projectResponse.Id, chapter.ProjectId);
            Assert.Equal("The Beginning", chapter.Title);
            Assert.Equal(1, chapter.ChapterNumber);
            Assert.NotEmpty(chapter.ContentPath);
            Assert.True(chapter.WordCount > 0);
        }

        // Step 5: Retrieve the chapter and verify content
        var getChapterResponse = await _client.GetAsync($"/api/chapters/{chapterResponse.Id}");
        Assert.Equal(HttpStatusCode.OK, getChapterResponse.StatusCode);

        var retrievedChapter = await getChapterResponse.Content.ReadFromJsonAsync<ChapterResponse>();
        Assert.NotNull(retrievedChapter);
        Assert.Equal("The Beginning", retrievedChapter.Title);
        Assert.Contains("magic and mystery", retrievedChapter.Content);

        // Step 6: Get all chapters for the project
        var getChaptersResponse = await _client.GetAsync($"/api/chapters/project/{projectResponse.Id}");
        Assert.Equal(HttpStatusCode.OK, getChaptersResponse.StatusCode);

        var chapters = await getChaptersResponse.Content.ReadFromJsonAsync<List<ChapterResponse>>();
        Assert.NotNull(chapters);
        Assert.Single(chapters);
        Assert.Equal("The Beginning", chapters[0].Title);
    }

    /// <summary>
    /// Journey 2: Chapter deletion cascades to foreshadow references.
    /// Tests: Create Chapter with Foreshadow → Delete Chapter → Verify Foreshadow FK is NULL.
    /// </summary>
    [Fact]
    public async Task Journey2_DeleteChapter_ClearsForeshadowReferences()
    {
        // Setup: Register user and login
        var registerRequest = new RegisterRequest
        {
            Username = "journey2user",
            Email = "journey2@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        var authResponse = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);

        var token = authResponse.Token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create project
        var createProjectRequest = new CreateProjectRequest
        {
            Title = "Journey 2 Novel",
            Genre = "Mystery"
        };

        var projectResponse = await _client.PostAsJsonAsync("/api/project", createProjectRequest);
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);

        // Create chapter
        var createChapterRequest = new CreateChapterRequest
        {
            ProjectId = project.Id,
            Title = "Chapter with Foreshadow",
            ChapterNumber = 1,
            Content = "A mysterious clue is revealed."
        };

        var chapterResponse = await _client.PostAsJsonAsync("/api/chapters", createChapterRequest);
        var chapter = await chapterResponse.Content.ReadFromJsonAsync<ChapterResponse>();
        Assert.NotNull(chapter);

        // Manually create a foreshadow that references this chapter
        string foreshadowId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var foreshadow = new Foreshadow
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = project.Id,
                Name = "The Great Mystery",
                Type = "plot",
                Status = "setup",
                SetupChapterId = chapter.Id,
                Importance = 5,
                Description = "A mystery that will be resolved later"
            };
            db.Foreshadows.Add(foreshadow);
            await db.SaveChangesAsync();
            foreshadowId = foreshadow.Id;
        }

        // Verify foreshadow references the chapter
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var foreshadow = await db.Foreshadows.FindAsync(foreshadowId);
            Assert.NotNull(foreshadow);
            Assert.Equal(chapter.Id, foreshadow.SetupChapterId);
        }

        // Delete the chapter
        var deleteResponse = await _client.DeleteAsync($"/api/chapters/{chapter.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify foreshadow still exists but SetupChapterId is NULL
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var foreshadow = await db.Foreshadows.FindAsync(foreshadowId);
            Assert.NotNull(foreshadow);
            Assert.Null(foreshadow.SetupChapterId);
            Assert.Equal("The Great Mystery", foreshadow.Name);
        }

        // Verify chapter is deleted from database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var deletedChapter = await db.Chapters.FindAsync(chapter.Id);
            Assert.Null(deletedChapter);
        }
    }

    /// <summary>
    /// Journey 3: Admin can access other users' projects, regular users cannot.
    /// Tests: Create User A with project → Login as Admin → Admin accesses User A's project.
    /// Also tests: Regular User B cannot access User A's project.
    /// </summary>
    [Fact]
    public async Task Journey3_AdminAccess_CanViewOtherUsersProjects()
    {
        // Step 1: Create User A and their project
        var userARegister = new RegisterRequest
        {
            Username = "userA",
            Email = "userA@test.com",
            Password = "SecurePass123!"
        };

        var userARegisterResponse = await _client.PostAsJsonAsync("/api/auth/register", userARegister);
        var userAAuth = await userARegisterResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(userAAuth);

        var userAClient = _factory.CreateClient();
        userAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAAuth.Token);

        var userAProjectRequest = new CreateProjectRequest
        {
            Title = "User A's Secret Novel",
            Genre = "Thriller"
        };

        var userAProjectResponse = await userAClient.PostAsJsonAsync("/api/project", userAProjectRequest);
        var userAProject = await userAProjectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(userAProject);

        // Step 2: Create User B (regular user)
        var userBRegister = new RegisterRequest
        {
            Username = "userB",
            Email = "userB@test.com",
            Password = "SecurePass123!"
        };

        var userBRegisterResponse = await _client.PostAsJsonAsync("/api/auth/register", userBRegister);
        var userBAuth = await userBRegisterResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(userBAuth);

        var userBClient = _factory.CreateClient();
        userBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBAuth.Token);

        // Step 3: User B tries to access User A's project - should be FORBIDDEN
        var userBAccessResponse = await userBClient.GetAsync($"/api/project/{userAProject.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, userBAccessResponse.StatusCode);

        // Step 4: Create an Admin user directly in the database
        string adminUserId;
        string adminToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

            // Create admin user with hashed password
            var adminUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "admin",
                Email = "admin@test.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPass123!"),
                Role = "Admin",
                IsActive = true
            };
            db.Users.Add(adminUser);

            var adminSettings = new UserSettings
            {
                UserId = adminUser.Id
            };
            db.UserSettings.Add(adminSettings);

            await db.SaveChangesAsync();
            adminUserId = adminUser.Id;
        }

        // Login as admin
        var adminLoginRequest = new LoginRequest
        {
            EmailOrUsername = "admin",
            Password = "AdminPass123!"
        };

        var adminLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", adminLoginRequest);
        Assert.Equal(HttpStatusCode.OK, adminLoginResponse.StatusCode);

        var adminAuth = await adminLoginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(adminAuth);
        Assert.Equal("Admin", adminAuth.Role);
        adminToken = adminAuth.Token;

        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Step 5: Admin accesses User A's project - should SUCCEED
        var adminAccessResponse = await adminClient.GetAsync($"/api/project/{userAProject.Id}");
        Assert.Equal(HttpStatusCode.OK, adminAccessResponse.StatusCode);

        var adminAccessedProject = await adminAccessResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(adminAccessedProject);
        Assert.Equal("User A's Secret Novel", adminAccessedProject.Title);
        Assert.Equal(userAProject.Id, adminAccessedProject.Id);

        // Step 6: Admin lists all projects - should see User A's project
        var adminListResponse = await adminClient.GetAsync("/api/project");
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);

        var adminProjects = await adminListResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = adminProjects.GetProperty("items").EnumerateArray().ToList();

        // Admin should see User A's project in the list
        var userAProjectInList = items.FirstOrDefault(p =>
            p.GetProperty("id").GetString() == userAProject.Id);
        Assert.NotEqual(default(JsonElement), userAProjectInList);
    }

    /// <summary>
    /// Journey 4: User isolation - users can only see their own projects.
    /// Tests: User A creates project → User B cannot see it in their project list.
    /// </summary>
    [Fact]
    public async Task Journey4_UserIsolation_OnlySeesOwnProjects()
    {
        // Create User A
        var userARegister = new RegisterRequest
        {
            Username = "isolationUserA",
            Email = "isolationA@test.com",
            Password = "SecurePass123!"
        };

        var userAResponse = await _client.PostAsJsonAsync("/api/auth/register", userARegister);
        var userAAuth = await userAResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(userAAuth);

        var userAClient = _factory.CreateClient();
        userAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAAuth.Token);

        // User A creates 2 projects
        for (int i = 1; i <= 2; i++)
        {
            var projectRequest = new CreateProjectRequest
            {
                Title = $"User A Project {i}",
                Genre = "Fantasy"
            };
            var projectResponse = await userAClient.PostAsJsonAsync("/api/project", projectRequest);
            Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        }

        // Create User B
        var userBRegister = new RegisterRequest
        {
            Username = "isolationUserB",
            Email = "isolationB@test.com",
            Password = "SecurePass123!"
        };

        var userBResponse = await _client.PostAsJsonAsync("/api/auth/register", userBRegister);
        var userBAuth = await userBResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(userBAuth);

        var userBClient = _factory.CreateClient();
        userBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBAuth.Token);

        // User B creates 1 project
        var userBProjectRequest = new CreateProjectRequest
        {
            Title = "User B Project 1",
            Genre = "Sci-Fi"
        };
        var userBProjectResponse = await userBClient.PostAsJsonAsync("/api/project", userBProjectRequest);
        Assert.Equal(HttpStatusCode.Created, userBProjectResponse.StatusCode);

        // User A lists their projects - should see only their 2 projects
        var userAListResponse = await userAClient.GetAsync("/api/project");
        Assert.Equal(HttpStatusCode.OK, userAListResponse.StatusCode);

        var userAProjects = await userAListResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userAItems = userAProjects.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, userAItems.Count);
        Assert.All(userAItems, p => Assert.StartsWith("User A Project", p.GetProperty("title").GetString()!));

        // User B lists their projects - should see only their 1 project
        var userBListResponse = await userBClient.GetAsync("/api/project");
        Assert.Equal(HttpStatusCode.OK, userBListResponse.StatusCode);

        var userBProjects = await userBListResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userBItems = userBProjects.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(userBItems);
        Assert.Equal("User B Project 1", userBItems[0].GetProperty("title").GetString());
    }

    /// <summary>
    /// Journey 5: Update chapter and verify content synchronization.
    /// Tests: Create Chapter → Update Content → Verify File System and Database Updated.
    /// </summary>
    [Fact]
    public async Task Journey5_UpdateChapter_ContentSynchronization()
    {
        // Setup user and project
        var register = new RegisterRequest
        {
            Username = "journey5user",
            Email = "journey5@test.com",
            Password = "SecurePass123!"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", register);
        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var projectRequest = new CreateProjectRequest
        {
            Title = "Journey 5 Novel",
            Genre = "Drama"
        };

        var projectResponse = await _client.PostAsJsonAsync("/api/project", projectRequest);
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);

        // Create chapter
        var createChapterRequest = new CreateChapterRequest
        {
            ProjectId = project.Id,
            Title = "Original Title",
            ChapterNumber = 1,
            Content = "Original content goes here."
        };

        var createResponse = await _client.PostAsJsonAsync("/api/chapters", createChapterRequest);
        var chapter = await createResponse.Content.ReadFromJsonAsync<ChapterResponse>();
        Assert.NotNull(chapter);

        // Update chapter with new content
        var updateRequest = new UpdateChapterRequest
        {
            Title = "Updated Title",
            Content = "This is the completely rewritten content with much more detail and information."
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/chapters/{chapter.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedChapter = await updateResponse.Content.ReadFromJsonAsync<ChapterResponse>();
        Assert.NotNull(updatedChapter);
        Assert.Equal("Updated Title", updatedChapter.Title);
        Assert.Contains("completely rewritten", updatedChapter.Content);

        // Verify database was updated
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            var dbChapter = await db.Chapters.FindAsync(chapter.Id);
            Assert.NotNull(dbChapter);
            Assert.Equal("Updated Title", dbChapter.Title);
            Assert.True(dbChapter.WordCount > 0);
        }

        // Get chapter again to verify persistence
        var getResponse = await _client.GetAsync($"/api/chapters/{chapter.Id}");
        var retrievedChapter = await getResponse.Content.ReadFromJsonAsync<ChapterResponse>();
        Assert.NotNull(retrievedChapter);
        Assert.Equal("Updated Title", retrievedChapter.Title);
        Assert.Contains("completely rewritten", retrievedChapter.Content);
    }

    /// <summary>
    /// Journey 6: Unauthorized access without JWT token.
    /// Tests: Access protected endpoints without authentication → Should return 401.
    /// </summary>
    [Fact]
    public async Task Journey6_UnauthorizedAccess_Returns401()
    {
        // Create a client without authentication header
        var unauthClient = _factory.CreateClient();

        // Try to list projects - should return 401
        var listResponse = await unauthClient.GetAsync("/api/project");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        // Try to create project - should return 401
        var createRequest = new CreateProjectRequest
        {
            Title = "Unauthorized Project",
            Genre = "Fantasy"
        };

        var createResponse = await unauthClient.PostAsJsonAsync("/api/project", createRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

        // Try to access a specific project - should return 401
        var getResponse = await unauthClient.GetAsync("/api/project/some-id");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        // Try to create chapter - should return 401
        var chapterRequest = new CreateChapterRequest
        {
            ProjectId = "some-id",
            Title = "Chapter",
            ChapterNumber = 1,
            Content = "Content"
        };

        var chapterResponse = await unauthClient.PostAsJsonAsync("/api/chapters", chapterRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, chapterResponse.StatusCode);
    }
}
