FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["src/CrabSenseBE.Api/CrabSenseBE.Api.csproj", "src/CrabSenseBE.Api/"]
COPY ["src/CrabSenseBE.Application/CrabSenseBE.Application.csproj", "src/CrabSenseBE.Application/"]
COPY ["src/CrabSenseBE.Domain/CrabSenseBE.Domain.csproj", "src/CrabSenseBE.Domain/"]
COPY ["src/CrabSenseBE.Infrastructure/CrabSenseBE.Infrastructure.csproj", "src/CrabSenseBE.Infrastructure/"]
RUN dotnet restore "src/CrabSenseBE.Api/CrabSenseBE.Api.csproj"

# Copy all source code
COPY . .

# Build
WORKDIR "/src/src/CrabSenseBE.Api"
RUN dotnet build "CrabSenseBE.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CrabSenseBE.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CrabSenseBE.Api.dll"]
