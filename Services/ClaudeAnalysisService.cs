using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using EmailTicketingService.Models;
using GraphMessage = Microsoft.Graph.Models.Message;

namespace EmailTicketingService.Services;

public class ClaudeAnalysisService : IEmailAnalysisService
{
    private readonly AnthropicClient _anthropicClient;
    private readonly ILogger<ClaudeAnalysisService> _logger;
    private readonly int _serviceId;

    public ClaudeAnalysisService(
        IConfiguration configuration,
        ILogger<ClaudeAnalysisService> logger)
    {
        _logger = logger;
        var apiKey = configuration["Claude:ApiKey"];
        _anthropicClient = new AnthropicClient(new APIAuthentication(apiKey));
        _serviceId = configuration.GetValue<int>("Email:ServiceId", 2);
    }

    public async Task<EmailAnalysisResult?> AnalyzeEmailAsync(GraphMessage email)
    {
        try
        {
            var fromAddress = email.From?.EmailAddress?.Address ?? "unknown";
            var subject = email.Subject ?? "No subject";
            var bodyContent = email.Body?.Content ?? "No content";

            var prompt = $@"Tu analyses des emails entrants pour un prestataire informatique afin de décider s'ils doivent devenir un ticket de support.

À partir de l'email ci-dessous, retourne UNIQUEMENT un JSON valide avec exactement ces champs :
- demandeur_email
- probleme (résumé clair en 1 à 2 lignes maximum)
- personne_contact (nom de la personne si identifiable, sinon null)
- tel_contact (numéro de téléphone si présent dans le mail, sinon null)
- service_id (toujours la valeur {_serviceId})
- urgence (true si le mail exprime une urgence : panne bloquante, arrêt d'activité, mots comme ""urgent"", ""critique"", ""immédiat"" ; sinon false)
- creer_ticket (true ou false)
- categorie (une des catégories suivantes)

Règles pour creer_ticket (IMPORTANT - Bien distinguer demande humaine vs notification auto) :
- ✅ creer_ticket = true UNIQUEMENT si :
  * Email provenant d'une PERSONNE (client, utilisateur, prestataire) qui demande de l'aide, une action ou pose une question
  * Demande explicite nécessitant une intervention humaine
  * Problème signalé par un utilisateur

- ❌ creer_ticket = false pour :
  * Notifications automatiques de systèmes (Sophos, Veeam, monitoring, etc.)
  * Rapports automatiques (sauvegardes, surveillance, alertes système)
  * Confirmations de commandes/système
  * Newsletters, marketing, propositions commerciales
  * Devis signés, factures, documents administratifs
  * Tout email provenant d'un système/robot sans demande humaine
  * Réponses fournissant des informations : si l'email est une RÉPONSE où le client fournit des informations demandées (identifiants, mots de passe, accès, codes, etc.), ce n'est PAS une demande d'assistance
  * RÉPONSES À NOS DEMANDES : si l'email est une réponse (sujet avec RE:, Re:, Rép:) à un email initialement envoyé par une adresse @acskm.fr (visible dans les citations du message ou l'historique), ce n'est PAS une demande de support - c'est une réponse à notre propre demande. Utilise categorie = ""REPONSE""

Exemples :
- ""[MOYENNE] Alerte Sophos : appareil non chiffré"" → creer_ticket = false (notification auto, catégorie ALERTE)
- ""Votre commande CB019637 validée"" → creer_ticket = false (confirmation auto, catégorie COMMANDE)
- ""Bonjour, j'ai un problème avec mon imprimante"" → creer_ticket = true (demande humaine)
- ""Le serveur ne répond plus, pouvez-vous vérifier ?"" → creer_ticket = true (demande humaine urgente)
- ""RE: Demande d'identifiants - Voici les identifiants demandés : user/password123"" → creer_ticket = false (pas une demande d'assistance, catégorie INFORMATION)
- ""RE: Demande de devis"" avec citation ""De: pierre@acskm.fr ... Bonjour, pouvez-vous nous faire un devis..."" → creer_ticket = false (réponse à notre demande initiée par @acskm.fr, catégorie REPONSE)

Autres règles :
- Si creer_ticket = false, urgence doit être false
- Si une information n'est pas trouvée (nom ou téléphone), retourne null

Catégories disponibles (utilise EXACTEMENT ces valeurs) :
- ""TECHNIQUE"" : Incidents techniques, pannes, problèmes système (mais sans ticket car notification auto)
- ""DEMANDE"" : Demandes de service, questions (mais sans ticket car pas urgentes ou auto-traitables)
- ""ALERTE"" : Alertes de monitoring, sécurité, systèmes (ex: Sophos, Veeam, surveillance)
- ""ADMINISTRATIF"" : Factures, devis, commandes, documents administratifs
- ""FOURNISSEUR"" : Communications des fournisseurs/prestataires
- ""INFORMATION"" : Notifications, confirmations, rapports automatiques
- ""REPONSE"" : Réponses à des emails que NOUS avons envoyés (initiés par @acskm.fr)
- ""AUTRE"" : Si aucune catégorie ne correspond

Important pour la catégorie :
- Analyse le sujet et le contenu pour déterminer la catégorie la plus pertinente
- Choisis UNE SEULE catégorie parmi la liste ci-dessus
- Si creer_ticket = true, la catégorie n'est pas importante (elle ne sera pas utilisée)

IMPORTANT (OBLIGATOIRE) :
- Réponds UNIQUEMENT avec un JSON brut
- N'utilise PAS de balises markdown
- N'utilise PAS le mot json
- Le premier caractère de ta réponse doit être {{ et le dernier }}
- Ne mets absolument aucun texte en dehors du JSON

EMAIL :
Expéditeur : {fromAddress}
Sujet : {subject}
Contenu : {bodyContent}";

            var messages = new List<Message>
            {
                new Message(RoleType.User, prompt)
            };

            var parameters = new MessageParameters
            {
                Messages = messages,
                MaxTokens = 1024,
                Model = "claude-haiku-4-5-20251001", // Claude Haiku 4.5 - Rapide et économique
                Stream = false,
                Temperature = 0.0m
            };

            var response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters);

            if (response?.Content == null || !response.Content.Any())
            {
                _logger.LogWarning("Empty response from Claude API");
                return null;
            }

            var contentBlock = response.Content.First();
            var jsonResponse = contentBlock switch
            {
                Anthropic.SDK.Messaging.TextContent textContent => textContent.Text,
                _ => contentBlock.ToString() ?? ""
            };

            // Nettoyer la réponse si elle contient des balises markdown
            jsonResponse = jsonResponse?.Trim() ?? "";
            if (jsonResponse.StartsWith("```json"))
            {
                jsonResponse = jsonResponse.Replace("```json", "");
                jsonResponse = jsonResponse.Replace("```", "");
                jsonResponse = jsonResponse.Trim();
            }
            else if (jsonResponse.StartsWith("```"))
            {
                jsonResponse = jsonResponse.Replace("```", "");
                jsonResponse = jsonResponse.Trim();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<EmailAnalysisResult>(jsonResponse, options);

            if (result != null)
            {
                _logger.LogInformation(
                    "Email analyzed - Create ticket: {CreateTicket}, Urgent: {Urgent}",
                    result.CreerTicket, result.Urgence);
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing Claude API response as JSON");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing email with Claude API");
            return null;
        }
    }
}
