FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy sln and central package management props
COPY ["FpsCdn.slnx", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]

# Copy project files
COPY ["src/NpmCdn.Api/src/NpmCdn.Api.csproj", "src/NpmCdn.Api/src/"]
COPY ["src/NpmCdn.NpmRegistry/src/NpmCdn.NpmRegistry.csproj", "src/NpmCdn.NpmRegistry/src/"]
COPY ["src/NpmCdn.Storage/src/NpmCdn.Storage.csproj", "src/NpmCdn.Storage/src/"]

# Restore
RUN dotnet restore "src/NpmCdn.Api/src/NpmCdn.Api.csproj"

# Copy full source code
COPY . .
WORKDIR "/src/src/NpmCdn.Api/src"

# Publish
RUN dotnet publish "NpmCdn.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NpmCdn.Api.dll"]
