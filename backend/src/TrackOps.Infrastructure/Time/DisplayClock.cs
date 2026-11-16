using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TrackOps.Application;
using TrackOps.Application.Abstractions;

namespace TrackOps.Infrastructure.Time;

/// <summary>
/// Approximate "now" for display and for running totals of an open shift: one external reading
/// (the anchor) plus monotonic elapsed time from Stopwatch. It never reads the wall clock and is
/// never used for recording. The anchor is refreshed from the external API when it gets old.
/// </summary>
public class DisplayClock : IDisplayClock
{
	private readonly ITimeProvider r_TimeProvider;
	private readonly TimeApiOptions r_Options;
	private readonly ILogger<DisplayClock> r_Logger;
	private readonly SemaphoreSlim r_RefreshLock = new SemaphoreSlim(1, 1);
	private TimeReading? m_Anchor;
	private long m_AnchorTimestamp;

	public DisplayClock(ITimeProvider i_TimeProvider, TimeApiOptions i_Options, ILogger<DisplayClock> i_Logger)
	{
		r_TimeProvider = i_TimeProvider;
		r_Options = i_Options;
		r_Logger = i_Logger;
	}

	public async Task<TimeReading> GetUtcNowAsync(CancellationToken i_CancellationToken)
	{
		await r_RefreshLock.WaitAsync(i_CancellationToken);
		try
		{
			if (m_Anchor == null || Stopwatch.GetElapsedTime(m_AnchorTimestamp).TotalSeconds >= r_Options.DisplayMaxAgeSeconds)
			{
				await refreshAsync(i_CancellationToken);
			}
		}
		finally
		{
			r_RefreshLock.Release();
		}

		return new TimeReading { Utc = m_Anchor!.Utc + Stopwatch.GetElapsedTime(m_AnchorTimestamp), Source = m_Anchor.Source };
	}

	private async Task refreshAsync(CancellationToken i_CancellationToken)
	{
		try
		{
			TimeReading reading = await r_TimeProvider.GetUtcNowAsync(i_CancellationToken);

			m_AnchorTimestamp = Stopwatch.GetTimestamp();
			m_Anchor = reading;
		}
		catch (AppException) when (m_Anchor != null)
		{
			// The old anchor plus monotonic time is still correct, so display keeps working.
			r_Logger.LogWarning("Display clock could not refresh its anchor; continuing from the previous reading");
		}
	}
}

/// <summary>
/// The server's own clock, isolated in the time module. It is used only for token lifetimes,
/// which the JWT validator also checks against the server clock. It is never used for recording.
/// </summary>
public class SystemClock : ISystemClock
{
	public DateTime UtcNow
	{
		get { return DateTime.UtcNow; }
	}
}
