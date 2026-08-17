using System.Text.RegularExpressions;
using EmailTicketingService.Models;
using EmailTicketingService.Services;

namespace EmailTicketingService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly GraphEmailService _graphEmailService;
    private readonly IEmailAnalysisService _emailAnalysisService;
    private readonly TicketingApiService _ticketingApiService;
    private readonly EmailTrackingService _emailTrackingService;
    private readonly IConfiguration _configuration;
    private readonly DateTimeOffset _serviceStartTime;

    public Worker(
        ILogger<Worker> logger,
        GraphEmailService graphEmailService,
        IEmailAnalysisService emailAnalysisService,
        TicketingApiService ticketingApiService,
        EmailTrackingService emailTrackingService,
        IConfiguration configuration)
    {
        _logger = logger;
        _graphEmailService = graphEmailService;
        _emailAnalysisService = emailAnalysisService;
        _ticketingApiService = ticketingApiService;
        _emailTrackingService = emailTrackingService;
        _configuration = configuration;
        _serviceStartTime = DateTimeOffset.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email Ticketing Service démarré à {StartTime} (UTC)", _serviceStartTime);
        _logger.LogInformation("Traitement des emails reçus après {StartTime}", _serviceStartTime);

        // Intervalle de polling en minutes (configurable)
        var pollingIntervalMinutes = _configuration.GetValue<int>("Email:PollingIntervalMinutes", 5);

        // Configuration des heures de travail
        var workingHoursEnabled = _configuration.GetValue<bool>("Email:WorkingHours:Enabled", false);
        var startHour = _configuration.GetValue<int>("Email:WorkingHours:StartHour", 8);
        var endHour = _configuration.GetValue<int>("Email:WorkingHours:EndHour", 18);
        var timeZoneId = _configuration.GetValue<string>("Email:WorkingHours:TimeZone") ?? "Romance Standard Time";

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("Fuseau horaire '{TimeZone}' non trouvé, utilisation de UTC", timeZoneId);
            timeZone = TimeZoneInfo.Utc;
        }

        if (workingHoursEnabled)
        {
            _logger.LogInformation("Heures de travail activées: {Start}h - {End}h ({TimeZone})",
                startHour, endHour, timeZone.DisplayName);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Vérifier si on est dans les heures de travail
                if (workingHoursEnabled)
                {
                    var waitTime = GetWaitTimeUntilWorkingHours(timeZone, startHour, endHour);
                    if (waitTime.HasValue)
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
                        _logger.LogInformation(
                            "Hors des heures de travail ({CurrentTime:HH:mm} {TimeZone}). Prochaine vérification à {StartHour}h00. Attente de {WaitMinutes} minutes.",
                            localTime, timeZone.StandardName, startHour, (int)waitTime.Value.TotalMinutes);

                        await Task.Delay(waitTime.Value, stoppingToken);
                        continue;
                    }
                }

                _logger.LogInformation("Début du cycle de polling ({Time})", DateTimeOffset.Now);

                // 1. Récupérer les emails non lus
                var unreadEmails = await _graphEmailService.GetUnreadEmailsAsync();

                if (unreadEmails.Count > 0)
                {
                    _logger.LogInformation("{Count} email(s) non lu(s) trouvé(s)", unreadEmails.Count);
                }

                var processedCount = 0;
                var skippedCount = 0;

                foreach (var email in unreadEmails)
                {
                    if (string.IsNullOrEmpty(email.Id))
                    {
                        _logger.LogWarning("⚠️ Email sans ID trouvé, ignoré");
                        skippedCount++;
                        continue;
                    }

                    _logger.LogInformation("Traitement de l'email: {Subject} (De: {From})",
                        email.Subject ?? "(Sans sujet)",
                        email.From?.EmailAddress?.Address ?? "unknown");

                    try
                    {
                        // 2. Vérifier si l'email a été reçu APRÈS le démarrage du service
                        if (email.ReceivedDateTime.HasValue && email.ReceivedDateTime.Value < _serviceStartTime)
                        {
                            _logger.LogInformation("⏭️ Email ignoré (reçu le {ReceivedAt} avant le démarrage du service {StartTime})",
                                email.ReceivedDateTime.Value, _serviceStartTime);
                            skippedCount++;
                            continue;
                        }

                        // 3. Vérifier si l'email a déjà été traité
                        if (await _emailTrackingService.IsEmailProcessedAsync(email.Id))
                        {
                            _logger.LogInformation("⏭️ Email ignoré (déjà traité dans la base de données)");
                            skippedCount++;
                            continue;
                        }

                        // 4. Vérifier si l'email provient de SuperOps (création directe de ticket)
                        var senderAddress = email.From?.EmailAddress?.Address ?? "";
                        EmailAnalysisResult? analysisResult;

                        if (senderAddress.Equals("support@groupeepikure.superops.ai", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extraire le numéro de ticket SuperOps depuis le sujet (ex: "New Ticket created - 28")
                            var subject = email.Subject ?? "Ticket SuperOps";
                            var superOpsMatch = Regex.Match(subject, @"New Ticket\s+created\s*-\s*(\d+)", RegexOptions.IgnoreCase);
                            var probleme = superOpsMatch.Success
                                ? $"#{superOpsMatch.Groups[1].Value} {subject}"
                                : subject;

                            _logger.LogInformation("📨 Email SuperOps détecté - Création directe du ticket pour Groupe Epikure (Problème: {Probleme})", probleme);
                            analysisResult = new EmailAnalysisResult
                            {
                                DemandeurEmail = "support@groupe-epikur.com",
                                Probleme = probleme,
                                PersonneContact = null,
                                TelContact = null,
                                ServiceId = _configuration.GetValue<int>("Email:ServiceId", 2),
                                Urgence = false,
                                CreerTicket = true,
                                Categorie = null
                            };
                        }
                        else
                        {
                            // Analyser l'email avec l'IA (Claude ou Gemini)
                            // L'IA détecte automatiquement les réponses à nos demandes (@acskm.fr) via le prompt
                            analysisResult = await _emailAnalysisService.AnalyzeEmailAsync(email);

                            if (analysisResult == null)
                            {
                                _logger.LogWarning("Failed to analyze email {EmailId}, skipping", email.Id);
                                continue;
                            }
                        }

                        // 5. Décider de créer un ticket ou non
                        if (analysisResult.CreerTicket)
                        {
                            _logger.LogInformation("Création d'un ticket - Urgence: {Urgent}, Problème: {Problem}",
                                analysisResult.Urgence ? "OUI" : "Non", analysisResult.Probleme);

                            // 6. Créer le ticket via l'API
                            var ticketReference = await _ticketingApiService.CreateTicketAsync(analysisResult);

                            if (ticketReference != null)
                            {
                                _logger.LogInformation("Ticket créé avec succès (Référence: {Reference})", ticketReference);

                                // 7. Déplacer l'email dans le dossier "Tickets" (reste non lu)
                                await _graphEmailService.MoveEmailToFolderAsync(email.Id, "Tickets");

                                // 8. Marquer l'email comme traité dans la base de données
                                await _emailTrackingService.MarkEmailAsProcessedAsync(
                                    email.Id, true, ticketReference);

                                processedCount++;
                            }
                            else
                            {
                                _logger.LogError("Échec de la création du ticket pour l'email {EmailId}", email.Id);
                                // On marque quand même comme traité pour éviter de reboucler
                                await _emailTrackingService.MarkEmailAsProcessedAsync(email.Id, false);
                                skippedCount++;
                            }
                        }
                        else
                        {
                            _logger.LogInformation("Ticket non nécessaire - Catégorie: {Category}",
                                analysisResult.Categorie ?? "AUTRE");

                            // Appliquer la catégorie à l'email (reste non lu en boîte de réception)
                            if (!string.IsNullOrWhiteSpace(analysisResult.Categorie))
                            {
                                await _graphEmailService.ApplyCategoryToEmailAsync(email.Id, analysisResult.Categorie);
                            }

                            // Marquer comme traité dans la base de données
                            await _emailTrackingService.MarkEmailAsProcessedAsync(email.Id, false);

                            processedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erreur lors du traitement de l'email {EmailId}", email.Id);

                        // Marquer l'email avec l'erreur pour retry ultérieur
                        // (Ne pas marquer comme traité pour permettre un nouveau traitement)
                        // Limite à 3 tentatives avant d'abandonner définitivement
                        await _emailTrackingService.MarkEmailAsProcessedAsync(
                            email.Id, false, null);
                        skippedCount++;
                    }
                }

                _logger.LogInformation("Cycle terminé - Trouvés: {Total}, Traités: {Processed}, Ignorés: {Skipped}",
                    unreadEmails.Count, processedCount, skippedCount);
                _logger.LogInformation("Prochain cycle dans {Minutes} minute(s)", pollingIntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur critique pendant le cycle de polling");
            }

            // Attendre avant le prochain cycle
            await Task.Delay(TimeSpan.FromMinutes(pollingIntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("Email Ticketing Service arrêté");
    }

    /// <summary>
    /// Calcule le temps d'attente jusqu'aux heures de travail.
    /// Retourne null si on est dans les heures de travail, sinon le TimeSpan à attendre.
    /// </summary>
    private static TimeSpan? GetWaitTimeUntilWorkingHours(TimeZoneInfo timeZone, int startHour, int endHour)
    {
        var utcNow = DateTime.UtcNow;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        var currentHour = localTime.Hour;

        // Si on est dans les heures de travail, pas d'attente
        if (currentHour >= startHour && currentHour < endHour)
        {
            return null;
        }

        // Calculer la prochaine heure de début
        DateTime nextStart;
        if (currentHour < startHour)
        {
            // Avant les heures de travail aujourd'hui
            nextStart = localTime.Date.AddHours(startHour);
        }
        else
        {
            // Après les heures de travail, attendre demain
            nextStart = localTime.Date.AddDays(1).AddHours(startHour);
        }

        // Convertir en UTC pour calculer la différence
        var nextStartUtc = TimeZoneInfo.ConvertTimeToUtc(nextStart, timeZone);
        return nextStartUtc - utcNow;
    }
}
