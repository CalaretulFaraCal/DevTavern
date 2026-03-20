# Etapa 1: Build (Aici descărcăm unelte grele doar ca să compilăm codul, nu să-l rulăm)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copiem doar fișierul CSPROJ (lista de pachete) și instalăm ce trebuie (NuGet restore)
COPY ["DevTavern.Server.csproj", "./"]
RUN dotnet restore "./DevTavern.Server.csproj"

# Acum copiem tot restul codului și îl compilăm oficial (Build Succeeded)
COPY . .
WORKDIR "/app/."
RUN dotnet build "DevTavern.Server.csproj" -c Release -o /app/build

# Acum îl publicăm într-un folder mic, curat, gata de "Production"
RUN dotnet publish "DevTavern.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa 2: Run (Aceasta este imaginea ultra-ușoară pe care o va primi clientul / profesorul)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Luăm folderul "publish" din etapa de build și îl lipim aici în etapa finală
COPY --from=build /app/publish .

# Comanda cu care va porni aplicația de fiecare dată când containerul Docker se aprinde
ENTRYPOINT ["dotnet", "DevTavern.Server.dll"]