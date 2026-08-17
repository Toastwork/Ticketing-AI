# GitHub Actions - Build Automatique

Build automatique de l'image Docker quand tu push sur `main`.

## 🚀 Comment ça marche

1. Tu push sur `main`
2. GitHub Actions build l'image automatiquement
3. L'image est publiée sur `ghcr.io` (GitHub Container Registry)

**Images créées:**
- `ghcr.io/ton-username/email-ticketing-service:latest`
- `ghcr.io/ton-username/email-ticketing-service:abc123` (hash du commit)

## ⚙️ Configuration (une seule fois)

### 1. Activer les permissions GitHub Actions

Dans ton repo GitHub:
1. **Settings** > **Actions** > **General**
2. Scroll jusqu'à **Workflow permissions**
3. Sélectionne **Read and write permissions**
4. Sauvegarde

### 2. Créer un Personal Access Token (pour repo privé)

Si ton repo est **privé**, l'image sera aussi privée. Il faut un token pour la récupérer:

1. Va dans **Settings** (ton profil, pas le repo)
2. **Developer settings** (tout en bas)
3. **Personal access tokens** > **Tokens (classic)**
4. **Generate new token (classic)**
5. Nom: "Production Docker Pull"
6. Permission: **`read:packages`** ✅
7. **Generate token**
8. **COPIE LE TOKEN** immédiatement!

## 📦 Déploiement en production

### Commandes

```bash
# 1. Se connecter (une seule fois)
echo "TON_TOKEN" | docker login ghcr.io -u TON_USERNAME --password-stdin

# 2. Pull et démarrer
docker compose pull
docker compose up -d

# 3. Voir les logs
docker compose logs -f
```

### Mettre à jour

```bash
docker compose pull
docker compose up -d
```

## 🔍 Monitoring

- **Voir les builds**: Va dans **Actions** sur GitHub
- **Voir les images publiées**: Profil GitHub > **Packages**

## 🔐 Sécurité

**⚠️ Important:**
- Ne commit JAMAIS le token dans git
- Le token est déjà dans `.gitignore` (fichier `.env`)
- Régénère le token si compromis

## 🆘 Troubleshooting

### Erreur: "denied: permission denied"

Tu n'es pas connecté au registry:
```bash
echo "TON_TOKEN" | docker login ghcr.io -u TON_USERNAME --password-stdin
```

### Erreur: "unauthorized: authentication required"

Le token n'a pas les bonnes permissions. Vérifie:
1. Permission `read:packages` cochée
2. Token non expiré
3. Username correct

### L'image n'est pas à jour

Force le pull:
```bash
docker compose pull
docker compose up -d --force-recreate
```
