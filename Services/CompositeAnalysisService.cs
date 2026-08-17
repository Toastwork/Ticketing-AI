using EmailTicketingService.Models;
using GraphMessage = Microsoft.Graph.Models.Message;

namespace EmailTicketingService.Services;

/// <summary>
/// Service d'analyse composite qui utilise Gemini en priorité et Claude en fallback.
/// </summary>
public class CompositeAnalysisService : IEmailAnalysisService
{
    private readonly GeminiAnalysisService _geminiService;
    private readonly ClaudeAnalysisService _claudeService;
    private readonly ILogger<CompositeAnalysisService> _logger;

    public CompositeAnalysisService(
        GeminiAnalysisService geminiService,
        ClaudeAnalysisService claudeService,
        ILogger<CompositeAnalysisService> logger)
    {
        _geminiService = geminiService;
        _claudeService = claudeService;
        _logger = logger;
    }

    public async Task<EmailAnalysisResult?> AnalyzeEmailAsync(GraphMessage email)
    {
        // Essayer Gemini d'abord
        try
        {
            var result = await _geminiService.AnalyzeEmailAsync(email);
            if (result != null)
            {
                return result;
            }

            _logger.LogWarning("Gemini a retourné un résultat null, tentative avec Claude...");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur avec Gemini, tentative avec Claude...");
        }

        // Fallback sur Claude
        try
        {
            return await _claudeService.AnalyzeEmailAsync(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur avec Claude également - échec de l'analyse");
            return null;
        }
    }
}
