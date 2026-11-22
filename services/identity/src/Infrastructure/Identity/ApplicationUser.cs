using Microsoft.AspNetCore.Identity;

namespace IdentityService.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string FullName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EmailConfirmedAt { get; set; }

    /// <summary>Son üyeliğin bittiği an; firmasız kullanıcı temizliği sayacının başlangıcı.</summary>
    public DateTimeOffset? LastMembershipEndedAt { get; set; }

    public DateTimeOffset? CleanupReminderSentAt { get; set; }

    public Guid? LastActiveTenantId { get; set; }
}
