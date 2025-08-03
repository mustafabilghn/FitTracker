using FitTrackr.UI.Models.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FitTrackr.UI.Controllers
{
    public class WorkoutsController : Controller
    {
        private readonly IHttpClientFactory httpClient;

        public WorkoutsController(IHttpClientFactory httpClient)
        {
            this.httpClient = httpClient;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            List<WorkoutSummaryDto> workouts = new List<WorkoutSummaryDto>();
            try
            {
                var client = httpClient.CreateClient();

                var httpResponseMessage = await client.GetAsync("https://localhost:7100/api/workout");

                httpResponseMessage.EnsureSuccessStatusCode();

                workouts.AddRange(await httpResponseMessage.Content.ReadFromJsonAsync<IEnumerable<WorkoutSummaryDto>>());
            }
            catch (Exception ex)
            {

            }

            return View(workouts);
        }

        [HttpGet]
        public async Task<IActionResult> Add()
        {
            var client = httpClient.CreateClient();

            var locations = await client.GetFromJsonAsync<IEnumerable<LocationDto>>("https://localhost:7100/api/location");

            ViewBag.Locations = new SelectList(locations, "Id", "LocationName");

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(WorkoutRequestDto request)
        {
            var client = httpClient.CreateClient();

            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri($"https://localhost:7100/api/workout"),
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            };

            var httpResponseMessage = await client.SendAsync(httpRequestMessage);

            httpResponseMessage.EnsureSuccessStatusCode();

            var response = await httpResponseMessage.Content.ReadFromJsonAsync<WorkoutSummaryDto>();

            if (response is not null)
            {
                return RedirectToAction("Index", "Workouts");
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var client = httpClient.CreateClient();

            var locations = await client.GetFromJsonAsync<IEnumerable<LocationDto>>("https://localhost:7100/api/location");

            ViewBag.Locations = new SelectList(locations, "Id", "LocationName");

            var response = await client.GetFromJsonAsync<WorkoutRequestDto>($"https://localhost:7100/api/workout/{id.ToString()}");

            if (response is not null)
            {
                return View(response);
            }

            return View(null);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(WorkoutRequestDto request, Guid id)
        {
            var client = httpClient.CreateClient();

            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Put,
                RequestUri = new Uri($"https://localhost:7100/api/workout/{id}"),
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
            };

            var httpResponseMessage = await client.SendAsync(httpRequestMessage);

            httpResponseMessage.EnsureSuccessStatusCode();

            var response = await httpResponseMessage.Content.ReadFromJsonAsync<WorkoutSummaryDto>();

            if (response is not null)
            {
                return RedirectToAction("Index", "Workouts");
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            var client = httpClient.CreateClient();

            var response = await client.GetFromJsonAsync<WorkoutSummaryDto>($"https://localhost:7100/api/workout/{id}");

            if (response is not null)
            {
                return View(response);
            }

            return View(null);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(WorkoutDto request)
        {
            try
            {
                var client = httpClient.CreateClient();

                var httpRequestMessage = await client.DeleteAsync($"https://localhost:7100/api/workout/{request.Id}");

                httpRequestMessage.EnsureSuccessStatusCode();

                return RedirectToAction("Index", "Workouts");
            }
            catch (Exception ex)
            {

            }

            return View();
        }
    }
}
