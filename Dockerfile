# Используем официальное изображение .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Устанавливаем рабочую директорию
WORKDIR /

# Копируем файл проекта отдельно, чтобы восстановить зависимости
COPY EasyConvert2.csproj EasyConvert2/
RUN dotnet restore "EasyConvert2/EasyConvert2.csproj"

# Копируем остальные исходные файлы
COPY . EasyConvert2/

# Сборка проекта
WORKDIR /EasyConvert2
RUN dotnet build "EasyConvert2.csproj" -c Release -o /app/build

# Публикация (опционально, но желательно)
RUN dotnet publish "EasyConvert2.csproj" -c Release -o /app/publish

# Используем runtime-образ (меньше весит)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Определяем команду для запуска
ENTRYPOINT ["dotnet", "EasyConvert2.dll"]
