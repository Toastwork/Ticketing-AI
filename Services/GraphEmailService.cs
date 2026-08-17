using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace EmailTicketingService.Services;

public class GraphEmailService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphEmailService> _logger;
    private readonly Dictionary<string, string> _folderCache = new();

    public GraphEmailService(
        IConfiguration configuration,
        ILogger<GraphEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var tenantId = _configuration["AzureAd:TenantId"];
        var clientId = _configuration["AzureAd:ClientId"];
        var clientSecret = _configuration["AzureAd:ClientSecret"];

        var clientSecretCredential = new ClientSecretCredential(
            tenantId, clientId, clientSecret);

        _graphClient = new GraphServiceClient(clientSecretCredential);
    }

    public async Task<List<Message>> GetUnreadEmailsAsync()
    {
        var requestId = Guid.NewGuid().ToString();
        try
        {
            var sharedMailbox = _configuration["Email:SharedMailboxAddress"];
            var allMessages = new List<Message>();

            _logger.LogInformation("📧 [GRAPH] Récupération des emails non lus de: {Mailbox} (RequestId: {RequestId})",
                sharedMailbox, requestId);

            var messages = await _graphClient.Users[sharedMailbox]
                .MailFolders["inbox"]
                .Messages
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = "isRead eq false";
                    requestConfiguration.QueryParameters.Top = 50;
                    requestConfiguration.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                    requestConfiguration.QueryParameters.Select = new[]
                    {
                        "id", "subject", "from", "body", "receivedDateTime", "hasAttachments"
                    };
                    requestConfiguration.Headers.Add("client-request-id", requestId);
                });

            if (messages?.Value == null)
            {
                return new List<Message>();
            }

            // Ajouter la première page
            allMessages.AddRange(messages.Value);
            _logger.LogDebug("📧 [GRAPH] Première page: {Count} emails récupérés", messages.Value.Count);

            // Parcourir les pages suivantes s'il y en a
            var pageNumber = 1;
            while (!string.IsNullOrEmpty(messages.OdataNextLink))
            {
                pageNumber++;
                _logger.LogDebug("📧 [GRAPH] Récupération de la page {PageNumber}...", pageNumber);

                messages = await _graphClient.Users[sharedMailbox]
                    .MailFolders["inbox"]
                    .Messages
                    .WithUrl(messages.OdataNextLink)
                    .GetAsync();

                if (messages?.Value != null)
                {
                    allMessages.AddRange(messages.Value);
                    _logger.LogDebug("📧 [GRAPH] Page {PageNumber}: {Count} emails supplémentaires", pageNumber, messages.Value.Count);
                }
            }

            _logger.LogInformation("📧 [GRAPH] ✅ Total: {Count} emails non lus récupérés (RequestId: {RequestId})", allMessages.Count, requestId);
            return allMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving emails from shared mailbox. RequestId: {RequestId}", requestId);
            return new List<Message>();
        }
    }

    public async Task MoveEmailToFolderAsync(string messageId, string folderName)
    {
        try
        {
            var sharedMailbox = _configuration["Email:SharedMailboxAddress"];
            string? targetFolderId = null;

            _logger.LogInformation("📁 [GRAPH] Déplacement de l'email {EmailId} vers 'Boîte de réception > {FolderName}'",
                messageId, folderName);

            // Vérifier le cache d'abord
            var cacheKey = $"inbox_{folderName}";
            if (_folderCache.TryGetValue(cacheKey, out var cachedFolderId))
            {
                targetFolderId = cachedFolderId;
                _logger.LogDebug("📁 [GRAPH] Utilisation du cache pour 'Inbox > {FolderName}' (ID: {FolderId})", folderName, cachedFolderId);
            }
            else
            {
                // Récupérer les sous-dossiers de la boîte de réception (inbox)
                _logger.LogDebug("📁 [GRAPH] Recherche du dossier '{FolderName}' dans les sous-dossiers de Inbox...", folderName);

                var childFolders = await _graphClient.Users[sharedMailbox]
                    .MailFolders["inbox"]
                    .ChildFolders
                    .GetAsync();

                var targetFolder = childFolders?.Value?
                    .FirstOrDefault(f => f.DisplayName?.Equals(folderName, StringComparison.OrdinalIgnoreCase) == true);

                if (targetFolder == null)
                {
                    _logger.LogWarning("📁 [GRAPH] ⚠️ Dossier 'Inbox > {FolderName}' introuvable. Création en cours...", folderName);
                    var newFolder = await _graphClient.Users[sharedMailbox]
                        .MailFolders["inbox"]
                        .ChildFolders
                        .PostAsync(new MailFolder { DisplayName = folderName });
                    targetFolder = newFolder;
                    _logger.LogInformation("📁 [GRAPH] ✅ Dossier 'Inbox > {FolderName}' créé (ID: {FolderId})", folderName, newFolder?.Id);
                }

                targetFolderId = targetFolder!.Id;

                // Mettre en cache l'ID du dossier
                _folderCache[cacheKey] = targetFolderId!;
                _logger.LogDebug("📁 [GRAPH] Dossier 'Inbox > {FolderName}' mis en cache (ID: {FolderId})", folderName, targetFolderId);
            }

            // Déplacer le message
            _logger.LogDebug("📁 [GRAPH] Appel API pour déplacer l'email...");
            await _graphClient.Users[sharedMailbox]
                .Messages[messageId]
                .Move
                .PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = targetFolderId
                });

            _logger.LogInformation("📁 [GRAPH] ✅ Email {EmailId} déplacé vers '{FolderName}'", messageId, folderName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving email {MessageId} to folder '{FolderName}'", messageId, folderName);
        }
    }

    public async Task MarkEmailAsReadAsync(string messageId)
    {
        try
        {
            var sharedMailbox = _configuration["Email:SharedMailboxAddress"];

            _logger.LogDebug("✉️ [GRAPH] Marquage de l'email {EmailId} comme lu...", messageId);

            var message = new Message
            {
                IsRead = true
            };

            await _graphClient.Users[sharedMailbox]
                .Messages[messageId]
                .PatchAsync(message);

            _logger.LogInformation("✉️ [GRAPH] ✅ Email {EmailId} marqué comme lu", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking email {MessageId} as read", messageId);
        }
    }

    public async Task ApplyCategoryToEmailAsync(string messageId, string category)
    {
        try
        {
            var sharedMailbox = _configuration["Email:SharedMailboxAddress"];

            _logger.LogInformation("🏷️ [GRAPH] Application de la catégorie '{Category}' à l'email {EmailId}", category, messageId);

            var message = new Message
            {
                Categories = new List<string> { category }
            };

            await _graphClient.Users[sharedMailbox]
                .Messages[messageId]
                .PatchAsync(message);

            _logger.LogInformation("🏷️ [GRAPH] ✅ Catégorie '{Category}' appliquée à l'email {EmailId}", category, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying category '{Category}' to email {MessageId}", category, messageId);
        }
    }

    public async Task InitializeCategoriesAsync()
    {
        var sharedMailbox = _configuration["Email:SharedMailboxAddress"];

        _logger.LogInformation("🎨 [GRAPH] Initialisation des catégories avec couleurs...");

        // Définir les catégories avec leurs couleurs
        var categories = new Dictionary<string, Microsoft.Graph.Models.CategoryColor>
        {
            { "TECHNIQUE", Microsoft.Graph.Models.CategoryColor.Preset0 },      // Rouge
            { "DEMANDE", Microsoft.Graph.Models.CategoryColor.Preset2 },        // Vert
            { "ALERTE", Microsoft.Graph.Models.CategoryColor.Preset1 },         // Orange
            { "ADMINISTRATIF", Microsoft.Graph.Models.CategoryColor.Preset3 },  // Bleu
            { "FOURNISSEUR", Microsoft.Graph.Models.CategoryColor.Preset4 },    // Violet
            { "INFORMATION", Microsoft.Graph.Models.CategoryColor.Preset5 },    // Jaune
            { "AUTRE", Microsoft.Graph.Models.CategoryColor.Preset8 },          // Gris
            { "REPONSE", Microsoft.Graph.Models.CategoryColor.Preset6 }         // Cyan - Réponses à nos demandes
        };

        // Récupérer les catégories existantes
        var existingCategories = await _graphClient.Users[sharedMailbox]
            .Outlook
            .MasterCategories
            .GetAsync();

        var existingCategoryNames = existingCategories?.Value?
            .Select(c => c.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

        _logger.LogInformation("🎨 [GRAPH] {Count} catégories existantes trouvées: {Categories}",
            existingCategoryNames.Count,
            string.Join(", ", existingCategoryNames));

        foreach (var category in categories)
        {
            if (!existingCategoryNames.Contains(category.Key))
            {
                // Créer la catégorie avec sa couleur
                await _graphClient.Users[sharedMailbox]
                    .Outlook
                    .MasterCategories
                    .PostAsync(new Microsoft.Graph.Models.OutlookCategory
                    {
                        DisplayName = category.Key,
                        Color = category.Value
                    });

                _logger.LogInformation("🎨 [GRAPH] ✅ Catégorie '{Category}' créée avec couleur {Color}",
                    category.Key, category.Value);
            }
            else
            {
                _logger.LogInformation("🎨 [GRAPH] ℹ️ Catégorie '{Category}' existe déjà", category.Key);
            }
        }

        _logger.LogInformation("🎨 [GRAPH] ✅ Initialisation des catégories terminée");
    }
}
