# Use the official .NET 10 SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore as distinct layers
COPY src/Oracle.Api/Oracle.Api.csproj ./Oracle.Api/
COPY src/Oracle.Core/Oracle.Core.csproj ./Oracle.Core/
COPY src/Oracle.Infrastructure/Oracle.Infrastructure.csproj ./Oracle.Infrastructure/
COPY src/Oracle.Generators/Oracle.Generators.csproj ./Oracle.Generators/
COPY src/Oracle.Core.Tests/Oracle.Core.Tests.csproj ./Oracle.Core.Tests/
COPY src/Oracle.Generators.Tests/Oracle.Generators.Tests.csproj ./Oracle.Generators.Tests/

RUN dotnet restore "Oracle.Api/Oracle.Api.csproj"

# Copy the entire source tree
COPY src/ ./src/

# Build and publish the API project
RUN dotnet publish "Oracle.Api/Oracle.Api.csproj" -c Release -o /app/publish /p:PublishTrimmed=false

# Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Oracle.Api.dll"]
