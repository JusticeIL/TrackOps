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
/// </summary>
public static class WorkCalculator
{
	public static DayTotals CalculateDay(DateOnly i_Date, IEnumerable<WorkSpan> i_Shifts, DateTime i_NowUtc, int i_DailyTargetMinutes)
	{
		DateTime dayStartUtc = ZurichCalendar.StartOfDayUtc(i_Date);
		DateTime dayEndUtc = ZurichCalendar.StartOfDayUtc(i_Date.AddDays(1));
		double shiftSeconds = 0;
		double breakSeconds = 0;
		int shiftCount = 0;

		foreach (WorkSpan shift in i_Shifts)
		{
			DateTime shiftEndUtc = effectiveEnd(shift.StartUtc, shift.EndUtc, i_NowUtc);
			double overlap = overlapSeconds(shift.StartUtc, shiftEndUtc, dayStartUtc, dayEndUtc);

			if (overlap > 0 || isInside(shift.StartUtc, dayStartUtc, dayEndUtc))
			{
				shiftCount++;
				shiftSeconds += overlap;
				foreach (WorkSpan pause in shift.Breaks)
				{
					DateTime pauseEndUtc = effectiveEnd(pause.StartUtc, pause.EndUtc, shiftEndUtc);

					breakSeconds += overlapSeconds(pause.StartUtc, pauseEndUtc, dayStartUtc, dayEndUtc);
				}
			}
		}

		int breakMinutes = (int)(breakSeconds / 60);
		int workedMinutes = Math.Max(0, (int)((shiftSeconds - breakSeconds) / 60));

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
