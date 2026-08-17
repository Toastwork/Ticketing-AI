using EmailTicketingService;
using EmailTicketingService.Data;
using EmailTicketingService.Services;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;

var builder = Host.CreateApplicationBuilder(args);

// Configuration du logging avec horodatage
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
    options.SingleLine = true;
});

// Configuration de la base de données SQLite
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    var dbPath = builder.Configuration["Database:Path"] ?? "emails.db";
    options.UseSqlite($"Data Source={dbPath}");
});

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("database", () =>
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Database OK"));

// Enregistrement des services
builder.Services.AddSingleton<GraphEmailService>();
builder.Services.AddSingleton<EmailTrackingService>();

// Auto-détection du provider d'IA basée sur les clés API disponibles
var geminiApiKey = builder.Configuration["Gemini:ApiKey"];
var claudeApiKey = builder.Configuration["Claude:ApiKey"];

var hasGemini = !string.IsNullOrWhiteSpace(geminiApiKey) &&
                !geminiApiKey.StartsWith("YOUR_") &&
                !geminiApiKey.StartsWith("your-");
var hasClaude = !string.IsNullOrWhiteSpace(claudeApiKey) &&
                !claudeApiKey.StartsWith("YOUR_") &&
                !claudeApiKey.StartsWith("your-");

if (hasGemini && hasClaude)
{
    // Les deux clés sont disponibles : Gemini en priorité, Claude en fallback
    builder.Services.AddHttpClient<GeminiAnalysisService>();
    builder.Services.AddSingleton<ClaudeAnalysisService>();
    builder.Services.AddSingleton<IEmailAnalysisService, CompositeAnalysisService>();
    Console.WriteLine("✓ AI Provider: Gemini (gemini-2.0-flash) avec fallback Claude (claude-haiku-4-5)");
}
else if (hasGemini)
{
    // Seule Gemini est disponible
    builder.Services.AddHttpClient<GeminiAnalysisService>();
    builder.Services.AddSingleton<IEmailAnalysisService, GeminiAnalysisService>();
    Console.WriteLine("✓ AI Provider: Gemini (gemini-2.0-flash)");
}
else if (hasClaude)
{
    // Seul Claude est disponible
    builder.Services.AddSingleton<IEmailAnalysisService, ClaudeAnalysisService>();
    Console.WriteLine("✓ AI Provider: Claude (claude-haiku-4-5)");
}
else
{
    throw new InvalidOperationException("Aucune clé API valide trouvée. Veuillez configurer soit Gemini:ApiKey soit Claude:ApiKey dans votre fichier .env");
}

// HttpClient pour l'API de ticketing avec politique de retry
builder.Services.AddHttpClient<TicketingApiService>((serviceProvider, client) =>
    {
        var config = serviceProvider.GetRequiredService<IConfiguration>();

        var token = config["TicketingApi:Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
        }
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Retry {RetryCount} for Ticketing API after {Delay}s. Reason: {Reason}",
                    retryCount, timespan.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
            }));

// Enregistrement du Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Valider la configuration au démarrage
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var configuration = host.Services.GetRequiredService<IConfiguration>();

logger.LogInformation("Validation de la configuration...");

var requiredSettings = new Dictionary<string, string>
{
    { "AzureAd:TenantId", configuration["AzureAd:TenantId"] ?? "" },
    { "AzureAd:ClientId", configuration["AzureAd:ClientId"] ?? "" },
    { "AzureAd:ClientSecret", configuration["AzureAd:ClientSecret"] ?? "" },
    { "Email:SharedMailboxAddress", configuration["Email:SharedMailboxAddress"] ?? "" },
    { "TicketingApi:BaseUrl", configuration["TicketingApi:BaseUrl"] ?? "" },
    { "TicketingApi:Token", configuration["TicketingApi:Token"] ?? "" }
};

// Validation : au moins une clé API doit être présente
var geminiKey = configuration["Gemini:ApiKey"] ?? "";
var claudeKey = configuration["Claude:ApiKey"] ?? "";
var hasValidGemini = !string.IsNullOrWhiteSpace(geminiKey) && !geminiKey.StartsWith("YOUR_") && !geminiKey.StartsWith("your-");
var hasValidClaude = !string.IsNullOrWhiteSpace(claudeKey) && !claudeKey.StartsWith("YOUR_") && !claudeKey.StartsWith("your-");

if (!hasValidGemini && !hasValidClaude)
{
    requiredSettings.Add("Gemini:ApiKey ou Claude:ApiKey", "");
}

var missingSettings = requiredSettings.Where(kvp =>
    string.IsNullOrWhiteSpace(kvp.Value) ||
    kvp.Value.StartsWith("YOUR_") ||
    kvp.Value.StartsWith("your-")).ToList();

if (missingSettings.Any())
{
    logger.LogCritical("Configuration invalide - Paramètres manquants :");
    foreach (var setting in missingSettings)
    {
        logger.LogCritical("  - {SettingName}", setting.Key);
    }
    logger.LogCritical("Veuillez configurer tous les paramètres requis dans .env");
    return;
}

logger.LogInformation("Configuration validée");
logger.LogInformation("Boîte mail: {Mailbox}", configuration["Email:SharedMailboxAddress"]);
logger.LogInformation("Service ID: {ServiceId}", configuration.GetValue<int>("Email:ServiceId", 2));
logger.LogInformation("Intervalle de polling: {Minutes} minute(s)", configuration["Email:PollingIntervalMinutes"]);

// Vérifier les health checks au démarrage
logger.LogInformation("Vérification des systèmes...");

var healthCheckService = host.Services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
var healthReport = await healthCheckService.CheckHealthAsync();

if (healthReport.Status != Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
{
    logger.LogWarning("Health checks: Problèmes détectés");
    foreach (var entry in healthReport.Entries)
    {
        if (entry.Value.Status != Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
        {
            logger.LogWarning("  {HealthCheckName}: {Status} - {Description}",
                entry.Key, entry.Value.Status, entry.Value.Description);
        }
    }
}
else
{
    logger.LogInformation("Health checks: OK");
}

// Créer la base de données si elle n'existe pas
logger.LogInformation("Initialisation de la base de données...");
using (var scope = host.Services.CreateScope())
{
    var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    await using var context = await contextFactory.CreateDbContextAsync();
    await context.Database.EnsureCreatedAsync();
}
logger.LogInformation("Base de données initialisée");

// Initialiser les catégories Outlook avec leurs couleurs
logger.LogInformation("Initialisation des catégories Outlook...");
try
{
    using (var scope = host.Services.CreateScope())
    {
        var graphEmailService = scope.ServiceProvider.GetRequiredService<GraphEmailService>();
        await graphEmailService.InitializeCategoriesAsync();
    }
    logger.LogInformation("Catégories Outlook initialisées");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Impossible d'initialiser les catégories Outlook (limitation API pour shared mailbox). Le service continuera sans catégories pré-créées.");
}

logger.LogInformation("");
logger.LogInformation("Email Ticketing Service - Démarrage");
logger.LogInformation("Surveillance de la boîte mail activée");
logger.LogInformation("");

host.Run();
