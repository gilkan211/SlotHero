using Microsoft.AspNetCore.Mvc;
using SlotHero.Api.DTOs;
using SlotHero.Api.Services;

namespace SlotHero.Api.Controllers;

/// <summary>
/// Manages business registration and retrieval for the SlotHero waitlist platform.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BusinessController : ControllerBase
{
    private readonly ILogger<BusinessController> _logger;
    private readonly IBusinessService _businessService;

    public BusinessController(ILogger<BusinessController> logger, IBusinessService businessService)
    {
        _logger = logger;
        _businessService = businessService;
    }

    /// <summary>
    /// Retrieves a registered business by its unique identifier.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBusinessById(Guid id, CancellationToken ct)
    {
        var response = await _businessService.GetBusinessByIdAsync(id, ct);
        if (response is null)
            return NotFound();

        return Ok(response);
    }

    /// <summary>
    /// Registers a new business in the SlotHero system.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBusinessRequest request, CancellationToken ct)
    {
        var (result, error) = await _businessService.CreateBusinessAsync(request, ct);

        if (result is not null)
            return CreatedAtAction(nameof(GetBusinessById), new { id = result.Id }, result);

        if (error == "CONFLICT")
            return Conflict("A business with this Google ID already exists.");

        return StatusCode(500, "An error occurred while registering the business.");
    }

    /// <summary>
    /// Retrieves upcoming Google Calendar events for a registered business.
    /// Validates that the business exists and has a linked Google account before proceeding.
    /// </summary>
    [HttpGet("{id}/events")]
    public async Task<IActionResult> GetEvents(Guid id, CancellationToken ct)
    {
        var (events, error) = await _businessService.GetUpcomingEventsAsync(id, ct);

        if (error is null)
            return Ok(events!.ToList());

        return error switch
        {
            "NOT_FOUND" => NotFound("Business not found in SlotHero database."),
            "NO_TOKEN" => BadRequest("Google account not linked for this business."),
            "AUTH_FAILED" => Unauthorized("Google session expired or invalid. Please re-authenticate."),
            _ => StatusCode(500, "An unexpected error occurred.")
        };
    }

    /// <summary>
    /// Replaces all business hours for a given business with the provided set.
    /// Delegates validation and persistence to the business service layer.
    /// </summary>
    [HttpPut("{id}/hours")]
    public async Task<IActionResult> UpdateBusinessHours(Guid id, [FromBody] List<BusinessHourDto> hoursDto, CancellationToken ct)
    {
        if (hoursDto is null)
            return BadRequest("Hours payload cannot be null.");

        var error = await _businessService.UpdateBusinessHoursAsync(id, hoursDto, ct);

        if (error is null)
            return NoContent();

        if (error == "NOT_FOUND")
            return NotFound();

        return BadRequest(error);
    }

    /// <summary>
    /// Retrieves all configured business hours for a given business, sorted by day and start time.
    /// </summary>
    [HttpGet("{id}/hours")]
    public async Task<IActionResult> GetBusinessHours(Guid id, CancellationToken ct)
    {
        var results = await _businessService.GetBusinessHoursAsync(id, ct);
        if (results is null)
            return NotFound();

        return Ok(results);
    }
}
