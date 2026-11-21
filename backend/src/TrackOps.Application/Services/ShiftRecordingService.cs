using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Services;

/// <summary>
/// The four recording actions: clock in, clock out, start break, end break.
/// Each one follows the same steps:
///   1. replay the stored response if this Idempotency-Key was already used;
///   2. check cheaply that the action makes sense (no lock, no external call);
///   3. read the time from the external time API (fails closed);
///   4. in one transaction: lock the open shift, re-check, write, store the response.
/// The database, not this class, guarantees "one open shift per user" and "one open break per shift".
/// </summary>
public class ShiftRecordingService
{
	private const string k_ClockIn = "clock-in";
	private const string k_ClockOut = "clock-out";
	private const string k_BreakStart = "break-start";
	private const string k_BreakEnd = "break-end";
	private const int k_Created = 201;
	private const int k_Ok = 200;
	private const int k_Conflict = 409;
	private readonly IAppDbContext r_Db;
	private readonly ITimeProvider r_TimeProvider;
	private readonly ICalendarCache r_CalendarCache;

	public ShiftRecordingService(IAppDbContext i_Db, ITimeProvider i_TimeProvider, ICalendarCache i_CalendarCache)
	{
		r_Db = i_Db;
		r_TimeProvider = i_TimeProvider;
		r_CalendarCache = i_CalendarCache;
	}

	public Task<RecordingResult> ClockInAsync(CurrentUser i_User, Guid i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		return recordAsync(
			i_User,
			i_IdempotencyKey,
			k_ClockIn,
			k_Created,
			async () =>
			{
				if (await hasOpenShiftAsync(i_User.Id, i_CancellationToken))
				{
					throw Problems.ShiftAlreadyOpen();
				}
			},
			reading =>
			{
				Shift shift = new Shift
				{
					UserId = i_User.Id,
					ClockInUtc = reading.Utc,
					Status = eShiftStatus.Open,
					TimeSource = reading.Source,
				};

				r_Db.Shifts.Add(shift);

				return Task.FromResult(shift);
			},
			i_CancellationToken);
	}

	public Task<RecordingResult> ClockOutAsync(CurrentUser i_User, Guid i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		return recordAsync(
			i_User,
			i_IdempotencyKey,
			k_ClockOut,
			k_Ok,
			() => ensureOpenShiftAsync(i_User.Id, i_CancellationToken),
			async reading =>
			{
				Shift shift = await lockOpenShiftAsync(i_User.Id, i_CancellationToken);
				Break? openBreak = shift.Breaks.FirstOrDefault(pause => pause.EndUtc == null);

				ensureTimeMovedForward(reading, shift);
				if (openBreak != null)
				{
					openBreak.EndUtc = reading.Utc;
				}

				shift.ClockOutUtc = reading.Utc;
				shift.Status = eShiftStatus.Closed;

				return shift;
			},
			i_CancellationToken);
	}

	public Task<RecordingResult> StartBreakAsync(CurrentUser i_User, Guid i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		return recordAsync(
			i_User,
			i_IdempotencyKey,
			k_BreakStart,
			k_Created,
			() => ensureOpenShiftAsync(i_User.Id, i_CancellationToken),
			async reading =>
			{
				Shift shift = await lockOpenShiftAsync(i_User.Id, i_CancellationToken);

				if (shift.Breaks.Any(pause => pause.EndUtc == null))
				{
					throw Problems.BreakAlreadyOpen();
				}

				ensureTimeMovedForward(reading, shift);
				shift.Breaks.Add(new Break { StartUtc = reading.Utc });

				return shift;
			},
			i_CancellationToken);
	}

	public Task<RecordingResult> EndBreakAsync(CurrentUser i_User, Guid i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		return recordAsync(
			i_User,
			i_IdempotencyKey,
			k_BreakEnd,
			k_Ok,
			() => ensureOpenShiftAsync(i_User.Id, i_CancellationToken),
			async reading =>
			{
				Shift shift = await lockOpenShiftAsync(i_User.Id, i_CancellationToken);
				Break? openBreak = shift.Breaks.FirstOrDefault(pause => pause.EndUtc == null);

				if (openBreak == null)
				{
					throw Problems.NoOpenBreak();
				}

				ensureTimeMovedForward(reading, shift);
				openBreak.EndUtc = reading.Utc;

				return shift;
			},
			i_CancellationToken);
	}

