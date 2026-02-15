# Phase 5: Deploy & Blog Article

## Objective
Deploy to Railway, write the blog article, and prepare the GitHub repo for public sharing.

## Tasks

### 5.1 Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/RealTimeDashboard/RealTimeDashboard.csproj", "RealTimeDashboard/"]
RUN dotnet restore "RealTimeDashboard/RealTimeDashboard.csproj"
COPY src/ .
RUN dotnet publish "RealTimeDashboard/RealTimeDashboard.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "RealTimeDashboard.dll"]
```

### 5.2 Railway Configuration
- `railway.toml` or `Procfile` if needed
- Environment variables:
  - `ConnectionStrings__DefaultConnection` → Railway MySQL
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `TransactionProcessor__TPS=10` (lower for demo to save resources)
- MySQL add-on in Railway

### 5.3 Production Tweaks
- Lower TPS in production (10 instead of 100) to stay within Railway free tier
- Add health check endpoint: `/health`
- Configure CORS if needed (shouldn't be for Blazor Server)
- Add response compression for SignalR

### 5.4 README.md for GitHub
Structure:
```markdown
# Real-Time Transaction Dashboard
> SignalR + .NET 8 + Blazor Server + MySQL

[Live Demo](https://your-app.railway.app) | [Architecture Article](https://your-blog.com/article)

## Screenshots
[dashboard screenshot]

## Architecture
[system overview diagram]

## Quick Start
git clone → docker-compose up → localhost:8080

## Key Features
- Real-time transaction streaming via SignalR
- Pre-computed metrics (no live DB queries per refresh)
- Batched broadcasting (500ms intervals)
- Simulated 100+ TPS throughput

## Tech Stack
...

## Performance
[benchmarks table]

## Blog Article
Full architecture deep-dive: [link]

## License
MIT
```

### 5.5 Blog Article Draft
Write `docs/blog/ARTICLE-DRAFT.md` with structure:

1. **Hook:** "Most SignalR tutorials show you how to build a chat app. Here's how to build something that actually handles production load."
2. **The Problem:** Why naive real-time dashboards break at scale
3. **Architecture Overview:** System diagram + explanation
4. **The Three Key Patterns:**
   - Batched broadcasting (why and how)
   - Pre-computed aggregations (metrics pipeline)
   - Channel<T> as internal message bus
5. **Implementation Deep-Dive:** Key code snippets with explanations
6. **Performance Numbers:** Real benchmarks with methodology
7. **Scaling Beyond:** What changes at 500K/1M (Redis backplane, read replicas, CQRS)
8. **GitHub + Live Demo links**

### 5.6 docker-compose.yml for Local Dev
```yaml
version: '3.8'
services:
  app:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ConnectionStrings__DefaultConnection=Server=db;Port=3306;Database=dashboard_db;User=root;Password=dev123;
    depends_on:
      - db
  db:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: dev123
      MYSQL_DATABASE: dashboard_db
    ports:
      - "3306:3306"
    volumes:
      - mysql_data:/var/lib/mysql

volumes:
  mysql_data:
```

## Definition of Done
- [ ] App deploys to Railway and is accessible publicly
- [ ] Demo runs stable for 24+ hours
- [ ] README.md is polished with screenshots
- [ ] Blog article draft is complete
- [ ] GitHub repo is public with proper .gitignore, LICENSE (MIT)
- [ ] Git: commit on `main`, tag `v1.0.0`

## Estimated Time: 2-3 hours with Claude Code
