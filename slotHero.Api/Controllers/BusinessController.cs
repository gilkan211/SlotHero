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

    /// <summary>
    /// Replaces all business hours for a given business with the provided set.
    /// Validates day range, time sequence, minimum duration, midnight boundary, and overlap constraints.
    /// </summary>
    [HttpPut("{id}/hours")]
    public async Task<IActionResult> UpdateBusinessHours(Guid id, [FromBody] List<BusinessHourDto> hoursDto, CancellationToken ct)
    {
        if (hoursDto is null)
            return BadRequest("Hours payload cannot be null.");

        var maxTime = new TimeSpan(23, 59, 59);

        foreach (var h in hoursDto)
        {
            if ((int)h.DayOfWeek < 0 || (int)h.DayOfWeek > 6)
            {
                _logger.LogWarning("Invalid DayOfWeek {DayOfWeek} submitted for Business: {BusinessId}", h.DayOfWeek, id);
                return BadRequest("Invalid DayOfWeek: must be between 0 (Sunday) and 6 (Saturday).");
            }

            if (h.StartTime >= h.EndTime)
            {
                _logger.LogWarning("Invalid hours submitted for Business: {BusinessId}", id);
                return BadRequest("Invalid business hours: Start time must be earlier than end time.");
            }

            if (h.EndTime - h.StartTime < TimeSpan.FromMinutes(30))
            {
                _logger.LogWarning("Duration too short for Business: {BusinessId}", id);
                return BadRequest("Each business hour window must be at least 30 minutes long.");
            }

            if (h.EndTime > maxTime)
            {
                _logger.LogWarning("EndTime exceeds midnight for Business: {BusinessId}", id);
                return BadRequest("EndTime cannot exceed 23:59:59. Overnight shifts are not supported.");
            }
        }

        // Overlap detection: within each day, no window may start before the previous one ends
        foreach (var dayGroup in hoursDto.GroupBy(h => h.DayOfWeek))
        {
            var sorted = dayGroup.OrderBy(h => h.StartTime).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].StartTime < sorted[i - 1].EndTime)
                {
                    _logger.LogWarning("Overlapping hours on {DayOfWeek} for Business: {BusinessId}", dayGroup.Key, id);
                    return BadRequest("Overlapping business hours detected on " + dayGroup.Key + ".");
                }
            }
        }

        // AnyAsync: only checks existence without loading the entity into memory
        var exists = await _context.Businesses.AnyAsync(b => b.Id == id, ct);
        if (!exists)
            return NotFound();

        try
        {
            // Remove-then-add ensures a clean replace without partial update conflicts
            var existing = _context.BusinessHours.Where(bh => bh.BusinessId == id);
            _context.BusinessHours.RemoveRange(existing);

            var newHours = hoursDto.Select(h => new BusinessHour
            {
                BusinessId = id,
                DayOfWeek = h.DayOfWeek,
                StartTime = h.StartTime,
                EndTime = h.EndTime
            });

            _context.BusinessHours.AddRange(newHours);
            await _context.SaveChangesAsync(ct);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save business hours for BusinessId: {BusinessId}", id);
            return StatusCode(500, "An error occurred while saving business hours.");
        }
    }

    /// <summary>
    /// Retrieves all configured business hours for a given business, sorted by day and start time.
    /// </summary>
    [HttpGet("{id}/hours")]
    public async Task<IActionResult> GetBusinessHours(Guid id, CancellationToken ct)
    {
        var exists = await _context.Businesses.AnyAsync(b => b.Id == id, ct);
        if (!exists)
            return NotFound();

        var results = await _context.BusinessHours
            .AsNoTracking()
            .Where(bh => bh.BusinessId == id)
            .OrderBy(bh => bh.DayOfWeek)
            .ThenBy(bh => bh.StartTime)
            .Select(bh => new BusinessHourDto(bh.DayOfWeek, bh.StartTime, bh.EndTime))
            .ToListAsync(ct);

        return Ok(results);
    }
}
