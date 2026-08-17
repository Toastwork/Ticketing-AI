using EmailTicketingService.Data;
using EmailTicketingService.Models;
using Microsoft.EntityFrameworkCore;

namespace EmailTicketingService.Services;

public class EmailTrackingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<EmailTrackingService> _logger;

    public EmailTrackingService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<EmailTrackingService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<bool> IsEmailProcessedAsync(string emailId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ProcessedEmails.AnyAsync(e => e.EmailId == emailId);
    }

    public async Task MarkEmailAsProcessedAsync(string emailId, bool ticketCreated, string? ticketReference = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var processedEmail = new ProcessedEmail
        {
            EmailId = emailId,
            ProcessedAt = DateTime.UtcNow,
            TicketCreated = ticketCreated,
            TicketReference = ticketReference
        };

        context.ProcessedEmails.Add(processedEmail);

        try
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Email {EmailId} marked as processed (Ticket: {TicketCreated})",
                emailId, ticketCreated);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Email {EmailId} already marked as processed", emailId);
        }
    }
}
