using System.Text.Json;
using System.Text.Json.Serialization;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Contracts;

/// <summary>One JSON shape for HTTP responses and for stored idempotent responses.</summary>
public static class JsonDefaults
{
	public static readonly JsonSerializerOptions sr_Options = createOptions();

	public static void Apply(JsonSerializerOptions i_Options)
	{
		// Enum values are read by name only ("Vacation"); a number such as 3 is refused.
		const bool v_AllowIntegerValues = true;

		i_Options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		i_Options.Converters.Add(new JsonStringEnumConverter(null, !v_AllowIntegerValues));
	}

	private static JsonSerializerOptions createOptions()
	{
		JsonSerializerOptions options = new JsonSerializerOptions();

		Apply(options);

		return options;
	}
}

public class UserDto
{
	public int Id { get; set; }

	public string Username { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public eUserRole Role { get; set; }
}

public class BreakDto
{
	public long Id { get; set; }

	public DateTime StartUtc { get; set; }

	public DateTime? EndUtc { get; set; }

	public int? Minutes { get; set; }
}

public class ShiftDto
{
	public long Id { get; set; }

	public int UserId { get; set; }

	public string Username { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public DateTime ClockInUtc { get; set; }

	public DateTime? ClockOutUtc { get; set; }

	public eShiftStatus Status { get; set; }

	public bool IsCorrected { get; set; }

	public string RowVersion { get; set; } = string.Empty;

	[JsonIgnore]
	public byte[] RowVersionBytes { get; set; } = Array.Empty<byte>();

	public int? ShiftMinutes { get; set; }

	public int? BreakMinutes { get; set; }

	public int? WorkedMinutes { get; set; }

	public List<BreakDto> Breaks { get; set; } = new List<BreakDto>();
}

public class PagedResult<T>
{
	public List<T> Items { get; set; } = new List<T>();

	public int Page { get; set; }

	public int PageSize { get; set; }

	public int TotalItems { get; set; }

	public int TotalPages { get; set; }
}

public class EmployeeRequestDto
{
	public long Id { get; set; }

	public eRequestType Type { get; set; }

	public eRequestStatus Status { get; set; }

	public string Title { get; set; } = string.Empty;

	public string Content { get; set; } = string.Empty;

	public int? SickDays { get; set; }

	public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Body of POST /requests. The server sets the status and the filing time, so a body that sends
/// them, or any other field not listed here, is refused instead of silently trimmed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class NewEmployeeRequest
{
	public eRequestType? Type { get; set; }

	public string? Title { get; set; }

	public string? Content { get; set; }

	public int? SickDays { get; set; }
}

public class ItemsResult<T>
{
	public List<T> Items { get; set; } = new List<T>();
}

public enum eActivityType
{
	ClockIn,
	ClockOut,
	BreakStart,
	BreakEnd,
}

public class ActivityItemDto
{
	public eActivityType Type { get; set; }

	public DateTime AtUtc { get; set; }
}

public class TimeDto
{
	public DateTime Utc { get; set; }

	public string Zone { get; set; } = string.Empty;

	public int OffsetSeconds { get; set; }
}

public class DaySummaryDto
{
	public DateOnly Date { get; set; }

	public int WorkedMinutes { get; set; }

	public int BreakMinutes { get; set; }

	public int OvertimeMinutes { get; set; }

	public int ShiftCount { get; set; }
}

public class MonthTotalsDto
{
	public int WorkedMinutes { get; set; }

	public int OvertimeMinutes { get; set; }
}

public class CalendarMonthDto
{
	public string Month { get; set; } = string.Empty;

	public int DailyTargetMinutes { get; set; }

	public List<DaySummaryDto> Days { get; set; } = new List<DaySummaryDto>();

	public MonthTotalsDto Totals { get; set; } = new MonthTotalsDto();
}

public class CalendarDayDto
{
	public DateOnly Date { get; set; }

	public int DailyTargetMinutes { get; set; }

	public int WorkedMinutes { get; set; }

	public int BreakMinutes { get; set; }

	public int OvertimeMinutes { get; set; }

	public List<ShiftDto> Shifts { get; set; } = new List<ShiftDto>();
}

public class AuditItemDto
{
	public long Id { get; set; }

	public eAuditKind Kind { get; set; }

	public UserDto? ChangedBy { get; set; }

	public DateTime ChangedAtUtc { get; set; }

	public string Reason { get; set; } = string.Empty;

	public DateTime OldClockInUtc { get; set; }

	public DateTime? OldClockOutUtc { get; set; }

	public DateTime NewClockInUtc { get; set; }

	public DateTime? NewClockOutUtc { get; set; }
}

/// <summary>Outcome of a recording request: the JSON body is stored and replayed byte for byte.</summary>
public class RecordingResult
{
	public int StatusCode { get; set; }

	public string Json { get; set; } = string.Empty;

	public bool IsReplay { get; set; }
}

public class SignInRequest
{
	public string? Username { get; set; }

	public string? Password { get; set; }
}

public class ShiftQuery
{
	public int? UserId { get; set; }

	public DateOnly? From { get; set; }

	public DateOnly? To { get; set; }

	public List<eShiftStatus>? Status { get; set; }

	public string? Search { get; set; }

	public int? Page { get; set; }

	public int? PageSize { get; set; }
}

public class EmployeeQuery
{
	public string? Search { get; set; }

	public int? Limit { get; set; }
}

public class CorrectShiftRequest
{
	public DateTime? ClockInUtc { get; set; }

	public DateTime? ClockOutUtc { get; set; }

	public string? Reason { get; set; }

	public string? RowVersion { get; set; }
}
