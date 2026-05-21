using System.Threading.Tasks;

namespace FitTrackr.API.Services.Interfaces
{
    public interface ISubscriptionService
    {
        Task<bool> IsPremiumAsync(string userId);
    }
}
