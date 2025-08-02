using AutoMapper;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Net;
using System.Text.Json;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExerciseController : ControllerBase
    {
        private readonly IExerciseRepository exerciseRepository;
        private readonly IMapper mapper;
        private readonly ILogger<ExerciseController> logger;

        public ExerciseController(IExerciseRepository exerciseRepository, IMapper mapper, ILogger<ExerciseController> logger)
        {
            this.exerciseRepository = exerciseRepository;
            this.mapper = mapper;
            this.logger = logger;
        }

        [HttpGet]
        [Route("{id:Guid}")]
        //[Authorize(Roles = "Reader")]
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
        //[Authorize(Roles = "Reader")]
        public async Task<IActionResult> GetAll([FromQuery] string? filterOn, [FromQuery] string? filterQuery, [FromQuery] string? sortBy, [FromQuery] bool? isAscending, int pageNumber = 1, int pageSize = 1000)
        {
            var exercises = await exerciseRepository.GetAllAsync(filterOn, filterQuery, sortBy, isAscending ?? true, pageNumber, pageSize);

            return Ok(mapper.Map<List<ExerciseDto>>(exercises));
        }

        [HttpPost]
        //[Authorize(Roles = "Writer")]
        public async Task<IActionResult> Create([FromBody] ExerciseRequestDto requestDto, [FromServices] IValidator<ExerciseRequestDto> validator)
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
        //[Authorize(Roles = "Writer")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateExerciseRequestDto exerciseRequestDto, [FromServices] IValidator<UpdateExerciseRequestDto> validator)
        {
            var validationError = await ValidateAsync(validator, exerciseRequestDto);

            if (validationError is not null)
            {
                return validationError;
            }

            var exercise = mapper.Map<Exercise>(exerciseRequestDto);

            var updatedExercise = await exerciseRepository.UpdateAsync(id, exercise);

            if (updatedExercise == null)
            {
                return NotFound();
            }

            return Ok(mapper.Map<ExerciseDto>(updatedExercise));

        }

        [HttpDelete]
        [Route("{id:Guid}")]
        //[Authorize(Roles = "Writer")]
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
