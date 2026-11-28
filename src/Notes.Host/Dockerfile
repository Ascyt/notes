FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base
# Mount points for the notes and the data protection keys, writable by the app user (named volumes copy this ownership)
RUN mkdir /data /keys && chown $APP_UID:$APP_UID /data /keys
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG BUILD_CONFIGURATION=Release
# Node builds the styles and scripts of the components (src/Notes/package.json), which the build of the library runs
RUN apt-get update && apt-get install -y --no-install-recommends nodejs npm && rm -rf /var/lib/apt/lists/*
WORKDIR /src
# The project files first, so that the restore is only done again when they change
COPY ["global.json", "./"]
COPY ["src/Notes/Notes.csproj", "src/Notes/"]
COPY ["src/Notes.Host/Notes.Host.csproj", "src/Notes.Host/"]
RUN dotnet restore "src/Notes.Host/Notes.Host.csproj"
# The same for the packages of the styles and scripts
COPY ["src/Notes/package.json", "src/Notes/package-lock.json", "src/Notes/"]
RUN npm ci --prefix src/Notes
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
