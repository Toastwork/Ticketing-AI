using System.ComponentModel.DataAnnotations;

namespace EmailTicketingService.Models;

public class ProcessedEmail
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string EmailId { get; set; } = string.Empty;

    [Required]
    public DateTime ProcessedAt { get; set; }

    public bool TicketCreated { get; set; }

    public string? TicketReference { get; set; }

    public int RetryCount { get; set; } = 0;

    public DateTime? LastRetryAt { get; set; }

    public string? LastError { get; set; }
}
