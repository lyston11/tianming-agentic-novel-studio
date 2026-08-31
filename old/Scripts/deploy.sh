#!/bin/bash
set -e

# Tianming Agentic Novel Studio - Automated Deployment Script
# This script automates the complete deployment process including:
# - Prerequisites check
# - Database migrations
# - Qdrant vector database startup
# - Backend build
# - Frontend build
# - Health checks

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Configuration
BACKEND_DIR="$PROJECT_ROOT/Web/NovelAgentWeb"
FRONTEND_DIR="$PROJECT_ROOT/Web/NovelAgentWeb.Frontend"
DOCKER_COMPOSE_FILE="$PROJECT_ROOT/docker-compose.yml"
BACKEND_URL="http://localhost:5002"
DOTNET="$PROJECT_ROOT/Scripts/dotnet"

# Print functions
print_header() {
    echo -e "${BLUE}╔════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${BLUE}║  Tianming Agentic Novel Studio - Deployment Script       ║${NC}"
    echo -e "${BLUE}╚════════════════════════════════════════════════════════════╝${NC}"
    echo ""
}

print_step() {
    echo -e "${GREEN}▶${NC} $1"
}

print_substep() {
    echo -e "  ${BLUE}→${NC} $1"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

# Error handler
error_exit() {
    print_error "$1"
    exit 1
}

# Check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Check prerequisites
check_prerequisites() {
    print_step "Checking prerequisites..."

    local missing_deps=()

    # Check the project-local .NET SDK pinned by global.json.
    if [ -x "$PROJECT_ROOT/.dotnet/dotnet" ]; then
        print_substep ".NET SDK: $($DOTNET --version) ✓"
    else
        print_warning "Project-local .NET SDK wrapper not found"
        missing_deps+=("project-local-dotnet-10.0.400")
    fi

    # Check Node.js
    if command_exists node; then
        NODE_VERSION=$(node --version | cut -d. -f1 | sed 's/v//')
        if [ "$NODE_VERSION" -ge 18 ]; then
            print_substep "Node.js: $(node --version) ✓"
        else
            print_warning "Node.js version is $NODE_VERSION, but 18+ is required"
            missing_deps+=("node-18")
        fi
    else
        print_warning "Node.js not found"
        missing_deps+=("node-18")
    fi

    # Check npm
    if command_exists npm; then
        print_substep "npm: $(npm --version) ✓"
    else
        print_warning "npm not found"
        missing_deps+=("npm")
    fi

    # Check Docker
    if command_exists docker; then
        print_substep "Docker: $(docker --version | cut -d' ' -f3 | sed 's/,//') ✓"
    else
        print_warning "Docker not found"
        missing_deps+=("docker")
    fi

    # Check Docker Compose
    if command_exists docker-compose || docker compose version >/dev/null 2>&1; then
        print_substep "Docker Compose: ✓"
    else
        print_warning "Docker Compose not found"
        missing_deps+=("docker-compose")
    fi

    # Report missing dependencies
    if [ ${#missing_deps[@]} -gt 0 ]; then
        echo ""
        print_error "Missing required dependencies: ${missing_deps[*]}"
        echo ""
        echo "Please install missing dependencies:"
        echo "  - Project-local .NET SDK 10.0.400: run ./Scripts/install-dotnet.sh"
        echo "  - Node.js 18+: https://nodejs.org/"
        echo "  - Docker: https://docs.docker.com/get-docker/"
        echo ""
        exit 1
    fi

    print_success "All prerequisites satisfied"
    echo ""
}

# Run database migrations
run_migrations() {
    print_step "Running database migrations..."

    cd "$BACKEND_DIR" || error_exit "Backend directory not found: $BACKEND_DIR"

    # Check if EF Core tools are installed
    if ! "$DOTNET" ef --version >/dev/null 2>&1; then
        print_substep "Installing EF Core tools..."
        "$DOTNET" tool install --global dotnet-ef || true
    fi

    # Run migrations
    print_substep "Applying database migrations..."
    "$DOTNET" ef database update --no-build || error_exit "Database migration failed"

    print_success "Database migrations complete"
    echo ""
}

# Start Qdrant container
start_qdrant() {
    print_step "Starting Qdrant vector database..."

    cd "$PROJECT_ROOT" || error_exit "Project root not found"

    if [ ! -f "$DOCKER_COMPOSE_FILE" ]; then
        error_exit "docker-compose.yml not found in project root"
    fi

    # Check if Qdrant is already running
    if docker ps | grep -q "novelagent-qdrant"; then
        print_substep "Qdrant container already running"
    else
        print_substep "Starting Qdrant container..."
        if command_exists docker-compose; then
            docker-compose up -d qdrant
        else
            docker compose up -d qdrant
        fi
    fi

    # Wait for Qdrant to be ready
    print_substep "Waiting for Qdrant to be ready..."
    local max_attempts=30
    local attempt=0

    while [ $attempt -lt $max_attempts ]; do
        if curl -s http://localhost:6333/health >/dev/null 2>&1; then
            print_success "Qdrant is ready"
            echo ""
            return 0
        fi
        attempt=$((attempt + 1))
        sleep 1
    done

    error_exit "Qdrant failed to start after $max_attempts seconds"
}

# Build backend
build_backend() {
    print_step "Building backend..."

    cd "$BACKEND_DIR" || error_exit "Backend directory not found: $BACKEND_DIR"

    print_substep "Restoring NuGet packages..."
    "$DOTNET" restore || error_exit "NuGet restore failed"

    print_substep "Building in Release mode..."
    "$DOTNET" build -c Release --no-restore || error_exit "Backend build failed"

    print_substep "Publishing backend..."
    "$DOTNET" publish -c Release -o ./publish --no-build || error_exit "Backend publish failed"

    print_success "Backend build complete"
    echo ""
}

# Build frontend
build_frontend() {
    print_step "Building frontend..."

    cd "$FRONTEND_DIR" || error_exit "Frontend directory not found: $FRONTEND_DIR"

    print_substep "Installing npm packages..."
    npm install || error_exit "npm install failed"

    print_substep "Building frontend assets..."
    npm run build || error_exit "Frontend build failed"

    # Check if dist directory was created
    if [ ! -d "$FRONTEND_DIR/dist" ]; then
        error_exit "Frontend build did not create dist directory"
    fi

    print_success "Frontend build complete"
    echo ""
}

# Health checks
run_health_checks() {
    print_step "Running health checks..."

    # Check Qdrant
    print_substep "Checking Qdrant..."
    if curl -sf http://localhost:6333/health >/dev/null 2>&1; then
        print_success "Qdrant health check passed"
    else
        print_warning "Qdrant health check failed (may be normal if not started yet)"
    fi

    # Check database
    print_substep "Checking database..."
    local db_path="$BACKEND_DIR/App_Data/Database/novelagent.db"
    if [ -f "$db_path" ]; then
        if sqlite3 "$db_path" "SELECT COUNT(*) FROM sqlite_master;" >/dev/null 2>&1; then
            print_success "Database health check passed"
        else
            print_warning "Database exists but query failed"
        fi
    else
        print_warning "Database file not found (will be created on first run)"
    fi

    # Check backend build
    print_substep "Checking backend build..."
    if [ -f "$BACKEND_DIR/publish/NovelAgentWeb.dll" ]; then
        print_success "Backend build artifacts found"
    else
        print_warning "Backend publish directory not found"
    fi

    # Check frontend build
    print_substep "Checking frontend build..."
    if [ -d "$FRONTEND_DIR/dist" ]; then
        local file_count=$(find "$FRONTEND_DIR/dist" -type f | wc -l)
        print_success "Frontend build artifacts found ($file_count files)"
    else
        print_warning "Frontend dist directory not found"
    fi

    echo ""
}

# Display summary
display_summary() {
    echo ""
    echo -e "${GREEN}╔════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║  Deployment Complete!                                     ║${NC}"
    echo -e "${GREEN}╚════════════════════════════════════════════════════════════╝${NC}"
    echo ""
    echo "Next steps:"
    echo ""
    echo "1. Start the backend:"
    echo "   cd $BACKEND_DIR"
    echo "   $DOTNET run --project NovelAgentWeb.csproj --urls $BACKEND_URL"
    echo ""
    echo "   Or use the published version:"
    echo "   cd $BACKEND_DIR/publish"
    echo "   $DOTNET NovelAgentWeb.dll"
    echo ""
    echo "2. Access the application:"
    echo "   Frontend dev server: http://localhost:3002"
    echo "   Backend API:          $BACKEND_URL"
    echo ""
    echo "3. Check service status:"
    echo "   Backend:  curl $BACKEND_URL/health"
    echo "   Qdrant:   curl http://localhost:6333/health"
    echo ""
    echo "For more information, see docs/DEPLOYMENT.md"
    echo ""
}

# Main execution
main() {
    print_header

    # Check if running from correct directory
    if [ ! -f "$PROJECT_ROOT/docker-compose.yml" ]; then
        error_exit "Please run this script from the project root or scripts directory"
    fi

    # Run deployment steps
    check_prerequisites
    start_qdrant
    run_migrations
    build_backend
    build_frontend
    run_health_checks
    display_summary
}

# Handle script interruption
trap 'echo ""; print_error "Deployment interrupted"; exit 1' INT TERM

# Run main function
main "$@"
