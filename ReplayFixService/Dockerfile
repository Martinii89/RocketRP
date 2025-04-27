FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ReplayFixService/ReplayFixService.csproj", "ReplayFixService/"]
COPY ["RocketRP/RocketRP.csproj", "RocketRP/"]
RUN dotnet restore "ReplayFixService/ReplayFixService.csproj"
COPY . .
WORKDIR "/src/ReplayFixService"
RUN dotnet build "./ReplayFixService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ReplayFixService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ReplayFixService.dll"]
