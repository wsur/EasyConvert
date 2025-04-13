# Указываем базовый образ
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Строим приложение
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем решение и проект
COPY ["EasyConvert2.csproj", "EasyConvert2/"]
COPY ["EasyConvert2.sln", "./"]

# Выполняем восстановление зависимостей
RUN dotnet restore "EasyConvert2/EasyConvert2.csproj"

# Копируем все остальные файлы
COPY . .

# Строим проект
WORKDIR "EasyConvert2"
RUN dotnet build "EasyConvert2.csproj" -c Release -o /app/build

# Публикуем приложение
FROM build AS publish
RUN dotnet publish "EasyConvert2.csproj" -c Release -o /app/publish

# Финальный образ с уже собранным приложением
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EasyConvert2.dll"]
