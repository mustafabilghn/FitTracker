using System.Threading.Tasks;
using FitTrackr.API.Services.Interfaces;

namespace FitTrackr.API.Services
{
    // Stub: everyone is premium. When freemium goes live, replace this with a real
    // implementation that reads ApplicationUser.SubscriptionTier from the DB.
    public class AlwaysPremiumSubscriptionService : ISubscriptionService
    {
        public Task<bool> IsPremiumAsync(string userId) => Task.FromResult(true);
    }
}
