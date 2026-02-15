FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["src/RealTimeDashboard/RealTimeDashboard.csproj", "RealTimeDashboard/"]
RUN dotnet restore "RealTimeDashboard/RealTimeDashboard.csproj"

# Copy source and publish
COPY src/ .
RUN dotnet publish "RealTimeDashboard/RealTimeDashboard.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "RealTimeDashboard.dll"]
