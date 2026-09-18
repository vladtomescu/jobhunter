FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NuGet.config", "Directory.Build.props", "./"]
COPY ["src/JobHunter/JobHunter.csproj", "src/JobHunter/"]
RUN dotnet restore "src/JobHunter/JobHunter.csproj"
COPY . .
WORKDIR "/src/src/JobHunter"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./JobHunter.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --chown=app:app --from=publish /app/publish .
COPY profile/ ./profile/
COPY prompts/ ./prompts/
ENV ASPNETCORE_HTTP_PORTS=
ENTRYPOINT ["dotnet", "JobHunter.dll"]
