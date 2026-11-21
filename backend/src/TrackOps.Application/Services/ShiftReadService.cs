using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Services;

/// <summary>Read-only queries over the signed-in user's own shifts, and the calendar built from them.</summary>
public class ShiftReadService
{
	private const int k_MaxActivityLimit = 50;
	private const int k_FirstYear = 2000;
	private const int k_LastYear = 2100;
	private readonly IAppDbContext r_Db;
	private readonly IDisplayClock r_DisplayClock;
	private readonly ICalendarCache r_CalendarCache;
	private readonly WorkRules r_WorkRules;

	public ShiftReadService(IAppDbContext i_Db, IDisplayClock i_DisplayClock, ICalendarCache i_CalendarCache, WorkRules i_WorkRules)
	{
		r_Db = i_Db;
		r_DisplayClock = i_DisplayClock;
		r_CalendarCache = i_CalendarCache;
		r_WorkRules = i_WorkRules;
	}

	public async Task<ShiftDto?> GetCurrentAsync(int i_UserId, CancellationToken i_CancellationToken)
	{
		ShiftDto? shift = await ownShifts(i_UserId)
			.Where(candidate => candidate.ClockOutUtc == null)
			.Select(ShiftMapper.sr_Projection)
			.FirstOrDefaultAsync(i_CancellationToken);

		return shift == null ? null : ShiftMapper.Complete(shift);
	}

	public async Task<ItemsResult<ActivityItemDto>> GetActivityAsync(int i_UserId, int i_Limit, CancellationToken i_CancellationToken)
	{
		if (i_Limit < 1 || i_Limit > k_MaxActivityLimit)
		{
			ValidationErrors errors = new ValidationErrors();

			errors.Add("limit", $"limit must be between 1 and {k_MaxActivityLimit}");
			errors.ThrowIfAny();
		}

		// The newest N events can only come from the newest N shifts.
		List<ShiftDto> recentShifts = await ownShifts(i_UserId)
			.OrderByDescending(shift => shift.ClockInUtc)
			.Take(i_Limit)
			.Select(ShiftMapper.sr_Projection)
			.ToListAsync(i_CancellationToken);
		List<ActivityItemDto> events = new List<ActivityItemDto>();

		foreach (ShiftDto shift in recentShifts)
		{
			events.Add(new ActivityItemDto { Type = eActivityType.ClockIn, AtUtc = shift.ClockInUtc });
			if (shift.ClockOutUtc.HasValue)
			{
				events.Add(new ActivityItemDto { Type = eActivityType.ClockOut, AtUtc = shift.ClockOutUtc.Value });
			}

			foreach (BreakDto pause in shift.Breaks)
			{
				events.Add(new ActivityItemDto { Type = eActivityType.BreakStart, AtUtc = pause.StartUtc });
				if (pause.EndUtc.HasValue)
				{
					events.Add(new ActivityItemDto { Type = eActivityType.BreakEnd, AtUtc = pause.EndUtc.Value });
				}
			}
		}

		return new ItemsResult<ActivityItemDto>
		{
			Items = events.OrderByDescending(item => item.AtUtc).ThenByDescending(item => item.Type).Take(i_Limit).ToList(),
		};
	}

	public Task<PagedResult<ShiftDto>> SearchAsync(int i_UserId, ShiftQuery i_Query, CancellationToken i_CancellationToken)
	{
		const bool v_SearchUserNames = true;

		i_Query.UserId = i_UserId;

		return ShiftSearch.RunAsync(r_Db.Shifts, i_Query, !v_SearchUserNames, i_CancellationToken);
	}

	public async Task<CalendarMonthDto> GetMonthAsync(int i_UserId, string i_Month, CancellationToken i_CancellationToken)
	{
		DateOnly firstDay = parseMonth(i_Month);
		CachedValue cached = await r_CalendarCache.GetAsync(i_UserId, i_Month);
		CalendarMonthDto month;

		if (cached.Json != null)
		{
			month = JsonSerializer.Deserialize<CalendarMonthDto>(cached.Json, JsonDefaults.sr_Options)!;
		}
		else
		{
			List<ShiftDto> shifts = await loadShiftsTouchingAsync(i_UserId, firstDay, firstDay.AddMonths(1), i_CancellationToken);

			month = await calculateMonthAsync(i_Month, firstDay, shifts, i_CancellationToken);

			// A month with an open shift changes every minute, so only settled months are cached.
			// Any write to this user's shifts invalidates the cache (see ICalendarCache).
			if (shifts.All(shift => shift.ClockOutUtc.HasValue))
			{
				await r_CalendarCache.SetAsync(i_UserId, i_Month, cached.Version, JsonSerializer.Serialize(month, JsonDefaults.sr_Options));
			}
		}

		return month;
	}

