#!/bin/bash

# Vector Migration Tool - Run Script
# Task 1.5: Data Migration Script - Vectors to Qdrant

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MIGRATION_DIR="$SCRIPT_DIR"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}=== Vector Migration Tool ===${NC}\n"

# Function to check if Qdrant is running
check_qdrant() {
    echo -e "${YELLOW}Checking Qdrant status...${NC}"
    if curl -s http://localhost:6333/health > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Qdrant is running${NC}"
        return 0
    else
        echo -e "${RED}✗ Qdrant is not running${NC}"
        echo -e "${YELLOW}Please start Qdrant with: docker-compose up -d${NC}"
        return 1
    fi
}

# Function to check database
check_database() {
    echo -e "${YELLOW}Checking SQLite database...${NC}"
    DB_PATH="$PROJECT_ROOT/Web/NovelAgentWeb/App_Data/Database/novelagent.db"

    if [ -f "$DB_PATH" ]; then
        echo -e "${GREEN}✓ Database exists${NC}"

        # Check if projects exist
        PROJECT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM novel_projects;" 2>/dev/null || echo "0")
        CHAPTER_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM chapters;" 2>/dev/null || echo "0")

        echo -e "  Projects: ${GREEN}$PROJECT_COUNT${NC}"
        echo -e "  Chapters: ${GREEN}$CHAPTER_COUNT${NC}"

        if [ "$PROJECT_COUNT" -eq "0" ]; then
            echo -e "${YELLOW}⚠ No projects found. Run Task 1.4 migration first.${NC}"
            return 1
        fi

        return 0
    else
        echo -e "${RED}✗ Database not found${NC}"
        echo -e "${YELLOW}Please run Task 1.1 (database initialization) first${NC}"
        return 1
    fi
}

# Function to check vector files
check_vector_files() {
    echo -e "${YELLOW}Checking for vector embedding files...${NC}"
    CHAPTER_EMBEDDINGS="$PROJECT_ROOT/Web/NovelAgentWeb/App_Data/Config/guides/chapter_embeddings.json"
    CHUNK_EMBEDDINGS="$PROJECT_ROOT/Web/NovelAgentWeb/App_Data/Config/guides/chunk_embeddings.json"

    HAS_VECTORS=false

    if [ -f "$CHAPTER_EMBEDDINGS" ]; then
        echo -e "${GREEN}✓ chapter_embeddings.json found${NC}"
        HAS_VECTORS=true
    else
        echo -e "${YELLOW}⚠ chapter_embeddings.json not found${NC}"
    fi

    if [ -f "$CHUNK_EMBEDDINGS" ]; then
        echo -e "${GREEN}✓ chunk_embeddings.json found${NC}"
        HAS_VECTORS=true
    else
        echo -e "${YELLOW}⚠ chunk_embeddings.json not found${NC}"
    fi

    if [ "$HAS_VECTORS" = false ]; then
        echo -e "${YELLOW}No vector files found. Migration will be skipped.${NC}"
        echo -e "${YELLOW}To generate sample vectors for testing, use: --generate-samples${NC}"
    fi
}

# Function to build the project
build_project() {
    echo -e "${YELLOW}Building Vector Migration Tool...${NC}"
    cd "$MIGRATION_DIR"

    if dotnet build VectorMigrationTool.csproj --configuration Release > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Build successful${NC}"
        return 0
    else
        echo -e "${RED}✗ Build failed${NC}"
        dotnet build VectorMigrationTool.csproj --configuration Release
        return 1
    fi
}

# Parse arguments
MIGRATION_ARGS=()
RUN_CHECKS=true
GENERATE_SAMPLES=false

for arg in "$@"; do
    case $arg in
        --skip-checks)
            RUN_CHECKS=false
            ;;
        --generate-samples)
            GENERATE_SAMPLES=true
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --skip-checks        Skip prerequisite checks"
            echo "  --generate-samples   Generate sample vector files for testing"
            echo "  --force             Force migration even if collections exist"
            echo "  --no-backup         Skip backup creation"
            echo "  --verbose           Enable verbose logging"
            echo "  --test-search       Run similarity search test after migration"
            echo "  --help              Show this help message"
            echo ""
            exit 0
            ;;
        *)
            MIGRATION_ARGS+=("$arg")
            ;;
    esac
done

# Run prerequisite checks
if [ "$RUN_CHECKS" = true ]; then
    echo -e "${BLUE}Running prerequisite checks...${NC}\n"

    if ! check_qdrant; then
        exit 1
    fi
    echo ""

    if ! check_database; then
        exit 1
    fi
    echo ""

    check_vector_files
    echo ""
fi

# Build project
if ! build_project; then
    exit 1
fi
echo ""

# Generate samples if requested
if [ "$GENERATE_SAMPLES" = true ]; then
    echo -e "${YELLOW}Generating sample vector files...${NC}"
    # This would require a separate sample generator tool
    echo -e "${YELLOW}Sample generation feature coming soon${NC}"
    echo ""
fi

# Run migration
echo -e "${BLUE}Starting vector migration...${NC}\n"
cd "$MIGRATION_DIR"

if dotnet run --project VectorMigrationTool.csproj --configuration Release -- "${MIGRATION_ARGS[@]}"; then
    echo -e "\n${GREEN}✓ Migration completed successfully${NC}"
    exit 0
else
    echo -e "\n${RED}✗ Migration failed${NC}"
    exit 1
fi
