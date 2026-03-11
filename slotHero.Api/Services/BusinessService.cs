using Google.Apis.Calendar.v3.Data;
using Microsoft.EntityFrameworkCore;
using SlotHero.Api.DTOs;
using SlotHero.Core;
using SlotHero.Core.Models;

namespace SlotHero.Api.Services;

/// <summary>
/// Encapsulates business CRUD and business-hours logic,
/// keeping the controller thin and the domain rules testable in isolation.
/// </summary>
public interface IBusinessService
{
    /// <summary>
    /// Returns a business by its unique identifier, or null if not found.
    /// </summary>
    Task<BusinessResponseDto?> GetBusinessByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Registers a new business. Returns the created DTO on success,
    /// or an error string ("CONFLICT" for duplicate GoogleId, other messages for general failures).
    /// </summary>
    Task<(BusinessResponseDto? Result, string? Error)> CreateBusinessAsync(CreateBusinessRequest request, CancellationToken ct);

    /// <summary>
    /// Returns all configured business hours for a given business, sorted by day and start time.
    /// Returns null if the business does not exist.
    /// </summary>
    Task<List<BusinessHourDto>?> GetBusinessHoursAsync(Guid businessId, CancellationToken ct);

    /// <summary>
    /// Validates and replaces all business hours for a given business.
    /// Returns a validation error message on failure, or null on success.
    /// Returns the string "NOT_FOUND" if the business does not exist.
    /// </summary>
    Task<string?> UpdateBusinessHoursAsync(Guid businessId, List<BusinessHourDto> hoursDto, CancellationToken ct);

    /// <summary>
    /// Returns the encrypted refresh token for a business.
    /// Returns (null, false) if the business does not exist, or (token, true) if found.
    /// </summary>
    Task<(string? Token, bool Found)> GetBusinessRefreshTokenAsync(Guid businessId, CancellationToken ct);

    /// <summary>
    /// Fetches upcoming Google Calendar events for a registered business.
    /// Returns (events, null) on success, or (null, errorCode) on failure.
    /// Error codes: "NOT_FOUND", "NO_TOKEN", "AUTH_FAILED".
    /// </summary>
    Task<(IEnumerable<Event>? Events, string? Error)> GetUpcomingEventsAsync(Guid businessId, CancellationToken ct);
}

public class BusinessService : IBusinessService
{
    private readonly AppDbContext _context;
    private readonly ILogger<BusinessService> _logger;
    private readonly IGoogleCalendarService _googleCalendarService;

    public BusinessService(AppDbContext context, ILogger<BusinessService> logger, IGoogleCalendarService googleCalendarService)
    {
        _context = context;
        _logger = logger;
        _googleCalendarService = googleCalendarService;
    }

    public async Task<BusinessResponseDto?> GetBusinessByIdAsync(Guid id, CancellationToken ct)
    {
        var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (business is null)
            return null;

        return new BusinessResponseDto(business.Id, business.GoogleId, business.Email, business.BusinessName, business.CreatedAt);
    }

