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

	// The reading and the moment it was taken are one object, replaced in a single assignment.
	// A reader therefore never pairs a new reading with an old timestamp ("now" jumping back).
	private volatile Anchor? m_Anchor;

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
			Anchor? current = m_Anchor;

			if (current == null || Stopwatch.GetElapsedTime(current.Timestamp).TotalSeconds >= r_Options.DisplayMaxAgeSeconds)
			{
				await refreshAsync(i_CancellationToken);
			}
		}
		finally
		{
			r_RefreshLock.Release();
		}

		Anchor anchor = m_Anchor!;

		return new TimeReading { Utc = anchor.Reading.Utc + Stopwatch.GetElapsedTime(anchor.Timestamp), Source = anchor.Reading.Source };
	}

	private async Task refreshAsync(CancellationToken i_CancellationToken)
	{
		try
		{
			TimeReading reading = await r_TimeProvider.GetUtcNowAsync(i_CancellationToken);

			m_Anchor = new Anchor { Reading = reading, Timestamp = Stopwatch.GetTimestamp() };
		}
		catch (AppException) when (m_Anchor != null)
		{
			// The old anchor plus monotonic time is still correct, so display keeps working.
			r_Logger.LogWarning("Display clock could not refresh its anchor; continuing from the previous reading");
		}
	}

	/// <summary>One external reading and the Stopwatch timestamp at which it was taken.</summary>
	private class Anchor
	{
		public TimeReading Reading { get; init; } = new TimeReading();

		public long Timestamp { get; init; }
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
