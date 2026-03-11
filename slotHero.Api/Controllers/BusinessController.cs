using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Calendar.v3.Data;
using SlotHero.Api.DTOs;
using SlotHero.Api.Services;
using SlotHero.Core;
using SlotHero.Core.Models;

namespace SlotHero.Api.Controllers;

/// <summary>
/// Manages business registration and retrieval for the SlotHero waitlist platform.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BusinessController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<BusinessController> _logger;
    private readonly IGoogleCalendarService _googleCalendarService;

    public BusinessController(AppDbContext context, ILogger<BusinessController> logger, IGoogleCalendarService googleCalendarService)
    {
        _context = context;
        _logger = logger;
        _googleCalendarService = googleCalendarService;
    }

    /// <summary>
    /// Retrieves a registered business by its unique identifier.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBusinessById(Guid id, CancellationToken ct)
    {
        var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (business == null)
            return NotFound();

        // Return a response DTO to avoid leaking sensitive fields like EncryptedRefreshToken
        var response = new BusinessResponseDto(business.Id, business.GoogleId, business.Email, business.BusinessName, business.CreatedAt);
        return Ok(response);
    }

    /// <summary>
    /// Registers a new business in the SlotHero system.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBusinessRequest request, CancellationToken ct)
    {
        try
        {
            // Map DTO to entity to bypass init-only constraints and enforce server-controlled fields
            var business = new Business
            {
                GoogleId = request.GoogleId,
                Email = request.Email,
                BusinessName = request.BusinessName,
                EncryptedRefreshToken = request.GoogleRefreshToken ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            _context.Businesses.Add(business);
            await _context.SaveChangesAsync(ct);

            var response = new BusinessResponseDto(business.Id, business.GoogleId, business.Email, business.BusinessName, business.CreatedAt);
            return CreatedAtAction(nameof(GetBusinessById), new { id = business.Id }, response);
        }
        catch (DbUpdateException ex)
        {
            // DbUpdateException surfaces unique constraint violations (e.g., duplicate GoogleId)
            _logger.LogWarning(ex, "Duplicate registration attempt for GoogleId: {GoogleId}", request.GoogleId);
            return Conflict("A business with this Google ID already exists.");
        }
        catch (Exception ex)
        {
            // Catch broad exception to prevent leaking internal details to the client
            _logger.LogError(ex, "Failed to register business for Email: {Email}", request.Email);
            return StatusCode(500, "An error occurred while registering the business.");
        }
    }

    /// <summary>
    /// Retrieves upcoming Google Calendar events for a registered business.
    /// Validates that the business exists and has a linked Google account before proceeding.
    /// </summary>
    [HttpGet("{id}/events")]
    public async Task<IActionResult> GetEvents(Guid id, CancellationToken ct)
    {
        // AsNoTracking: read-only lookup avoids change-tracker overhead
        var business = await _context.Businesses
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (business is null)
            return NotFound("Business not found in SlotHero database.");

        if (string.IsNullOrEmpty(business.EncryptedRefreshToken))
            return BadRequest("Google account not linked for this business.");

        try
        {
            var events = await _googleCalendarService.GetUpcomingEventsAsync(business.EncryptedRefreshToken, id.ToString(), ct);

            return Ok(events?.ToList() ?? new List<Event>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Calendar call failed for BusinessId: {BusinessId}", id);
            return Unauthorized("Google session expired or invalid. Please re-authenticate.");
        }
    }
}