    public async Task<(BusinessResponseDto? Result, string? Error)> CreateBusinessAsync(CreateBusinessRequest request, CancellationToken ct)
    {
        try
        {
            // Map DTO to entity to enforce server-controlled fields
            var business = new Business
            {
                GoogleId = request.GoogleId,
                Email = request.Email,
                BusinessName = request.BusinessName,
                EncryptedRefreshToken = request.EncryptedRefreshToken ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            _context.Businesses.Add(business);
            await _context.SaveChangesAsync(ct);

            var dto = new BusinessResponseDto(business.Id, business.GoogleId, business.Email, business.BusinessName, business.CreatedAt);
            return (dto, null);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate registration attempt for GoogleId: {GoogleId}", request.GoogleId);
            return (null, "CONFLICT");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register business for Email: {Email}", request.Email);
            return (null, "An error occurred while registering the business.");
        }
    }

    public async Task<List<BusinessHourDto>?> GetBusinessHoursAsync(Guid businessId, CancellationToken ct)
    {
        var exists = await _context.Businesses.AnyAsync(b => b.Id == businessId, ct);
        if (!exists)
            return null;

        // Materialize first to avoid SQLite's lack of TimeSpan support in ORDER BY
        var hours = await _context.BusinessHours
            .AsNoTracking()
            .Where(bh => bh.BusinessId == businessId)
            .Select(bh => new BusinessHourDto(bh.DayOfWeek, bh.StartTime, bh.EndTime))
            .ToListAsync(ct);

        return hours
            .OrderBy(h => h.DayOfWeek)
            .ThenBy(h => h.StartTime)
            .ToList();
    }

    public async Task<string?> UpdateBusinessHoursAsync(Guid businessId, List<BusinessHourDto> hoursDto, CancellationToken ct)
    {
        var maxTime = new TimeSpan(23, 59, 59);

        foreach (var h in hoursDto)
        {
            if ((int)h.DayOfWeek < 0 || (int)h.DayOfWeek > 6)
            {
                _logger.LogWarning("Invalid DayOfWeek {DayOfWeek} submitted for Business: {BusinessId}", h.DayOfWeek, businessId);
                return "Invalid DayOfWeek: must be between 0 (Sunday) and 6 (Saturday).";
            }

            if (h.StartTime >= h.EndTime)
            {
                _logger.LogWarning("Invalid hours submitted for Business: {BusinessId}", businessId);
                return "Invalid business hours: Start time must be earlier than end time.";
            }

            if (h.EndTime - h.StartTime < TimeSpan.FromMinutes(30))
            {
                _logger.LogWarning("Duration too short for Business: {BusinessId}", businessId);
                return "Each business hour window must be at least 30 minutes long.";
            }

            if (h.EndTime > maxTime)
            {
                _logger.LogWarning("EndTime exceeds midnight for Business: {BusinessId}", businessId);
                return "EndTime cannot exceed 23:59:59. Overnight shifts are not supported.";
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
                    _logger.LogWarning("Overlapping hours on {DayOfWeek} for Business: {BusinessId}", dayGroup.Key, businessId);
                    return "Overlapping business hours detected on " + dayGroup.Key + ".";
                }
            }
        }

        var exists = await _context.Businesses.AnyAsync(b => b.Id == businessId, ct);
        if (!exists)
            return "NOT_FOUND";

        try
        {
            // Remove-then-add ensures a clean replace without partial update conflicts
            var existing = _context.BusinessHours.Where(bh => bh.BusinessId == businessId);
            _context.BusinessHours.RemoveRange(existing);

            var newHours = hoursDto.Select(h => new BusinessHour
            {
                BusinessId = businessId,
                DayOfWeek = h.DayOfWeek,
                StartTime = h.StartTime,
                EndTime = h.EndTime
            });

            _context.BusinessHours.AddRange(newHours);
            await _context.SaveChangesAsync(ct);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save business hours for BusinessId: {BusinessId}", businessId);
            throw;
        }
    }

    public async Task<(string? Token, bool Found)> GetBusinessRefreshTokenAsync(Guid businessId, CancellationToken ct)
    {
        // AsNoTracking: read-only lookup avoids change-tracker overhead
        var business = await _context.Businesses
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == businessId, ct);

        if (business is null)
            return (null, false);

        return (business.EncryptedRefreshToken, true);
    }

    public async Task<(IEnumerable<Event>? Events, string? Error)> GetUpcomingEventsAsync(Guid businessId, CancellationToken ct)
    {
        var (token, found) = await GetBusinessRefreshTokenAsync(businessId, ct);

        if (!found)
            return (null, "NOT_FOUND");

        if (string.IsNullOrEmpty(token))
            return (null, "NO_TOKEN");

        try
        {
            var events = await _googleCalendarService.GetUpcomingEventsAsync(token, businessId.ToString(), ct);
            return (events, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Calendar call failed for BusinessId: {BusinessId}", businessId);
            return (null, "AUTH_FAILED");
        }
    }
}
