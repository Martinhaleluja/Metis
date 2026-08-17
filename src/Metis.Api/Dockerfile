# Built from the repository root so the Metis.Core project reference resolves:
#   docker build -f src/Metis.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Metis.Core/Metis.Core.csproj src/Metis.Core/
COPY src/Metis.Api/Metis.Api.csproj src/Metis.Api/
RUN dotnet restore src/Metis.Api/Metis.Api.csproj

COPY src/Metis.Core/ src/Metis.Core/
COPY src/Metis.Api/ src/Metis.Api/
RUN dotnet publish src/Metis.Api/Metis.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Fly sets PORT; Kestrel is told to listen on it rather than its default.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Runs as a non-root user. A gateway holding provider keys is the last process
# that should be root in its own container.
RUN useradd --create-home --shell /usr/sbin/nologin metis
USER metis

ENTRYPOINT ["dotnet", "Metis.Api.dll"]
