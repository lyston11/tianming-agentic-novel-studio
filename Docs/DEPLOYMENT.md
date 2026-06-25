# Deployment Guide

This document provides comprehensive deployment instructions for the Tianming Agentic Novel Studio multi-user system.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Database Setup](#database-setup)
- [Qdrant Vector Database Setup](#qdrant-vector-database-setup)
- [Frontend Build](#frontend-build)
- [Backend Deployment](#backend-deployment)
- [Environment Variables](#environment-variables)
- [Health Checks](#health-checks)
- [Troubleshooting](#troubleshooting)
- [Backup and Restore](#backup-and-restore)
- [Production Considerations](#production-considerations)

---

## Prerequisites

### Required Software

| Component | Version | Purpose |
|-----------|---------|---------|
| **.NET SDK** | 8.0+ | Backend runtime and build |
| **Node.js** | 18.0+ | Frontend build |
| **npm** | 9.0+ | Frontend package manager |
| **Docker** | 24.0+ | Qdrant vector database |
| **Docker Compose** | 2.0+ | Container orchestration |

### Operating System Support

- **macOS**: 11 (Big Sur) or later
- **Linux**: Ubuntu 20.04+, Debian 11+, RHEL 8+
- **Windows**: Windows 10/11 with WSL2 (for Docker)

### Hardware Requirements

**Minimum:**
- 2 CPU cores
- 4GB RAM
- 10GB disk space

**Recommended:**
- 4+ CPU cores
- 8GB+ RAM
- 20GB+ SSD storage

---

## Quick Start

For a fully automated deployment, use the deployment script:

```bash
cd /path/to/tianming-agentic-novel-studio
chmod +x scripts/deploy.sh
./scripts/deploy.sh
```

The script will:
1. Check prerequisites
2. Run database migrations
3. Start Qdrant container
4. Build backend
5. Build frontend
6. Perform health checks

---

## Configuration

### appsettings.json

Located at `Web/NovelAgentWeb/appsettings.json`, this file contains core configuration:

```json
{
  "ConnectionStrings": {
    "NovelAgentDb": "Data Source=App_Data/Database/novelagent.db"
  },
  "NovelAgent": {
    "StorageRoot": "App_Data",
    "ProjectName": "AgenticNovelStudio"
  },
  "Qdrant": {
    "BaseUrl": "http://localhost:6333",
    "Host": "localhost",
    "Port": 6334,
    "HealthCheckIntervalSeconds": 60,
    "VectorDimension": 512,
    "BatchSize": 100
  },
  "Embedding": {
    "Provider": "bge-small-zh",
    "Model": "bge-small-zh-v1.5",
    "RequireRealEmbeddings": true
  },
  "JwtSettings": {
    "SecretKey": "CHANGE_THIS_IN_PRODUCTION",
    "Issuer": "NovelAgentWeb",
    "Audience": "NovelAgentWeb",
    "ExpiryDays": "7"
  }
}
```

### Configuration Keys Reference

| Key | Description | Default | Required |
|-----|-------------|---------|----------|
| `ConnectionStrings:NovelAgentDb` | SQLite database file path | `App_Data/Database/novelagent.db` | Yes |
| `NovelAgent:StorageRoot` | Root directory for project files | `App_Data` | Yes |
| `NovelAgent:ProjectName` | Default project namespace | `AgenticNovelStudio` | Yes |
| `Qdrant:BaseUrl` | Qdrant REST API endpoint | `http://localhost:6333` | Yes |
| `Qdrant:Host` | Qdrant gRPC host | `localhost` | Yes |
| `Qdrant:Port` | Qdrant gRPC port | `6334` | Yes |
| `Qdrant:VectorDimension` | Embedding vector size | `512` | Yes |
| `Qdrant:BatchSize` | Bulk operation batch size | `100` | No |
| `Embedding:Provider` | Embedding provider. Current build supports `bge-small-zh` | `bge-small-zh` | Yes |
| `Embedding:RequireRealEmbeddings` | Fail startup if real embeddings are unavailable | `true` | Yes |
| `Redis:Enabled` | Enable Redis-backed runtime state, locks, event broadcast, and tool cache | `true` | Yes |
| `Redis:ConnectionString` | Redis endpoint | `localhost:6379` | Yes |
| `JwtSettings:SecretKey` | JWT signing secret (min 32 chars) | - | **Yes** |
| `JwtSettings:Issuer` | JWT token issuer | `NovelAgentWeb` | Yes |
| `JwtSettings:Audience` | JWT token audience | `NovelAgentWeb` | Yes |
| `JwtSettings:ExpiryDays` | Token validity period | `7` | Yes |

---

## Database Setup

### Initial Migration

The SQLite database is created automatically on first run. To manually apply migrations:

```bash
cd Web/NovelAgentWeb
dotnet ef database update
```

### Database Schema

The system uses the following tables:

- **Users**: User accounts with hashed passwords
- **Projects**: Novel projects owned by users
- **Chapters**: Chapter metadata and content paths
- **Foreshadows**: Plot threads with lifecycle tracking
- **CharacterLedgers**: Character state snapshots
- **CanonLedgers**: Story canon entries

### Database And Index Updates

Database schema updates are handled by the current Web project migrations:

```bash
cd Web/NovelAgentWeb
dotnet ef database update
```

Qdrant knowledge, memory, and chapter indexes are rebuilt through the current outbox and vectorization services. Legacy JSON/vector migration tools are intentionally removed from the production path.

---

## Qdrant Vector Database Setup

### Docker Compose Deployment

Start Qdrant using Docker Compose:

```bash
docker-compose up -d qdrant
```

This creates a Qdrant container with:
- REST API on port 6333
- gRPC API on port 6334
- Persistent storage in `App_Data/Qdrant/`

When running the full Docker Compose stack, the API connects to Qdrant by gRPC on
`Qdrant__Host=qdrant` and `Qdrant__Port=6334`.

### Redis Cache Setup

Redis is optional for local development. By default the API uses an in-process
distributed memory cache and keeps SQLite as the authoritative store. The Docker
Compose stack includes Redis and enables it for the API with:

```bash
Redis__Enabled=true
Redis__ConnectionString=redis:6379
```

### Manual Docker Deployment

Alternatively, run Qdrant directly:

```bash
docker run -d \
  --name novelagent-qdrant \
  -p 6333:6333 \
  -p 6334:6334 \
  -v $(pwd)/App_Data/Qdrant:/qdrant/storage \
  qdrant/qdrant:v1.8.0
```

### Verify Qdrant Status

Check Qdrant health:

```bash
curl http://localhost:6333/health
```

Expected response: `{"title":"qdrant - vector search engine","version":"1.8.0"}`

### Collection Structure

Collections are created automatically per project:
- Collection name format: `project_{projectId}`
- Vector dimension: 512 (configured in appsettings.json)
- Distance metric: Cosine similarity

---

## Frontend Build

### Development Build

```bash
cd Web/NovelAgentWeb.Frontend
npm install
npm run dev
```

Frontend will be available at `http://localhost:3002`. Vite proxies `/api`
requests to `http://127.0.0.1:5002`.

### Production Build

```bash
cd Web/NovelAgentWeb.Frontend
npm install
npm run build
```

Build output goes to `dist/`, which is served by the backend.

### Build Configuration

Edit `vite.config.ts` to customize:
- Output directory
- Proxy settings
- Build optimization

---

## Backend Deployment

### Development Mode

```bash
cd Web/NovelAgentWeb
ASPNETCORE_URLS=http://+:5002 dotnet run
```

Development URL: `http://localhost:5002`. Keep this aligned with
`Web/NovelAgentWeb.Frontend/vite.config.ts`.

### Production Build

```bash
cd Web/NovelAgentWeb
dotnet publish -c Release -o ./publish
```

### Run Production Build

```bash
cd Web/NovelAgentWeb/publish
ASPNETCORE_URLS=http://+:5002 dotnet NovelAgentWeb.dll
```

### Service Configuration

For systemd (Linux):

```ini
[Unit]
Description=Tianming Agentic Novel Studio
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/novelagent
ExecStart=/usr/bin/dotnet /opt/novelagent/NovelAgentWeb.dll
Restart=always
RestartSec=10
User=novelagent
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5002
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

---

## Environment Variables

### Required for Production

Set these environment variables before deployment:

```bash
# JWT Secret (CRITICAL - MUST BE SET)
export JWT_SECRET_KEY="your-super-secure-random-string-min-32-chars"

# Environment
export ASPNETCORE_ENVIRONMENT="Production"

# Database Path (optional, override appsettings.json)
export ConnectionStrings__NovelAgentDb="Data Source=/data/novelagent.db"

# Qdrant Connection (optional)
export Qdrant__BaseUrl="http://qdrant-server:6333"
export Qdrant__Host="qdrant-server"
```

### Environment Variable Precedence

Configuration is loaded in this order (later overrides earlier):
1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. Environment variables
4. Command-line arguments

---

## Health Checks

### Backend Health Check

```bash
curl http://localhost:5002/health
```

The health response includes embedding, Qdrant, Redis, and database status. The
current runtime expects `provider=bge-small-zh` with model-grade semantic
retrieval; missing model files or unavailable vector infrastructure should be
treated as deployment errors.

### Database Health Check

```bash
sqlite3 Web/NovelAgentWeb/App_Data/Database/novelagent.db "SELECT COUNT(*) FROM Users;"
```

### Qdrant Health Check

```bash
curl http://localhost:6333/health
curl http://localhost:6333/collections
```

### Full System Health Script

```bash
#!/bin/bash
echo "Checking Backend..."
curl -f http://localhost:5002/health || echo "Backend FAILED"

echo "Checking Qdrant..."
curl -f http://localhost:6333/health || echo "Qdrant FAILED"

echo "Checking Database..."
sqlite3 Web/NovelAgentWeb/App_Data/Database/novelagent.db "SELECT 1;" || echo "Database FAILED"

echo "All health checks complete"
```

---

## Troubleshooting

### Issue: "Database locked" errors

**Cause:** SQLite does not support high concurrency

**Solution:**
- Enable WAL mode: `PRAGMA journal_mode=WAL;`
- Reduce concurrent writes
- Consider PostgreSQL for high-traffic deployments

### Issue: Qdrant connection timeout

**Cause:** Qdrant container not running or network misconfiguration

**Solution:**
```bash
docker ps | grep qdrant  # Check if running
docker logs novelagent-qdrant  # Check logs
docker restart novelagent-qdrant  # Restart
```

### Issue: JWT validation fails

**Cause:** Secret key mismatch or token expiry

**Solution:**
- Verify `JwtSettings:SecretKey` is consistent
- Check token expiry: decode JWT at jwt.io
- Clear browser local storage and re-login

### Issue: Frontend 404 errors

**Cause:** Backend not serving frontend static files

**Solution:**
- Ensure frontend is built: `npm run build`
- Check `dist/` folder exists in `NovelAgentWeb.Frontend/`
- Verify backend serves static files from `wwwroot/`

### Issue: EF Core migration errors

**Cause:** Database schema out of sync

**Solution:**
```bash
cd Web/NovelAgentWeb
dotnet ef migrations list  # Check applied migrations
dotnet ef database update  # Apply pending migrations
```

### Issue: Port already in use

**Cause:** Another process using 5002, 3002, 6333, or 6334

**Solution:**
```bash
# Find process using port
lsof -i :5002
lsof -i :3002
lsof -i :6333

# Kill process or change port in config
```

---

## Backup and Restore

### Database Backup

```bash
# Backup SQLite database
cp Web/NovelAgentWeb/App_Data/Database/novelagent.db \
   /backups/novelagent-$(date +%Y%m%d).db

# Automated daily backup
0 2 * * * cp /opt/novelagent/App_Data/Database/novelagent.db \
  /backups/novelagent-$(date +\%Y\%m\%d).db
```

### Qdrant Backup

```bash
# Backup Qdrant storage
tar -czf qdrant-backup-$(date +%Y%m%d).tar.gz \
  Web/NovelAgentWeb/App_Data/Qdrant/

# Restore Qdrant
docker-compose down qdrant
tar -xzf qdrant-backup-YYYYMMDD.tar.gz -C Web/NovelAgentWeb/App_Data/
docker-compose up -d qdrant
```

### Full System Backup

```bash
#!/bin/bash
BACKUP_DIR="/backups/$(date +%Y%m%d)"
mkdir -p "$BACKUP_DIR"

# Database
cp App_Data/Database/novelagent.db "$BACKUP_DIR/"

# Qdrant vectors
tar -czf "$BACKUP_DIR/qdrant.tar.gz" App_Data/Qdrant/

# Project files
tar -czf "$BACKUP_DIR/projects.tar.gz" App_Data/Projects/

echo "Backup complete: $BACKUP_DIR"
```

### Restore Procedure

1. Stop all services
2. Restore database file
3. Restore Qdrant storage
4. Restore project files
5. Restart services
6. Run health checks

---

## Production Considerations

### Security Checklist

- [ ] Change `JwtSettings:SecretKey` to a strong random value (32+ characters)
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Enable HTTPS (use reverse proxy like Nginx)
- [ ] Set up firewall rules (only expose necessary ports)
- [ ] Use strong passwords for user accounts
- [ ] Regularly update dependencies: `dotnet outdated`, `npm outdated`
- [ ] Enable rate limiting on auth endpoints
- [ ] Set up log monitoring and alerting

### Performance Tuning

**Database:**
- Enable WAL mode: `PRAGMA journal_mode=WAL;`
- Increase cache size: `PRAGMA cache_size=-64000;` (64MB)
- Vacuum regularly: `VACUUM;`

**Qdrant:**
- Increase batch size for bulk operations (appsettings.json)
- Use gRPC for better performance (port 6334)
- Configure memory limits in docker-compose.yml

**Backend:**
- Enable response compression
- Configure caching headers for static files
- Use connection pooling

**Frontend:**
- Enable gzip/brotli compression on reverse proxy
- Set long cache times for static assets
- Use CDN for static resources

### Monitoring

**Recommended Tools:**
- **Logs**: Serilog + Seq or ELK Stack
- **Metrics**: Prometheus + Grafana
- **APM**: Application Insights or Datadog
- **Uptime**: UptimeRobot or Pingdom

**Key Metrics to Monitor:**
- API response times (p50, p95, p99)
- Database query performance
- Qdrant query latency
- Memory usage
- Disk space (SQLite grows over time)
- Error rates by endpoint

### Scaling Considerations

**Current Architecture Limitations:**
- SQLite: Not suitable for high concurrency (consider PostgreSQL)
- Single instance: No horizontal scaling (add load balancer + multiple instances)
- File storage: Not distributed (consider blob storage like S3)

**Migration Path:**
1. Replace SQLite with PostgreSQL
2. Add Redis for session storage
3. Move file storage to S3/Azure Blob
4. Deploy multiple backend instances behind load balancer
5. Use managed Qdrant Cloud or Qdrant cluster

---

## Support

For issues and questions:
- GitHub Issues: https://github.com/your-org/tianming-agentic-novel-studio
- Documentation: `/docs` directory
- Email: support@example.com

---

**Last Updated:** 2026-06-08
