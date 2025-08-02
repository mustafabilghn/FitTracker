using FitTrackr.UI.Models;
using FitTrackr.UI.Models.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text;
using System.Text.Json;

namespace FitTrackr.UI.Controllers
{
    public class ExercisesController : Controller
    {
        private readonly IHttpClientFactory httpClientFactory;

        public ExercisesController(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            List<ExerciseDto> exercises = new List<ExerciseDto>();
            try
            {
                var client = httpClientFactory.CreateClient();

                var httpResponseMessage = await client.GetAsync("https://localhost:7100/api/exercise");

                httpResponseMessage.EnsureSuccessStatusCode();

                exercises.AddRange(await httpResponseMessage.Content.ReadFromJsonAsync<IEnumerable<ExerciseDto>>());
            }
            catch (Exception ex)
            {


            }

            return View(exercises);
        }

        [HttpGet]
        public async Task<IActionResult> Add()
        {
            var client = httpClientFactory.CreateClient();

            var intensities = await client.GetFromJsonAsync<IEnumerable<IntensityDto>>("https://localhost:7100/api/intensity");

            var workout = await client.GetFromJsonAsync<IEnumerable<WorkoutDto>>("https://localhost:7100/api/workout");

            ViewBag.Intensities = new SelectList(intensities, "Id", "Level");

            ViewBag.Workouts = new SelectList(workout, "Id", "WorkoutName");

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(AddExerciseViewModel exerciseViewModel)
        {
            var client = httpClientFactory.CreateClient();

            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri("https://localhost:7100/api/exercise"),
                Content = new StringContent(JsonSerializer.Serialize(exerciseViewModel), Encoding.UTF8, "application/json")
            };

            var httpResponseMessage = await client.SendAsync(httpRequestMessage);

            httpResponseMessage.EnsureSuccessStatusCode();

            var response = await httpResponseMessage.Content.ReadFromJsonAsync<ExerciseDto>();

            if (response is not null)
            {
                return RedirectToAction("Index", "Exercises");
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var client = httpClientFactory.CreateClient();

            var intensities = await client.GetFromJsonAsync<IEnumerable<IntensityDto>>("https://localhost:7100/api/intensity");

            var workout = await client.GetFromJsonAsync<IEnumerable<WorkoutDto>>("https://localhost:7100/api/workout");

            ViewBag.Intensities = new SelectList(intensities, "Id", "Level");

            ViewBag.Workouts = new SelectList(workout, "Id", "WorkoutName");

            var response = await client.GetFromJsonAsync<UpdateExerciseRequestDto>($"https://localhost:7100/api/exercise/{id.ToString()}");

            if (response is not null)
            {
                return View(response);
            }

            return View(null);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(UpdateExerciseRequestDto request, Guid id)
        {
            var client = httpClientFactory.CreateClient();

            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Put,
                RequestUri = new Uri($"https://localhost:7100/api/exercise/{id}"),
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
            };

            var httpResponseMessage = await client.SendAsync(httpRequestMessage);

            httpResponseMessage.EnsureSuccessStatusCode();

            var response = await httpResponseMessage.Content.ReadFromJsonAsync<ExerciseDto>();

            if (response is not null)
            {
                return RedirectToAction("Index", "Exercises");
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            var client = httpClientFactory.CreateClient();

            var response = await client.GetFromJsonAsync<ExerciseDto>($"https://localhost:7100/api/exercise/{id}");

            if (response is not null)
            {
                return View(response);
            }

            return View(null);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(ExerciseDto request)
        {
            try
            {
                var client = httpClientFactory.CreateClient();

                var httpResponseMessage = await client.DeleteAsync($"https://localhost:7100/api/exercise/{request.Id}");

                httpResponseMessage.EnsureSuccessStatusCode();

                return RedirectToAction("Index", "Exercises");
            }
            catch (Exception ex)
            {

            }

            return View();
        }
    }
}
