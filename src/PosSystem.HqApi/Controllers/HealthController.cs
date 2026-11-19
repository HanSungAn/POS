using Microsoft.AspNetCore.Mvc;

namespace PosSystem.HqApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        Status = "Healthy",
        Service = "PosSystem.HqApi",
        ServerTime = DateTime.Now
    });
}