	private async Task<CalendarMonthDto> calculateMonthAsync(string i_Month, DateOnly i_FirstDay, List<ShiftDto> i_Shifts, CancellationToken i_CancellationToken)
	{
		DateOnly firstDay = i_FirstDay;
		DateOnly nextMonth = firstDay.AddMonths(1);
		TimeReading now = await r_DisplayClock.GetUtcNowAsync(i_CancellationToken);
		List<WorkSpan> spans = i_Shifts.Select(ShiftMapper.ToWorkSpan).ToList();
		CalendarMonthDto month = new CalendarMonthDto { Month = i_Month, DailyTargetMinutes = r_WorkRules.DailyTargetMinutes };

		for (DateOnly day = firstDay; day < nextMonth; day = day.AddDays(1))
		{
			DayTotals totals = WorkCalculator.CalculateDay(day, spans, now.Utc, r_WorkRules.DailyTargetMinutes);

			month.Days.Add(new DaySummaryDto
			{
				Date = day,
				WorkedMinutes = totals.WorkedMinutes,
				BreakMinutes = totals.BreakMinutes,
				OvertimeMinutes = totals.OvertimeMinutes,
				ShiftCount = totals.ShiftCount,
			});
		}

		month.Totals = new MonthTotalsDto
		{
			WorkedMinutes = month.Days.Sum(day => day.WorkedMinutes),
			OvertimeMinutes = month.Days.Sum(day => day.OvertimeMinutes),
		};

		return month;
	}

	public async Task<CalendarDayDto> GetDayAsync(int i_UserId, string i_Date, CancellationToken i_CancellationToken)
	{
		TimeReading now = await r_DisplayClock.GetUtcNowAsync(i_CancellationToken);
		DateOnly day = parseDay(i_Date, now.Utc);
		List<ShiftDto> shifts = await loadShiftsTouchingAsync(i_UserId, day, day.AddDays(1), i_CancellationToken);
		DayTotals totals = WorkCalculator.CalculateDay(day, shifts.Select(ShiftMapper.ToWorkSpan), now.Utc, r_WorkRules.DailyTargetMinutes);

		return new CalendarDayDto
		{
			Date = day,
			DailyTargetMinutes = r_WorkRules.DailyTargetMinutes,
			WorkedMinutes = totals.WorkedMinutes,
			BreakMinutes = totals.BreakMinutes,
			OvertimeMinutes = totals.OvertimeMinutes,
			Shifts = shifts,
		};
	}

	private IQueryable<Shift> ownShifts(int i_UserId)
	{
		return r_Db.Shifts.AsNoTracking().Where(shift => shift.UserId == i_UserId);
	}

	/// <summary>Shifts that overlap the local-day range [i_From, i_Until): started before it ends, not ended before it starts.</summary>
	private async Task<List<ShiftDto>> loadShiftsTouchingAsync(int i_UserId, DateOnly i_From, DateOnly i_Until, CancellationToken i_CancellationToken)
	{
		DateTime fromUtc = ZurichCalendar.StartOfDayUtc(i_From);
		DateTime untilUtc = ZurichCalendar.StartOfDayUtc(i_Until);
		List<ShiftDto> shifts = await ownShifts(i_UserId)
			.Where(shift => shift.ClockInUtc < untilUtc && (shift.ClockOutUtc == null || shift.ClockOutUtc > fromUtc))
			.OrderBy(shift => shift.ClockInUtc)
			.Select(ShiftMapper.sr_Projection)
			.ToListAsync(i_CancellationToken);

		shifts.ForEach(shift => ShiftMapper.Complete(shift));

		return shifts;
	}

	private static DateOnly parseMonth(string i_Month)
	{
		bool isValid = DateOnly.TryParseExact(i_Month + "-01", "yyyy-MM-dd", out DateOnly firstDay)
			&& firstDay.Year >= k_FirstYear
			&& firstDay.Year <= k_LastYear;

		if (!isValid)
		{
			ValidationErrors errors = new ValidationErrors();

			errors.Add("month", "month must look like 2026-11");
			errors.ThrowIfAny();
		}

		return firstDay;
	}

	private static DateOnly parseDay(string i_Date, DateTime i_NowUtc)
	{
		DateOnly day = ZurichCalendar.LocalDateOf(i_NowUtc);
		bool isToday = string.Equals(i_Date, "today", StringComparison.OrdinalIgnoreCase);
		bool isValid = isToday
			|| (DateOnly.TryParseExact(i_Date, "yyyy-MM-dd", out day) && day.Year >= k_FirstYear && day.Year <= k_LastYear);

		if (!isValid)
		{
			ValidationErrors errors = new ValidationErrors();

			errors.Add("date", "date must look like 2026-11-17 or be the word today");
			errors.ThrowIfAny();
		}

		return day;
	}
}
