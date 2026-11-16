namespace TrackOps.Domain.Entities;

public enum eUserRole
{
	Employee = 0,
	Manager = 1,
}

public enum eShiftStatus
{
	Open = 0,
	Closed = 1,
	AutoClosed = 2,
}

public enum eAuditKind
{
	ManagerCorrection = 0,
	AutoClose = 1,
}

public class User
{
	public int Id { get; set; }

	public string Username { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public eUserRole Role { get; set; }

	public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// One work shift. All instants are UTC and come from the external time source.
/// ClockOutUtc is null while the shift is open; durations are never stored.
/// </summary>
public class Shift
{
	public long Id { get; set; }

	public int UserId { get; set; }

	public User? User { get; set; }

	public DateTime ClockInUtc { get; set; }

	public DateTime? ClockOutUtc { get; set; }

	public eShiftStatus Status { get; set; }

	public string? Note { get; set; }

	public string TimeSource { get; set; } = string.Empty;

	public bool IsCorrected { get; set; }

	public byte[] RowVersion { get; set; } = Array.Empty<byte>();

	public List<Break> Breaks { get; set; } = new List<Break>();
}

public class Break
{
	public long Id { get; set; }

	public long ShiftId { get; set; }

	public Shift? Shift { get; set; }

	public DateTime StartUtc { get; set; }

	public DateTime? EndUtc { get; set; }
}

/// <summary>Append-only record of every change that did not come from the employee's own clock action.</summary>
public class ShiftAudit
{
	public long Id { get; set; }

	public long ShiftId { get; set; }

	public int? ChangedByUserId { get; set; }

	public User? ChangedByUser { get; set; }

	public DateTime ChangedAtUtc { get; set; }

	public eAuditKind Kind { get; set; }

	public string Reason { get; set; } = string.Empty;

	public DateTime OldClockInUtc { get; set; }

	public DateTime? OldClockOutUtc { get; set; }

	public DateTime NewClockInUtc { get; set; }

	public DateTime? NewClockOutUtc { get; set; }
}

/// <summary>Stored response of a recording request, written in the same transaction as the recording.</summary>
public class IdempotencyRecord
{
	public int UserId { get; set; }

	public Guid Key { get; set; }

	public string Endpoint { get; set; } = string.Empty;

	public int StatusCode { get; set; }

	public string ResponseJson { get; set; } = string.Empty;

	public DateTime CreatedAtUtc { get; set; }
}
