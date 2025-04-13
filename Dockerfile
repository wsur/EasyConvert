# Используем официальный образ .NET
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Используем образ SDK для сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["EasyConvert2/EasyConvert2.csproj", "EasyConvert2/"]
RUN dotnet restore "EasyConvert2/EasyConvert2.csproj"
COPY . .
WORKDIR "/src/EasyConvert2"
RUN dotnet build "EasyConvert2.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EasyConvert2.csproj" -c Release -o /app/publish

# В конечный контейнер копируем скомпилированное приложение
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EasyConvert2.dll"]
