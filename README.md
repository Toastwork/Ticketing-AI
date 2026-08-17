# Email Ticketing Service

Service .NET qui surveille une boîte mail Office 365, analyse les emails avec Gemini/Claude, et crée automatiquement des tickets.

## Fonctionnalités

- 📧 Surveillance automatique de la boîte mail
- 🤖 Analyse IA (Gemini 2.0 Flash ou Claude Haiku 4.5)
- 🎫 Création automatique de tickets
- 🏷️ Catégorisation des emails (ALERTE, ADMINISTRATIF, etc.)
- 📁 Organisation dans "Boîte de réception > Tickets"
- 🔄 Anti-doublon avec SQLite
- 🐳 Docker ready

## Configuration Azure AD

1. [Azure Portal](https://portal.azure.com/) > **Microsoft Entra ID** > **App registrations** > **New registration**
2. **Certificates & secrets** > **New client secret** (copier la valeur!)
3. **API permissions** > Ajouter:
   - `Mail.Read`
   - `Mail.ReadWrite`
   - `MailboxFolder.ReadWrite.All`
4. **Grant admin consent**

## Installation

### 1. Créer `.env`

```bash
cp .env.example .env
nano .env
```

Remplis avec tes credentials Azure + Gemini/Claude.

### 2. Lancer

```bash
# Build local
docker compose up --build -d

# Ou utiliser l'image GitHub (si configuré)
docker compose pull
docker compose up -d
```

### 3. Logs

```bash
docker compose logs -f
```

## Déploiement avec GitHub Actions

Le service build automatiquement l'image Docker quand tu push sur `main`.

**Setup**: [docs/GITHUB_ACTIONS.md](docs/GITHUB_ACTIONS.md)

## Base de données

SQLite dans `./data/emails.db`. Pour voir le contenu:

```bash
sqlite3 data/emails.db "SELECT * FROM ProcessedEmails;"
```

## Structure

```
EmailTicketingService/
├── Services/              # Services métier
├── Models/                # Modèles de données
├── Data/                  # EF Core SQLite
├── Program.cs             # Configuration
├── Worker.cs              # Logique principale
├── Dockerfile
├── docker-compose.yml
└── .env                   # Configuration (non versionné)
```
