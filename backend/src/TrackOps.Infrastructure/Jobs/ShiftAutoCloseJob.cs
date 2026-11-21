using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TrackOps.Application;
using TrackOps.Application.Abstractions;
using TrackOps.Domain.Entities;

namespace TrackOps.Infrastructure.Jobs;

public class AutoCloseOptions
{
	public bool IsEnabled { get; set; } = true;

	public int IntervalSeconds { get; set; } = 300;
}

/// <summary>
/// Handles the forgotten clock-out: a shift open longer than the allowed maximum is closed at
/// clock-in + maximum, marked AutoClosed and written to the audit trail, so a manager can correct it.
/// A short-lived Redis lock stops two replicas from running a round at the same moment. If the
/// external time is unavailable the round is skipped: the job fails closed like every other writer.
/// </summary>
public class ShiftAutoCloseJob : BackgroundService
{
	private const string k_LockKey = "lock:shift-auto-close";
	private const int k_MaxLockSeconds = 30;
	private const int k_BatchSize = 100;
	private const int k_IdempotencyRetentionHours = 24;
	private readonly IServiceScopeFactory r_ScopeFactory;
	private readonly IConnectionMultiplexer r_Redis;
	private readonly AutoCloseOptions r_Options;
	private readonly WorkRules r_WorkRules;
	private readonly ILogger<ShiftAutoCloseJob> r_Logger;

	public ShiftAutoCloseJob(
		IServiceScopeFactory i_ScopeFactory,
		IConnectionMultiplexer i_Redis,
		AutoCloseOptions i_Options,
		WorkRules i_WorkRules,
		ILogger<ShiftAutoCloseJob> i_Logger)
	{
		r_ScopeFactory = i_ScopeFactory;
		r_Redis = i_Redis;
		r_Options = i_Options;
		r_WorkRules = i_WorkRules;
		r_Logger = i_Logger;
	}

	protected override async Task ExecuteAsync(CancellationToken i_StoppingToken)
	{
		TimeSpan interval = TimeSpan.FromSeconds(r_Options.IntervalSeconds);

		// The lock only has to outlive one round. Tying it to the interval would let a lock left by a
		// deployment with a long interval block the job after a redeploy.
		TimeSpan lockLifetime = TimeSpan.FromSeconds(Math.Min(r_Options.IntervalSeconds, k_MaxLockSeconds));

		while (r_Options.IsEnabled && !i_StoppingToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(interval, i_StoppingToken);

				bool hasLock = await r_Redis.GetDatabase().StringSetAsync(k_LockKey, Environment.MachineName, lockLifetime, When.NotExists);

				if (hasLock)
				{
					await runRoundAsync(i_StoppingToken);
				}
			}
			catch (OperationCanceledException) when (i_StoppingToken.IsCancellationRequested)
			{
				// Normal shutdown.
			}
			catch (AppException exception)
			{
				r_Logger.LogWarning("Auto-close round skipped: {Code}", exception.Code);
			}
			catch (Exception exception)
			{
				r_Logger.LogError(exception, "Auto-close round failed");
			}
		}
	}

	private async Task runRoundAsync(CancellationToken i_CancellationToken)
	{
		using IServiceScope scope = r_ScopeFactory.CreateScope();
		IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
		ITimeProvider timeProvider = scope.ServiceProvider.GetRequiredService<ITimeProvider>();
		ICalendarCache calendarCache = scope.ServiceProvider.GetRequiredService<ICalendarCache>();
		TimeReading now = await timeProvider.GetUtcNowAsync(i_CancellationToken);
		TimeSpan maxLength = TimeSpan.FromHours(r_WorkRules.MaxShiftHours);
		DateTime cutoffUtc = now.Utc - maxLength;
		List<Shift> forgotten = await db.Shifts
			.Include(shift => shift.Break)
			.Where(shift => shift.ClockOutUtc == null && shift.ClockInUtc < cutoffUtc)
			.OrderBy(shift => shift.ClockInUtc)
			.Take(k_BatchSize)
			.ToListAsync(i_CancellationToken);

		foreach (Shift shift in forgotten)
		{
			DateTime closeAtUtc = chooseCloseInstant(shift, maxLength);

			if (shift.Break != null && shift.Break.EndUtc == null)
			{
				shift.Break.EndUtc = closeAtUtc;
			}

			db.ShiftAudits.Add(new ShiftAudit
			{
				ShiftId = shift.Id,
				ChangedAtUtc = now.Utc,
				Kind = eAuditKind.AutoClose,
				Reason = $"Open longer than {r_WorkRules.MaxShiftHours} hours; closed automatically",
				OldClockInUtc = shift.ClockInUtc,
				OldClockOutUtc = null,
				NewClockInUtc = shift.ClockInUtc,
				NewClockOutUtc = closeAtUtc,
			});
			shift.ClockOutUtc = closeAtUtc;
			shift.Status = eShiftStatus.AutoClosed;
		}

		if (forgotten.Count > 0)
		{
			await saveIgnoringRacesAsync(db, i_CancellationToken);
			foreach (int userId in forgotten.Select(shift => shift.UserId).Distinct())
			{
				await calendarCache.InvalidateAsync(userId);
			}

			r_Logger.LogInformation("Auto-closed {Count} forgotten shifts", forgotten.Count);
		}

		DateTime retentionCutoffUtc = now.Utc.AddHours(-k_IdempotencyRetentionHours);

		await db.IdempotencyRecords.Where(record => record.CreatedAtUtc < retentionCutoffUtc).ExecuteDeleteAsync(i_CancellationToken);
	}

	/// <summary>Clock-in + maximum, but never earlier than something already recorded on the shift.</summary>
	private static DateTime chooseCloseInstant(Shift i_Shift, TimeSpan i_MaxLength)
	{
		DateTime closeAtUtc = i_Shift.ClockInUtc + i_MaxLength;

		if (i_Shift.Break != null)
		{
			DateTime afterPause = (i_Shift.Break.EndUtc ?? i_Shift.Break.StartUtc).AddMilliseconds(1);

			closeAtUtc = afterPause > closeAtUtc ? afterPause : closeAtUtc;
		}

		return closeAtUtc;
	}

	private async Task saveIgnoringRacesAsync(IAppDbContext i_Db, CancellationToken i_CancellationToken)
	{
		try
		{
			await i_Db.SaveChangesAsync(i_CancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			// An employee clocked out while this round ran. Their real clock-out wins; retry next round.
			r_Logger.LogInformation("Auto-close lost a race with a real clock-out; nothing was changed");
		}
	}
}
