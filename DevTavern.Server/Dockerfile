FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ["DevTavern.Server.csproj", "./"]
RUN dotnet restore "./DevTavern.Server.csproj"

COPY . .
WORKDIR "/app/."
RUN dotnet build "DevTavern.Server.csproj" -c Release -o /app/build

RUN dotnet publish "DevTavern.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DevTavern.Server.dll"]