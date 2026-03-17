# Base stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["Ecommerce.API.csproj", "./"]
COPY ["Ecommerce.Application/Ecommerce.Application.csproj", "Ecommerce.Application/"]
COPY ["Ecommerce.Domain/Ecommerce.Domain.csproj", "Ecommerce.Domain/"]
COPY ["Ecommerce.Infrastructure/Ecommerce.Infrastructure.csproj", "Ecommerce.Infrastructure/"]

RUN dotnet restore "Ecommerce.API.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "Ecommerce.API.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Ecommerce.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Root stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ecommerce.API.dll"]
