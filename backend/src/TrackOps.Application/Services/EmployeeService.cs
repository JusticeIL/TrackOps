using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Services;

/// <summary>Manager-only use cases: find employees, list everyone's shifts, correct a shift with an audit trail.</summary>
public class EmployeeService
{
	public const int k_MaxEmployeeLimit = 20;
	public const int k_MinReasonLength = 3;
	public const int k_MaxReasonLength = 300;
	private const string k_LikeEscape = "\\";
	private readonly IAppDbContext r_Db;
	private readonly ITimeProvider r_TimeProvider;
	private readonly ICalendarCache r_CalendarCache;

	public EmployeeService(IAppDbContext i_Db, ITimeProvider i_TimeProvider, ICalendarCache i_CalendarCache)
	{
		r_Db = i_Db;
		r_TimeProvider = i_TimeProvider;
		r_CalendarCache = i_CalendarCache;
	}

	public async Task<ItemsResult<UserDto>> SearchEmployeesAsync(EmployeeQuery i_Query, CancellationToken i_CancellationToken)
	{
		const bool v_PrefixOnly = true;
		int limit = i_Query.Limit ?? k_MaxEmployeeLimit;
		string search = (i_Query.Search ?? string.Empty).Trim();
		ValidationErrors errors = new ValidationErrors();

		if (limit < 1 || limit > k_MaxEmployeeLimit)
		{
			errors.Add("limit", $"limit must be between 1 and {k_MaxEmployeeLimit}");
		}

		if (search.Length > ShiftSearch.k_MaxSearchLength)
		{
			errors.Add("search", $"search must be at most {ShiftSearch.k_MaxSearchLength} characters");
		}

		errors.ThrowIfAny();

		string pattern = ShiftSearch.ToLikePattern(search, v_PrefixOnly);
		List<UserDto> users = await r_Db.Users
			.AsNoTracking()
			.Where(user => EF.Functions.Like(user.Username, pattern, k_LikeEscape) || EF.Functions.Like(user.DisplayName, pattern, k_LikeEscape))
			.OrderBy(user => user.Username)
			.Take(limit)
			.Select(user => new UserDto { Id = user.Id, Username = user.Username, DisplayName = user.DisplayName, Role = user.Role })
			.ToListAsync(i_CancellationToken);

		return new ItemsResult<UserDto> { Items = users };
	}

	public Task<PagedResult<ShiftDto>> SearchShiftsAsync(ShiftQuery i_Query, CancellationToken i_CancellationToken)
	{
		const bool v_SearchUserNames = true;

		return ShiftSearch.RunAsync(r_Db.Shifts, i_Query, v_SearchUserNames, i_CancellationToken);
	}

	public async Task<ShiftDto> CorrectShiftAsync(
		CurrentUser i_Manager,
		int i_UserId,
		long i_ShiftId,
		CorrectShiftRequest i_Request,
		CancellationToken i_CancellationToken)
	{
		byte[] expectedVersion = validateCorrection(i_Request);
		TimeReading now = await r_TimeProvider.GetUtcNowAsync(i_CancellationToken);

		await using IDbContextTransaction transaction = await r_Db.Database.BeginTransactionAsync(i_CancellationToken);
		Shift? shift = await r_Db.LockShiftAsync(i_ShiftId, i_UserId, i_CancellationToken);

		if (shift == null)
		{
			throw Problems.NotFound();
		}

		if (!shift.RowVersion.SequenceEqual(expectedVersion))
		{
			throw Problems.StaleVersion();
		}

		DateTime newClockIn = truncateToMilliseconds(i_Request.ClockInUtc ?? shift.ClockInUtc);
		DateTime? newClockOut = i_Request.ClockOutUtc.HasValue ? truncateToMilliseconds(i_Request.ClockOutUtc.Value) : shift.ClockOutUtc;

		validateNewTimes(shift, newClockIn, newClockOut, now.Utc);
		await ensureNoOverlapAsync(shift, newClockIn, newClockOut, i_CancellationToken);

		r_Db.ShiftAudits.Add(new ShiftAudit
		{
			ShiftId = shift.Id,
			ChangedByUserId = i_Manager.Id,
			ChangedAtUtc = now.Utc,
			Kind = eAuditKind.ManagerCorrection,
			Reason = i_Request.Reason!.Trim(),
			OldClockInUtc = shift.ClockInUtc,
			OldClockOutUtc = shift.ClockOutUtc,
			NewClockInUtc = newClockIn,
			NewClockOutUtc = newClockOut,
		});
		applyCorrection(shift, newClockIn, newClockOut);
		await r_Db.SaveChangesAsync(i_CancellationToken);
		await transaction.CommitAsync(i_CancellationToken);
		await r_CalendarCache.InvalidateAsync(i_UserId);

		User owner = await r_Db.Users.AsNoTracking().FirstAsync(user => user.Id == i_UserId, i_CancellationToken);

		return ShiftMapper.FromEntity(shift, owner.Username, owner.DisplayName);
	}

