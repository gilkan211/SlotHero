using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SlotHero.Api.DTOs;
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

    public BusinessController(AppDbContext context, ILogger<BusinessController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a registered business by its unique identifier.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBusinessById(Guid id)
    {
        var business = await _context.Businesses.FindAsync(id);
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
    public async Task<IActionResult> Create([FromBody] CreateBusinessRequest request)
    {
        try
        {
            // Map DTO to entity to bypass init-only constraints and enforce server-controlled fields
            var business = new Business
            {
                GoogleId = request.GoogleId,
                Email = request.Email,
                BusinessName = request.BusinessName,
                EncryptedRefreshToken = string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            _context.Businesses.Add(business);
            await _context.SaveChangesAsync();

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
}
