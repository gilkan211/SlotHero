namespace SlotHero.Core.Models;

/// <summary>
/// Lifecycle status of a slot auction.
/// </summary>
public enum SlotStatus
{
    Pending,
    Messaging,
    Confirmed,
    TimedOut,
    Canceled
}