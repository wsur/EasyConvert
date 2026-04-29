FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /

COPY EasyConvert2.csproj EasyConvert2/
RUN dotnet restore "EasyConvert2/EasyConvert2.csproj"

COPY . EasyConvert2/

WORKDIR /EasyConvert2
RUN dotnet build "EasyConvert2.csproj" -c Release -o /app/build

RUN dotnet publish "EasyConvert2.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "EasyConvert2.dll"]
