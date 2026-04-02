using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Services.Interfaces
{
    public interface IWorkoutAnalysisService
    {
        Task<WorkoutAnalysisDto> GetAnalysisAsync(string userId);
    }
}


