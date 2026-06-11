#!/bin/bash

# Data Migration Helper Script
# This script helps run the migration tool with common options

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$SCRIPT_DIR/../.."

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

function print_usage() {
    echo "Usage: $0 [command] [options]"
    echo ""
    echo "Commands:"
    echo "  migrate     Run the migration (default)"
    echo "  verify      Verify migration results"
    echo "  help        Show this help message"
    echo ""
    echo "Options for migrate:"
    echo "  --force         Force migration even if data exists"
    echo "  --no-backup     Skip backup creation"
    echo "  -y              Skip confirmation prompt"
    echo ""
    echo "Examples:"
    echo "  $0 migrate"
    echo "  $0 migrate --force -y"
    echo "  $0 verify"
}

function run_migration() {
    echo -e "${GREEN}=== Running Data Migration ===${NC}"
    echo ""

    cd "$SCRIPT_DIR"

    # Build first
    echo "Building migration tool..."
    dotnet build -c Release > /dev/null 2>&1

    if [ $? -ne 0 ]; then
        echo -e "${RED}✗ Build failed${NC}"
        exit 1
    fi

    echo -e "${GREEN}✓ Build succeeded${NC}"
    echo ""

    # Run migration
    dotnet run --no-build -c Release -- "$@"

    EXIT_CODE=$?

    if [ $EXIT_CODE -eq 0 ]; then
        echo ""
        echo -e "${GREEN}✓ Migration completed successfully!${NC}"
        echo ""
        echo -e "${YELLOW}Next steps:${NC}"
        echo "1. Run: $0 verify"
        echo "2. Login with admin/admin123 and change password"
        echo "3. Proceed with Task 1.5: Vector migration to Qdrant"
    else
        echo ""
        echo -e "${RED}✗ Migration failed with exit code $EXIT_CODE${NC}"
    fi

    return $EXIT_CODE
}

function run_verification() {
    echo -e "${GREEN}=== Verifying Migration Results ===${NC}"
    echo ""

    cd "$SCRIPT_DIR"

    # Use the VerificationTool if built, otherwise build it
    if [ ! -f "bin/Release/net8.0/MigrationTool.dll" ]; then
        echo "Building tools..."
        dotnet build -c Release > /dev/null 2>&1
    fi

    # Run verification (pass database path if provided)
    DB_PATH="${1:-$PROJECT_ROOT/Web/NovelAgentWeb/App_Data/Database/novelagent.db}"

    dotnet run --project VerificationTool.csproj --no-build -c Release -- "$DB_PATH"

    return $?
}

# Main script logic
COMMAND="${1:-migrate}"

case "$COMMAND" in
    migrate)
        shift
        run_migration "$@"
        ;;
    verify)
        shift
        run_verification "$@"
        ;;
    help|--help|-h)
        print_usage
        ;;
    *)
        echo -e "${RED}Unknown command: $COMMAND${NC}"
        echo ""
        print_usage
        exit 1
        ;;
esac
