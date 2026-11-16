namespace TrackOps.Domain;

/// <summary>
/// Converts between UTC instants and Europe/Zurich local days.
/// A local day is the span between two local midnights, so it lasts 23 h or 25 h on DST days.
/// </summary>
public static class ZurichCalendar
{
	public const string k_ZoneId = "Europe/Zurich";
	private static readonly TimeZoneInfo sr_Zone = TimeZoneInfo.FindSystemTimeZoneById(k_ZoneId);

	public static DateOnly LocalDateOf(DateTime i_Utc)
	{
		DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(i_Utc, DateTimeKind.Utc), sr_Zone);

		return DateOnly.FromDateTime(local);
	}

	/// <summary>UTC instant of local midnight at the start of the given day. Midnight is never skipped or repeated in Zurich.</summary>
	public static DateTime StartOfDayUtc(DateOnly i_LocalDate)
	{
		DateTime localMidnight = i_LocalDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

		return TimeZoneInfo.ConvertTimeToUtc(localMidnight, sr_Zone);
	}

	public static int UtcOffsetSecondsAt(DateTime i_Utc)
	{
		TimeSpan offset = sr_Zone.GetUtcOffset(DateTime.SpecifyKind(i_Utc, DateTimeKind.Utc));

		return (int)offset.TotalSeconds;
	}
}
