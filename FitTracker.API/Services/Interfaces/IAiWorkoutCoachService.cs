using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Services.Interfaces
{
    public interface IAiWorkoutCoachService
    {
        Task<AiWorkoutInsightDto> GetInsightsAsync(string userId);
    }
}
