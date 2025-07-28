using AutoMapper;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExerciseController : ControllerBase
    {
        private readonly IExerciseRepository exerciseRepository;
        private readonly IMapper mapper;

        public ExerciseController(IExerciseRepository exerciseRepository, IMapper mapper)
        {
            this.exerciseRepository = exerciseRepository;
            this.mapper = mapper;
        }

        [HttpGet]
        [Route("{id:Guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var exercise = await exerciseRepository.GetByIdAsync(id);

            if (exercise == null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<ExerciseDto>(exercise));
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var exercises = await exerciseRepository.GetAllAsync();

            if (exercises == null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<List<ExerciseDto>>(exercises));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ExerciseRequestDto requestDto, IValidator<ExerciseRequestDto> validator)
        {
            var validationError = await ValidateAsync(validator, requestDto);

            if (validationError is not null)
            {
                return validationError;
            }

            var exercise = mapper.Map<Exercise>(requestDto);

            var createdExercise = await exerciseRepository.CreateAsync(exercise);

            var exerciseDto = mapper.Map<ExerciseDto>(createdExercise);

            return CreatedAtAction(nameof(GetById), new { id = exerciseDto.Id }, exerciseDto);
        }

        [HttpPut]
        [Route("{id:Guid}")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateExerciseRequestDto exerciseRequestDto, [FromServices] IValidator<UpdateExerciseRequestDto> validator)
        {
            var validationError = await ValidateAsync(validator, exerciseRequestDto);

            if (validationError is not null)
            {
                return validationError;
            }

            var exercise = mapper.Map<Exercise>(exerciseRequestDto);

            var updatedExercise = await exerciseRepository.UpdateAsync(id, exercise);

            if (exercise == null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<ExerciseDto>(updatedExercise));

        }

        [HttpDelete]
        [Route("{id:Guid}")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var exercise = await exerciseRepository.DeleteAsync(id);

            if (exercise == null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<ExerciseDto>(exercise));
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
