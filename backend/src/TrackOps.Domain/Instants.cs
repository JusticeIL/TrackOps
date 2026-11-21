namespace TrackOps.Domain;

/// <summary>Helpers for UTC instants. The database stores datetime2(3), so an instant keeps whole milliseconds.</summary>
public static class Instants
{
	/// <summary>Drops the sub-millisecond part, so the value in memory equals the value stored.</summary>
	public static DateTime TruncateToMilliseconds(DateTime i_Utc)
	{
		return new DateTime(i_Utc.Ticks - (i_Utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
	}
}
