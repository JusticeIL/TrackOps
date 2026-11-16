namespace TrackOps.Application;

/// <summary>A failure the API reports to the client as an RFC 9457 problem with a stable code.</summary>
public class AppException : Exception
{
	public AppException(int i_StatusCode, string i_Code, string i_Title)
		: base(i_Title)
	{
		StatusCode = i_StatusCode;
		Code = i_Code;
	}

	public int StatusCode { get; }

	public string Code { get; }

	public Dictionary<string, string[]>? Errors { get; set; }

	public int? RetryAfterSeconds { get; set; }
}

public static class Problems
{
	public static AppException Validation(Dictionary<string, string[]> i_Errors)
	{
		return new AppException(400, "validation_failed", "The request is not valid") { Errors = i_Errors };
	}

	public static AppException IdempotencyKeyRequired()
	{
		return new AppException(400, "idempotency_key_required", "An Idempotency-Key header holding a UUID is required");
	}

	public static AppException Unauthenticated()
	{
		return new AppException(401, "unauthenticated", "Sign in to continue");
	}

	public static AppException Forbidden()
	{
		return new AppException(403, "forbidden", "This action needs the manager role");
	}

	public static AppException CsrfInvalid()
	{
		return new AppException(403, "csrf_invalid", "The CSRF token is missing or does not match");
	}

	public static AppException NotFound()
	{
		return new AppException(404, "not_found", "Not found");
	}

	public static AppException ShiftAlreadyOpen()
	{
		return new AppException(409, "shift_already_open", "A shift is already open");
	}

	public static AppException NoOpenShift()
	{
		return new AppException(409, "no_open_shift", "There is no open shift");
	}

	public static AppException BreakAlreadyOpen()
	{
		return new AppException(409, "break_already_open", "A break is already open");
	}

	public static AppException NoOpenBreak()
	{
		return new AppException(409, "no_open_break", "There is no open break");
	}

	public static AppException StaleVersion()
	{
		return new AppException(409, "stale_version", "The shift changed since it was loaded");
	}

	public static AppException ShiftOverlap()
	{
		return new AppException(409, "shift_overlap", "The corrected times overlap another shift of this employee");
	}

	public static AppException IdempotencyKeyReused()
	{
		return new AppException(409, "idempotency_key_reused", "This Idempotency-Key was already used for a different action");
	}

	public static AppException RateLimited(int i_RetryAfterSeconds)
	{
		return new AppException(429, "rate_limited", "Too many requests") { RetryAfterSeconds = i_RetryAfterSeconds };
	}

	public static AppException TimeSourceUnavailable()
	{
		const int k_RetryAfterSeconds = 5;

		return new AppException(503, "time_source_unavailable", "The external time source is unavailable, so nothing was recorded")
		{
			RetryAfterSeconds = k_RetryAfterSeconds,
		};
	}
}

/// <summary>Collects field errors and throws one validation problem.</summary>
public class ValidationErrors
{
	private readonly Dictionary<string, string[]> r_Errors = new Dictionary<string, string[]>();

	public void Add(string i_Field, string i_Message)
	{
		r_Errors[i_Field] = new[] { i_Message };
	}

	public void ThrowIfAny()
	{
		if (r_Errors.Count > 0)
		{
			throw Problems.Validation(r_Errors);
		}
	}
}