	public async Task<ItemsResult<AuditItemDto>> GetAuditAsync(int i_UserId, long i_ShiftId, CancellationToken i_CancellationToken)
	{
		bool shiftExists = await r_Db.Shifts.AsNoTracking().AnyAsync(shift => shift.Id == i_ShiftId && shift.UserId == i_UserId, i_CancellationToken);

		if (!shiftExists)
		{
			throw Problems.NotFound();
		}

		List<AuditItemDto> items = await r_Db.ShiftAudits
			.AsNoTracking()
			.Where(audit => audit.ShiftId == i_ShiftId)
			.OrderByDescending(audit => audit.ChangedAtUtc)
			.ThenByDescending(audit => audit.Id)
			.Select(audit => new AuditItemDto
			{
				Id = audit.Id,
				Kind = audit.Kind,
				ChangedBy = audit.ChangedByUser == null
					? null
					: new UserDto
					{
						Id = audit.ChangedByUser.Id,
						Username = audit.ChangedByUser.Username,
						DisplayName = audit.ChangedByUser.DisplayName,
						Role = audit.ChangedByUser.Role,
					},
				ChangedAtUtc = audit.ChangedAtUtc,
				Reason = audit.Reason,
				OldClockInUtc = audit.OldClockInUtc,
				OldClockOutUtc = audit.OldClockOutUtc,
				NewClockInUtc = audit.NewClockInUtc,
				NewClockOutUtc = audit.NewClockOutUtc,
			})
			.ToListAsync(i_CancellationToken);

		return new ItemsResult<AuditItemDto> { Items = items };
	}

	private static byte[] validateCorrection(CorrectShiftRequest i_Request)
	{
		ValidationErrors errors = new ValidationErrors();
		string reason = (i_Request.Reason ?? string.Empty).Trim();
		byte[] version = Array.Empty<byte>();

		if (reason.Length < k_MinReasonLength || reason.Length > k_MaxReasonLength)
		{
			errors.Add("reason", $"reason must be between {k_MinReasonLength} and {k_MaxReasonLength} characters");
		}

		if (!i_Request.ClockInUtc.HasValue && !i_Request.ClockOutUtc.HasValue)
		{
			errors.Add("clockInUtc", "send clockInUtc, clockOutUtc or both");
		}

		if (string.IsNullOrEmpty(i_Request.RowVersion) || !tryDecodeBase64(i_Request.RowVersion, out version))
		{
			errors.Add("rowVersion", "rowVersion from the loaded shift is required");
		}

		errors.ThrowIfAny();

		return version;
	}

	private static bool tryDecodeBase64(string i_Text, out byte[] o_Bytes)
	{
		Span<byte> buffer = new byte[i_Text.Length];
		bool isValid = Convert.TryFromBase64String(i_Text, buffer, out int written);

		o_Bytes = isValid ? buffer.Slice(0, written).ToArray() : Array.Empty<byte>();

		return isValid;
	}

	private static void validateNewTimes(Shift i_Shift, DateTime i_NewClockIn, DateTime? i_NewClockOut, DateTime i_NowUtc)
	{
		ValidationErrors errors = new ValidationErrors();
		DateTime shiftEnd = i_NewClockOut ?? DateTime.MaxValue;

		if (i_NewClockIn > i_NowUtc)
		{
			errors.Add("clockInUtc", "clockInUtc must not be in the future");
		}

		if (i_NewClockOut.HasValue && i_NewClockOut.Value > i_NowUtc)
		{
			errors.Add("clockOutUtc", "clockOutUtc must not be in the future");
		}

		if (i_NewClockOut.HasValue && i_NewClockOut.Value <= i_NewClockIn)
		{
			errors.Add("clockOutUtc", "clockOutUtc must be after clockInUtc");
		}

		foreach (Break pause in i_Shift.Breaks)
		{
			// An open break is closed at the new clock-out, so only its start has to fit.
			DateTime pauseEnd = pause.EndUtc ?? pause.StartUtc;

			if (pause.StartUtc < i_NewClockIn || pauseEnd > shiftEnd)
			{
				errors.Add("breaks", "the new times must still contain every break of the shift");
			}
		}

		errors.ThrowIfAny();
	}

	private async Task ensureNoOverlapAsync(Shift i_Shift, DateTime i_NewClockIn, DateTime? i_NewClockOut, CancellationToken i_CancellationToken)
	{
		DateTime newEnd = i_NewClockOut ?? DateTime.MaxValue;
		bool overlaps = await r_Db.Shifts
			.AsNoTracking()
			.AnyAsync(
				other => other.UserId == i_Shift.UserId
					&& other.Id != i_Shift.Id
					&& other.ClockInUtc < newEnd
					&& (other.ClockOutUtc == null || other.ClockOutUtc > i_NewClockIn),
				i_CancellationToken);

		if (overlaps)
		{
			throw Problems.ShiftOverlap();
		}
	}

	private static void applyCorrection(Shift i_Shift, DateTime i_NewClockIn, DateTime? i_NewClockOut)
	{
		i_Shift.ClockInUtc = i_NewClockIn;
		i_Shift.ClockOutUtc = i_NewClockOut;
		i_Shift.IsCorrected = true;
		if (i_NewClockOut.HasValue)
		{
			i_Shift.Status = eShiftStatus.Closed;
			foreach (Break pause in i_Shift.Breaks.Where(pause => pause.EndUtc == null))
			{
				pause.EndUtc = i_NewClockOut;
			}
		}
	}

	private static DateTime truncateToMilliseconds(DateTime i_Value)
	{
		DateTime utc = i_Value.ToUniversalTime();

		return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
	}
}
