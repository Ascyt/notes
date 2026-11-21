FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Mount points for the notes and the data protection keys, writable by the app user (named volumes copy this ownership)
RUN mkdir /data /keys && chown $APP_UID:$APP_UID /data /keys
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# The project files first, so that the restore is only done again when they change
COPY ["global.json", "./"]
COPY ["src/Notes/Notes.csproj", "src/Notes/"]
COPY ["src/Notes.Host/Notes.Host.csproj", "src/Notes.Host/"]
RUN dotnet restore "src/Notes.Host/Notes.Host.csproj"
COPY . .
WORKDIR "/src/src/Notes.Host"
RUN dotnet build "./Notes.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Notes.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Notes.Host.dll"]
