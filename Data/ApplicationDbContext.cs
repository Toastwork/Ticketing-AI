using EmailTicketingService.Models;
using Microsoft.EntityFrameworkCore;

namespace EmailTicketingService.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProcessedEmail> ProcessedEmails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcessedEmail>()
            .HasIndex(e => e.EmailId)
            .IsUnique();
    }
}
