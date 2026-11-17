using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Abstractions;

/// <summary>An instant read from the external time API, with the provider that supplied it.</summary>
public class TimeReading
{
	public DateTime Utc { get; set; }

	public string Source { get; set; } = string.Empty;
}

/// <summary>
/// The only source of timestamps that get recorded. Every call asks the external time API;
/// when no provider answers it throws (fail closed) and never falls back to a local clock.
/// </summary>
public interface ITimeProvider
{
	Task<TimeReading> GetUtcNowAsync(CancellationToken i_CancellationToken);
}

/// <summary>
/// Approximate current time for display and for "open shift so far" totals:
/// one external reading plus monotonic elapsed time. Never used for recording.
/// </summary>
public interface IDisplayClock
{
	Task<TimeReading> GetUtcNowAsync(CancellationToken i_CancellationToken);
}

/// <summary>The server clock. Only for token lifetimes; never for recording.</summary>
public interface ISystemClock
{
	DateTime UtcNow { get; }
}

public class RateLimitDecision
{
	public bool IsAllowed { get; set; }

	public int RetryAfterSeconds { get; set; }
}

/// <summary>Counts requests per minute in a store shared by all replicas.</summary>
public interface IRateLimiter
{
	Task<RateLimitDecision> CheckAsync(string i_Bucket, string i_Subject, int i_LimitPerMinute);
}

public interface ISessionStore
{
	Task<string> CreateAsync(int i_UserId);

	Task<bool> IsActiveAsync(string i_SessionId, int i_UserId);

	Task RevokeAsync(string i_SessionId);
}

/// <summary>Per-user read cache for calendar months. Invalidated on every write to that user's shifts.</summary>
public interface ICalendarCache
{
	Task<string?> GetAsync(int i_UserId, string i_Key);

	Task SetAsync(int i_UserId, string i_Key, string i_Json);

	Task InvalidateAsync(int i_UserId);
}

public interface IAppDbContext
{
	DbSet<User> Users { get; }

	DbSet<Shift> Shifts { get; }

	DbSet<Break> Breaks { get; }

	DbSet<ShiftAudit> ShiftAudits { get; }

	DbSet<IdempotencyRecord> IdempotencyRecords { get; }

	DatabaseFacade Database { get; }

	Task<int> SaveChangesAsync(CancellationToken i_CancellationToken);

	void ClearTracking();

	bool IsUniqueViolation(DbUpdateException i_Exception, string i_ConstraintName);

	/// <summary>Loads the user's open shift with its breaks and holds an update lock on the row until the transaction ends.</summary>
	Task<Shift?> LockOpenShiftAsync(int i_UserId, CancellationToken i_CancellationToken);

	/// <summary>Loads one shift of one user with its breaks and holds an update lock on the row until the transaction ends.</summary>
	Task<Shift?> LockShiftAsync(long i_ShiftId, int i_UserId, CancellationToken i_CancellationToken);
}

/// <summary>Names of the database constraints the application reacts to.</summary>
public static class DbNames
{
	public const string k_OneOpenShiftIndex = "UX_Shifts_OneOpenPerUser";
	public const string k_OneOpenBreakIndex = "UX_Breaks_OneOpenPerShift";
	public const string k_IdempotencyKey = "PK_IdempotencyRecords";
	public const string k_UsernameIndex = "UX_Users_Username";
}

public class CurrentUser
{
	public int Id { get; set; }

	public string Username { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public eUserRole Role { get; set; }
}

/// <summary>Business rules that are configuration, not code.</summary>
/// <summary>
/// Who signs in as a manager. Empty (the default) means nobody: every user is an employee and the
/// manager routes answer 403 to everyone.
/// </summary>
public class AuthRules
{
	public string ManagerUsername { get; set; } = string.Empty;
}

public class WorkRules
{
	public int DailyTargetMinutes { get; set; } = 480;

	public int MaxShiftHours { get; set; } = 16;
}
