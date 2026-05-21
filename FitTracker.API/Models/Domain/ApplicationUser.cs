using Microsoft.AspNetCore.Identity;

namespace FitTrackr.API.Models.Domain
{
    public class ApplicationUser : IdentityUser
    {
        // "free" | "premium" — default free, switched by ISubscriptionService implementation
        public string SubscriptionTier { get; set; } = "free";
    }
}
