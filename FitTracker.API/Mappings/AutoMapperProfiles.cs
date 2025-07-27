using AutoMapper;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Mappings
{
    public class AutoMapperProfiles : Profile
    {
        public AutoMapperProfiles()
        {
            CreateMap<Exercise, ExerciseDto>().ReverseMap();
            CreateMap<ExerciseRequestDto, Exercise>().ReverseMap();
            CreateMap<UpdateExerciseRequestDto, Exercise>().ReverseMap();
            CreateMap<WorkoutRequestDto, Workout>().ReverseMap();
            CreateMap<Workout, WorkoutDto>().ReverseMap();
            CreateMap<Location, LocationDto>().ReverseMap();
            CreateMap<Intensity, IntensityDto>().ReverseMap();
            CreateMap<Workout, WorkoutSummaryDto>().ReverseMap();
            CreateMap<UpdateWorkoutRequestDto, Workout>().ReverseMap();
        }
    }
}
