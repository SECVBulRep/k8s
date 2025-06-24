# Сборочный этап
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Копируем проекты
COPY HostnameApi/ ./HostnameApi
COPY SeedWork/ ./SeedWork

# Переходим в директорию HostnameApi и публикуем
WORKDIR /app/HostnameApi
RUN dotnet publish -c Release -o /app/out

# Рантайм
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "HostnameApi.dll"]
