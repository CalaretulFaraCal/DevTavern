# ETAPA 1: Construirea aplicației (Build)
# Luăm o imagine oficială de la Microsoft care are uneltele necesare să compileze cod C# (.NET 8 SDK)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Setăm folderul curent în interiorul containerului ca fiind /src
WORKDIR /src

# Copiem fișierul de proiect și descărcăm dependențele (NuGet packages)
# Astfel folosim cache-ul Docker pentru a grăbi viitoarele build-uri
COPY ["DevTavern.Server.csproj", "./"]
RUN dotnet restore "DevTavern.Server.csproj"

# Copiem tot restul codului din computerul nostru în container
COPY . .

# Compilăm aplicația în folderul /app/build, modul Release (optimizat)
RUN dotnet build "DevTavern.Server.csproj" -c Release -o /app/build

# Publicăm aplicația (creăm fișierele finale gata de rulat) într-un alt folder /app/publish
RUN dotnet publish "DevTavern.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ETAPA 2: Rularea aplicației (Runtime)
# Nu vrem să rulăm aplicația pe o imagine greoaie cu SDK (care are ~800MB),
# așa că luăm o imagine mult mai mică de la Microsoft doar cu mediul de rulare aspnet.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Expunem portul 8080 (asta e portul standard intern pe care îl va asculta serverul în Linux)
EXPOSE 8080

# Copiem fișierele publicate de la Etapa 1 în această nouă "mașinărie" de runtime
COPY --from=build /app/publish .

# Comanda finală: Când acest container "prinde viață", rulează serverul!
ENTRYPOINT ["dotnet", "DevTavern.Server.dll"]