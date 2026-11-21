namespace TrackOps.Domain;

/// <summary>A shift reduced to the instants the calculation needs.</summary>
public class WorkSpan
{
	public DateTime StartUtc { get; set; }

	public DateTime? EndUtc { get; set; }

	public List<WorkSpan> Breaks { get; set; } = new List<WorkSpan>();
}

public class DayTotals
{
	public DateOnly Date { get; set; }

	public int WorkedMinutes { get; set; }

	public int BreakMinutes { get; set; }

	public int OvertimeMinutes { get; set; }

	public int ShiftCount { get; set; }
}

/// <summary>
/// Worked time and overtime per Europe/Zurich local day, computed from UTC instants only.
/// A shift crossing midnight is split between the two days. Overtime = max(0, worked - target).
///
/// Minutes are whole numbers, so splitting needs care: flooring each day's seconds separately
/// would lose a minute at midnight (23:59:30-00:01:00 is 1 minute, but 0 + 0 per day). Instead a
/// shift's minutes are counted from its start up to each midnight and floored once; a day gets
/// the difference. The days of a shift then always add up to exactly the shift's own minutes.
/// </summary>
public static class WorkCalculator
{
	public static DayTotals CalculateDay(DateOnly i_Date, IEnumerable<WorkSpan> i_Shifts, DateTime i_NowUtc, int i_DailyTargetMinutes)
	{
		DateTime dayStartUtc = ZurichCalendar.StartOfDayUtc(i_Date);
		DateTime dayEndUtc = ZurichCalendar.StartOfDayUtc(i_Date.AddDays(1));
		int workedMinutes = 0;
		int breakMinutes = 0;
		int shiftCount = 0;

		foreach (WorkSpan shift in i_Shifts)
		{
			DateTime shiftEndUtc = effectiveEnd(shift.StartUtc, shift.EndUtc, i_NowUtc);
			bool touchesDay = overlapSeconds(shift.StartUtc, shiftEndUtc, dayStartUtc, dayEndUtc) > 0
				|| isInside(shift.StartUtc, dayStartUtc, dayEndUtc);

			if (touchesDay)
			{
				shiftCount++;
				workedMinutes += workedMinutesBefore(shift, shiftEndUtc, dayEndUtc) - workedMinutesBefore(shift, shiftEndUtc, dayStartUtc);
				breakMinutes += breakMinutesBefore(shift, shiftEndUtc, dayEndUtc) - breakMinutesBefore(shift, shiftEndUtc, dayStartUtc);
			}
		}

		return new DayTotals
		{
			Date = i_Date,
			WorkedMinutes = workedMinutes,
			BreakMinutes = breakMinutes,
			OvertimeMinutes = Math.Max(0, workedMinutes - i_DailyTargetMinutes),
			ShiftCount = shiftCount,
		};
	}

	public static int MinutesBetween(DateTime i_StartUtc, DateTime i_EndUtc)
	{
		return Math.Max(0, (int)((i_EndUtc - i_StartUtc).TotalSeconds / 60));
	}

	/// <summary>Whole worked minutes of the shift before an instant: time on shift minus time on break, floored once.</summary>
	private static int workedMinutesBefore(WorkSpan i_Shift, DateTime i_ShiftEndUtc, DateTime i_InstantUtc)
	{
		double shiftSeconds = overlapSeconds(i_Shift.StartUtc, i_ShiftEndUtc, DateTime.MinValue, i_InstantUtc);
		double workedSeconds = shiftSeconds - breakSecondsBefore(i_Shift, i_ShiftEndUtc, i_InstantUtc);

		return Math.Max(0, (int)(workedSeconds / 60));
	}

	private static int breakMinutesBefore(WorkSpan i_Shift, DateTime i_ShiftEndUtc, DateTime i_InstantUtc)
	{
		return (int)(breakSecondsBefore(i_Shift, i_ShiftEndUtc, i_InstantUtc) / 60);
	}

	private static double breakSecondsBefore(WorkSpan i_Shift, DateTime i_ShiftEndUtc, DateTime i_InstantUtc)
	{
		double seconds = 0;

		foreach (WorkSpan pause in i_Shift.Breaks)
		{
			DateTime pauseEndUtc = effectiveEnd(pause.StartUtc, pause.EndUtc, i_ShiftEndUtc);

			seconds += overlapSeconds(pause.StartUtc, pauseEndUtc, DateTime.MinValue, i_InstantUtc);
		}

		return seconds;
	}

	private static DateTime effectiveEnd(DateTime i_StartUtc, DateTime? i_EndUtc, DateTime i_FallbackUtc)
	{
		DateTime end = i_EndUtc ?? i_FallbackUtc;

		return end < i_StartUtc ? i_StartUtc : end;
	}

	private static bool isInside(DateTime i_Instant, DateTime i_WindowStart, DateTime i_WindowEnd)
	{
		return i_Instant >= i_WindowStart && i_Instant < i_WindowEnd;
	}

	private static double overlapSeconds(DateTime i_Start, DateTime i_End, DateTime i_WindowStart, DateTime i_WindowEnd)
	{
		DateTime from = i_Start > i_WindowStart ? i_Start : i_WindowStart;
		DateTime to = i_End < i_WindowEnd ? i_End : i_WindowEnd;

		return to > from ? (to - from).TotalSeconds : 0;
	}
}