	private async Task<RecordingResult> recordAsync(
		CurrentUser i_User,
		Guid i_IdempotencyKey,
		string i_Endpoint,
		int i_SuccessStatus,
		Func<Task> i_Precheck,
		Func<TimeReading, Task<Shift>> i_Write,
		CancellationToken i_CancellationToken)
	{
		RecordingResult? result = await findStoredAsync(i_User.Id, i_IdempotencyKey, i_Endpoint, i_CancellationToken);

		if (result == null)
		{
			try
			{
				await i_Precheck();

				TimeReading reading = await r_TimeProvider.GetUtcNowAsync(i_CancellationToken);

				await using IDbContextTransaction transaction = await r_Db.Database.BeginTransactionAsync(i_CancellationToken);
				Shift shift = await i_Write(reading);

				await r_Db.SaveChangesAsync(i_CancellationToken);

				string json = JsonSerializer.Serialize(
					ShiftMapper.FromEntity(shift, i_User.Username, i_User.DisplayName),
					JsonDefaults.sr_Options);

				r_Db.IdempotencyRecords.Add(new IdempotencyRecord
				{
					UserId = i_User.Id,
					Key = i_IdempotencyKey,
					Endpoint = i_Endpoint,
					StatusCode = i_SuccessStatus,
					ResponseJson = json,
					CreatedAtUtc = reading.Utc,
				});
				await r_Db.SaveChangesAsync(i_CancellationToken);
				await transaction.CommitAsync(i_CancellationToken);
				result = new RecordingResult { StatusCode = i_SuccessStatus, Json = json };
			}
			catch (DbUpdateException exception) when (isDuplicateRecording(exception))
			{
				// The same key (or a concurrent clock-in) won the race. If our key is now stored, replay it.
				r_Db.ClearTracking();
				result = await findStoredAsync(i_User.Id, i_IdempotencyKey, i_Endpoint, i_CancellationToken);
				if (result == null)
				{
					throw r_Db.IsUniqueViolation(exception, DbNames.k_OneOpenBreakIndex)
						? Problems.BreakAlreadyOpen()
						: Problems.ShiftAlreadyOpen();
				}
			}
			catch (AppException conflict) when (conflict.StatusCode == k_Conflict)
			{
				// "Already open" or "nothing open" may be the trace of this very request: a retry with
				// the same key that finished while this one was running. If so, replay its response.
				r_Db.ClearTracking();
				result = await findStoredAsync(i_User.Id, i_IdempotencyKey, i_Endpoint, i_CancellationToken);
				if (result == null)
				{
					throw;
				}
			}

			await r_CalendarCache.InvalidateAsync(i_User.Id);
		}

		return result;
	}

	private bool isDuplicateRecording(DbUpdateException i_Exception)
	{
		return r_Db.IsUniqueViolation(i_Exception, DbNames.k_IdempotencyKey)
			|| r_Db.IsUniqueViolation(i_Exception, DbNames.k_OneOpenShiftIndex)
			|| r_Db.IsUniqueViolation(i_Exception, DbNames.k_OneOpenBreakIndex);
	}

	private async Task<RecordingResult?> findStoredAsync(int i_UserId, Guid i_Key, string i_Endpoint, CancellationToken i_CancellationToken)
	{
		RecordingResult? result = null;
		IdempotencyRecord? stored = await r_Db.IdempotencyRecords
			.AsNoTracking()
			.FirstOrDefaultAsync(record => record.UserId == i_UserId && record.Key == i_Key, i_CancellationToken);

		if (stored != null)
		{
			if (stored.Endpoint != i_Endpoint)
			{
				throw Problems.IdempotencyKeyReused();
			}

			result = new RecordingResult { StatusCode = stored.StatusCode, Json = stored.ResponseJson, IsReplay = true };
		}

		return result;
	}

	private Task<bool> hasOpenShiftAsync(int i_UserId, CancellationToken i_CancellationToken)
	{
		return r_Db.Shifts.AsNoTracking().AnyAsync(shift => shift.UserId == i_UserId && shift.ClockOutUtc == null, i_CancellationToken);
	}

	private async Task ensureOpenShiftAsync(int i_UserId, CancellationToken i_CancellationToken)
	{
		if (!await hasOpenShiftAsync(i_UserId, i_CancellationToken))
		{
			throw Problems.NoOpenShift();
		}
	}

	private async Task<Shift> lockOpenShiftAsync(int i_UserId, CancellationToken i_CancellationToken)
	{
		Shift? shift = await r_Db.LockOpenShiftAsync(i_UserId, i_CancellationToken);

		if (shift == null)
		{
			throw Problems.NoOpenShift();
		}

		return shift;
	}

	/// <summary>
	/// A new reading must be later than everything already recorded on the shift.
	/// If it is not, the time source is wrong, so nothing is written.
	/// </summary>
	private static void ensureTimeMovedForward(TimeReading i_Reading, Shift i_Shift)
	{
		DateTime latest = i_Shift.ClockInUtc;

		foreach (Break pause in i_Shift.Breaks)
		{
			DateTime pauseLatest = pause.EndUtc ?? pause.StartUtc;

			latest = pauseLatest > latest ? pauseLatest : latest;
		}

		if (i_Reading.Utc <= latest)
		{
			throw Problems.TimeSourceUnavailable();
		}
	}
}
