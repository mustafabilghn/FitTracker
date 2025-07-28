using AutoMapper;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WorkoutController : ControllerBase
    {
        private readonly IMapper mapper;
        private readonly IWorkoutRepository repository;

        public WorkoutController(IMapper mapper, IWorkoutRepository repository)
        {
            this.mapper = mapper;
            this.repository = repository;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WorkoutRequestDto requestDto, [FromServices] IValidator<WorkoutRequestDto> validator)
        {
            var validationError = await ValidateAsync(validator, requestDto);

            if (validationError is not null)
            {
                return validationError;
            }

            var workout = mapper.Map<Workout>(requestDto);

            var createdWorkout = await repository.CreateAsync(workout);

            var workoutDto = mapper.Map<WorkoutSummaryDto>(createdWorkout);

            return CreatedAtAction(nameof(GetById), new { id = workoutDto.Id }, workoutDto);
        }

        [HttpGet]
        [Route("{id:Guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var workout = await repository.GetByIdAsync(id);

            if (workout == null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<WorkoutDto>(workout));
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var workout = await repository.GetAllAsync();

            return Ok(mapper.Map<List<WorkoutSummaryDto>>(workout));
        }

        [HttpPut]
        [Route("{id:guid}")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWorkoutRequestDto requestDto, [FromServices] IValidator<UpdateWorkoutRequestDto> validator)
        {
            var validationError = await ValidateAsync(validator, requestDto);

            if (validationError is not null)
            {
                return validationError;
            }

            var workoutDomain = mapper.Map<Workout>(requestDto);

            var updatedWorkout = await repository.UpdateAsync(id, workoutDomain);

            if (updatedWorkout is null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<WorkoutSummaryDto>(updatedWorkout));
        }

        [HttpDelete]
        [Route("{id:guid}")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var workout = await repository.DeleteAsync(id);

            if (workout is null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<WorkoutDto>(workout));
        }

        private async Task<IActionResult> ValidateAsync<T>(IValidator<T> validator, T model)
        {
            var validationResult = await validator.ValidateAsync(model);

            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors.Select(e => new
                {
                    field = e.PropertyName,
                    error = e.ErrorMessage
                }));
            }

            return null;
        }
    }
}
