using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EmailTicketingService.Models;

namespace EmailTicketingService.Services;

public class TicketingApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TicketingApiService> _logger;
    private readonly int _serviceId;
    private readonly string _ticketsEndpoint;

    public TicketingApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TicketingApiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _serviceId = configuration.GetValue<int>("Email:ServiceId", 2);
        _ticketsEndpoint = configuration["TicketingApi:BaseUrl"]
            ?? throw new InvalidOperationException("TicketingApi:BaseUrl n'est pas configuré");
    }

    public async Task<string?> CreateTicketAsync(EmailAnalysisResult analysisResult)
    {
        try
        {

            var ticketRequest = new TicketRequest
            {
                DemandeurEmail = analysisResult.DemandeurEmail,
                Probleme = analysisResult.Probleme,
                PersonneContact = analysisResult.PersonneContact,
                TelContact = analysisResult.TelContact,
                ServiceId = _serviceId,
                Urgence = analysisResult.Urgence
            };

            var jsonContent = JsonSerializer.Serialize(ticketRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_ticketsEndpoint, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();

                // Essayer d'extraire l'ID du ticket depuis la réponse
                try
                {
                    var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    if (responseJson.TryGetProperty("ticket_id", out var ticketIdElement))
                    {
                        return ticketIdElement.ToString();
                    }
                    if (responseJson.TryGetProperty("id", out var idElement))
                    {
                        return idElement.ToString();
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Impossible de parser la réponse, mais ticket créé");
                }

                return "created";
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Échec de création du ticket - Status: {StatusCode}, Réponse: {Response}",
                    response.StatusCode, errorContent);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'appel à l'API de ticketing");
            return null;
        }
    }
}
