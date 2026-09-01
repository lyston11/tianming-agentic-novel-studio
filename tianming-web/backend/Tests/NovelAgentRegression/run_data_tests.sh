#!/bin/bash
# Standalone test runner for data access layer tests
# Run this script to test the data layer in isolation

echo "Building standalone data layer test project..."

# Create temporary test project
TEMP_DIR=$(mktemp -d)
cd "$TEMP_DIR"

cat > DataLayerTests.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.4.2" />
    <PackageReference Include="xunit.runner.console" Version="2.4.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../Web/NovelAgentWeb/NovelAgentWeb.csproj" />
  </ItemGroup>
</Project>
EOF

# Copy test files
cp "$PROJECT_ROOT/Tests/NovelAgentRegression/Helpers/TestDbContextFactory.cs" .
cp "$PROJECT_ROOT/Tests/NovelAgentRegression/Data/UserRepositoryTests.cs" .
cp "$PROJECT_ROOT/Tests/NovelAgentRegression/Data/ProjectRepositoryTests.cs" .
cp "$PROJECT_ROOT/Tests/NovelAgentRegression/Data/ForeshadowRepositoryTests.cs" .

# Build and run tests
dotnet test --verbosity normal

# Cleanup
cd -
rm -rf "$TEMP_DIR"
