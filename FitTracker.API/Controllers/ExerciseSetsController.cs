using AutoMapper;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExerciseSetsController : ControllerBase
    {
        private readonly IExerciseSetRepository exerciseSetRepository;
        private readonly IMapper mapper;

        public ExerciseSetsController(IExerciseSetRepository exerciseSetRepository, IMapper mapper)
        {
            this.exerciseSetRepository = exerciseSetRepository;
            this.mapper = mapper;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var exerciseSets = await exerciseSetRepository.GetAllAsync();

            return Ok(mapper.Map<List<ExerciseSetDto>>(exerciseSets));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var exerciseSet = await exerciseSetRepository.GetByIdAsync(id);

            if (exerciseSet == null)
                return NotFound();

            return Ok(mapper.Map<ExerciseSetDto>(exerciseSet));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ExerciseSetRequestDto exerciseSetRequestDto)
        {
            var exerciseSetDomainModel = mapper.Map<ExerciseSet>(exerciseSetRequestDto);

            var createdExerciseSet = await exerciseSetRepository.CreateAsync(exerciseSetDomainModel);

            return CreatedAtAction(nameof(GetById), new { id = createdExerciseSet.Id }, mapper.Map<ExerciseSetDto>(createdExerciseSet));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateExerciseSetRequestDto updateExerciseSetRequestDto)
        {
            var exerciseSetDomainModel = mapper.Map<ExerciseSet>(updateExerciseSetRequestDto);

            var updatedExerciseSet = await exerciseSetRepository.UpdateAsync(id, exerciseSetDomainModel);

            if (updatedExerciseSet == null)
                return NotFound();

            return Ok(mapper.Map<ExerciseSetDto>(updatedExerciseSet));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var deletedExerciseSet = await exerciseSetRepository.DeleteAsync(id);

            if (deletedExerciseSet == null)
                return NotFound();

            return Ok(mapper.Map<ExerciseSetDto>(deletedExerciseSet));
        }
    }
}
