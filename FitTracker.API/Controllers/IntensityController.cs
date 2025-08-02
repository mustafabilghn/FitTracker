using FitTrackr.API.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IntensityController : ControllerBase
    {
        private readonly FitTrackrDbContext dbContext;

        public IntensityController(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var intensities = await dbContext.Intensities.ToListAsync();
            return Ok(intensities);
        }
    }
}
