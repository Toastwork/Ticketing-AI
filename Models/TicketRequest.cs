using System.Text.Json.Serialization;

namespace EmailTicketingService.Models;

public class TicketRequest
{
    [JsonPropertyName("demandeur_email")]
    public string DemandeurEmail { get; set; } = string.Empty;

    [JsonPropertyName("probleme")]
    public string Probleme { get; set; } = string.Empty;

    [JsonPropertyName("personne_contact")]
    public string? PersonneContact { get; set; }

    [JsonPropertyName("tel_contact")]
    public string? TelContact { get; set; }

    [JsonPropertyName("service_id")]
    public int ServiceId { get; set; }

    [JsonPropertyName("urgence")]
    public bool Urgence { get; set; }
}
