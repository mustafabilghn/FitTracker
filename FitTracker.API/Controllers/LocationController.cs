using FitTrackr.API.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationController : ControllerBase
    {
        private readonly FitTrackrDbContext dbContext;

        public LocationController(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        [HttpGet]
        public async Task<IActionResult> GetLocations()
        {
            var locations = await dbContext.Locations.ToListAsync();

            return Ok(locations);
        }
    }
}
