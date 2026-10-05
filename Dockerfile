# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Commerce.Core/Commerce.Core.csproj src/Commerce.Core/
COPY src/Commerce.Gateway/Commerce.Gateway.csproj src/Commerce.Gateway/
RUN dotnet restore src/Commerce.Gateway/Commerce.Gateway.csproj
COPY catalog/ catalog/
COPY src/ src/
RUN dotnet publish src/Commerce.Gateway/Commerce.Gateway.csproj -c Release -o /app --no-restore

# Debian-based runtime: ships ICU and tzdata, which the schedule maths needs for IANA zones.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Commerce.Gateway.dll"]
