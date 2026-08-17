using EmailTicketingService.Models;
using GraphMessage = Microsoft.Graph.Models.Message;

namespace EmailTicketingService.Services;

/// <summary>
/// Interface pour les services d'analyse d'email par IA
/// Peut être implémentée par Claude, Gemini, ou d'autres providers
/// </summary>
public interface IEmailAnalysisService
{
    /// <summary>
    /// Analyse un email et détermine s'il nécessite la création d'un ticket
    /// </summary>
    /// <param name="email">L'email à analyser</param>
    /// <returns>Le résultat de l'analyse ou null en cas d'erreur</returns>
    Task<EmailAnalysisResult?> AnalyzeEmailAsync(GraphMessage email);
}
