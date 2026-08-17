FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copier le fichier projet et restaurer les dépendances
COPY *.csproj ./
RUN dotnet restore

# Copier tous les fichiers sources
COPY Data/ ./Data/
COPY Models/ ./Models/
COPY Services/ ./Services/
COPY Properties/ ./Properties/
COPY *.cs ./
COPY *.json ./

# Compiler et publier
RUN dotnet publish EmailTicketingService.csproj -c Release -o /app/publish

# Image finale
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Créer le répertoire pour la base de données
RUN mkdir -p /app/data

# Copier les fichiers compilés
COPY --from=build /app/publish .

# Variables d'environnement par défaut (à surcharger avec docker-compose)
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "EmailTicketingService.dll"]
