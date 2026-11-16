using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;
using TrackOps.Api.Auth;
using TrackOps.Application;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;

namespace TrackOps.Api.Middleware;

/// <summary>Writes every error in one shape: RFC 9457 problem+json with a stable "code".</summary>
public static class ProblemWriter
{
	private const string k_ContentType = "application/problem+json";
	private const string k_TypePrefix = "https://trackops.local/problems/";

	public static async Task WriteAsync(HttpContext i_Context, AppException i_Problem)
	{
		Dictionary<string, object?> body = new Dictionary<string, object?>
		{
			["type"] = k_TypePrefix + i_Problem.Code,
			["title"] = i_Problem.Message,
			["status"] = i_Problem.StatusCode,
			["code"] = i_Problem.Code,
			["traceId"] = Activity.Current?.Id ?? i_Context.TraceIdentifier,
		};

		if (i_Problem.Errors != null)
		{
			body["errors"] = i_Problem.Errors;
		}

		if (i_Problem.RetryAfterSeconds.HasValue)
		{
			i_Context.Response.Headers.RetryAfter = i_Problem.RetryAfterSeconds.Value.ToString();
		}

		i_Context.Response.StatusCode = i_Problem.StatusCode;
		i_Context.Response.ContentType = k_ContentType;
		await i_Context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonDefaults.sr_Options));
	}
}

/// <summary>Turns exceptions into problems. Unexpected errors are logged in full and reported without details.</summary>
public class ExceptionMiddleware
{
	private readonly RequestDelegate r_Next;
	private readonly ILogger<ExceptionMiddleware> r_Logger;

	public ExceptionMiddleware(RequestDelegate i_Next, ILogger<ExceptionMiddleware> i_Logger)
	{
		r_Next = i_Next;
		r_Logger = i_Logger;
	}

	public async Task InvokeAsync(HttpContext i_Context)
	{
		try
		{
			await r_Next(i_Context);
		}
		catch (AppException problem)
		{
			await ProblemWriter.WriteAsync(i_Context, problem);
		}
		catch (BadHttpRequestException)
		{
			await ProblemWriter.WriteAsync(i_Context, new AppException(StatusCodes.Status400BadRequest, "validation_failed", "The request could not be read"));
		}
		catch (OperationCanceledException) when (i_Context.RequestAborted.IsCancellationRequested)
		{
			// The client went away; there is nobody to answer.
		}
		catch (Exception exception) when (exception is RedisException || exception is SqlException)
		{
			r_Logger.LogError(exception, "A dependency is unavailable");
			await ProblemWriter.WriteAsync(
				i_Context,
				new AppException(StatusCodes.Status503ServiceUnavailable, "dependency_unavailable", "The service is temporarily unavailable"));
		}
		catch (Exception exception)
		{
			r_Logger.LogError(exception, "Unhandled error");
			await ProblemWriter.WriteAsync(
				i_Context,
				new AppException(StatusCodes.Status500InternalServerError, "internal_error", "Something went wrong"));
		}
	}
}

/// <summary>Headers for every API response. The gateway adds the page-level ones (CSP and friends) for all responses.</summary>
public class SecurityHeadersMiddleware
{
	private const string k_Hsts = "max-age=31536000; includeSubDomains";
	private readonly RequestDelegate r_Next;
	private readonly SecurityOptions r_Security;

	public SecurityHeadersMiddleware(RequestDelegate i_Next, SecurityOptions i_Security)
	{
		r_Next = i_Next;
		r_Security = i_Security;
	}

	public Task InvokeAsync(HttpContext i_Context)
	{
		IHeaderDictionary headers = i_Context.Response.Headers;

		headers.XContentTypeOptions = "nosniff";
		headers.CacheControl = "no-store";
		headers["Referrer-Policy"] = "no-referrer";
		headers["X-Frame-Options"] = "DENY";
		headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
		if (r_Security.RequireHttps)
		{
			headers.StrictTransportSecurity = k_Hsts;
		}

		if (!string.IsNullOrEmpty(r_Security.ReplicaName))
		{
			headers["X-Replica"] = r_Security.ReplicaName;
		}

		return r_Next(i_Context);
	}
}

/// <summary>Rejects state-changing requests that do not carry a valid CSRF token. GET, HEAD, OPTIONS and QUERY are safe.</summary>
public class CsrfMiddleware
{
	private static readonly HashSet<string> sr_SafeMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "QUERY" };
	private readonly RequestDelegate r_Next;
	private readonly CsrfTokens r_Tokens;
	private readonly ILogger<CsrfMiddleware> r_Logger;

	public CsrfMiddleware(RequestDelegate i_Next, CsrfTokens i_Tokens, ILogger<CsrfMiddleware> i_Logger)
	{
		r_Next = i_Next;
		r_Tokens = i_Tokens;
		r_Logger = i_Logger;
	}

	public Task InvokeAsync(HttpContext i_Context)
	{
		HttpRequest request = i_Context.Request;

		if (!sr_SafeMethods.Contains(request.Method))
		{
			bool isValid = r_Tokens.IsValid(
				request.Headers[CsrfTokens.k_HeaderName].ToString(),
				request.Cookies[AuthCookies.k_Csrf],
				request.Cookies[AuthCookies.k_SessionId]);

			if (!isValid)
			{
				r_Logger.LogWarning("security_event=csrf_rejected method={Method} path={Path} ip={Ip}", request.Method, request.Path, i_Context.Connection.RemoteIpAddress);
				throw Problems.CsrfInvalid();
			}
		}

		return r_Next(i_Context);
	}
}

public enum eRateLimitBucket
{
	Default,
	SignIn,
	Recording,
	Search,
}

/// <summary>Marks an endpoint with the rate-limit bucket it counts against.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class RateLimitAttribute : Attribute
{
	public RateLimitAttribute(eRateLimitBucket i_Bucket)
	{
		Bucket = i_Bucket;
	}

	public eRateLimitBucket Bucket { get; }
}

public class RateLimitOptions
{
	public int SignInPerMinute { get; set; } = 10;

	public int RecordingPerMinute { get; set; } = 30;

	public int SearchPerMinute { get; set; } = 60;

	public int DefaultPerMinute { get; set; } = 240;
}

/// <summary>Per-minute limits counted in Redis. Sign-in is limited per client IP, everything else per user.</summary>
public class RateLimitMiddleware
{
	private readonly RequestDelegate r_Next;
	private readonly IRateLimiter r_Limiter;
	private readonly RateLimitOptions r_Options;
	private readonly ILogger<RateLimitMiddleware> r_Logger;

	public RateLimitMiddleware(RequestDelegate i_Next, IRateLimiter i_Limiter, RateLimitOptions i_Options, ILogger<RateLimitMiddleware> i_Logger)
	{
		r_Next = i_Next;
		r_Limiter = i_Limiter;
		r_Options = i_Options;
		r_Logger = i_Logger;
	}

	public async Task InvokeAsync(HttpContext i_Context)
	{
		eRateLimitBucket bucket = i_Context.GetEndpoint()?.Metadata.GetMetadata<RateLimitAttribute>()?.Bucket ?? eRateLimitBucket.Default;
		string? userId = i_Context.User.FindFirstValue(ClaimNames.k_UserId);
		string subject = bucket == eRateLimitBucket.SignIn || userId == null
			? "ip:" + i_Context.Connection.RemoteIpAddress
			: "user:" + userId;
		RateLimitDecision decision = await r_Limiter.CheckAsync(bucket.ToString(), subject, limitFor(bucket));

		if (!decision.IsAllowed)
		{
			r_Logger.LogWarning("security_event=rate_limited bucket={Bucket} subject={Subject} path={Path}", bucket, subject, i_Context.Request.Path);
			throw Problems.RateLimited(decision.RetryAfterSeconds);
		}

		await r_Next(i_Context);
	}

	private int limitFor(eRateLimitBucket i_Bucket)
	{
		int limit = r_Options.DefaultPerMinute;

		switch (i_Bucket)
		{
			case eRateLimitBucket.SignIn:
				limit = r_Options.SignInPerMinute;
				break;
			case eRateLimitBucket.Recording:
				limit = r_Options.RecordingPerMinute;
				break;
			case eRateLimitBucket.Search:
				limit = r_Options.SearchPerMinute;
				break;
		}

		return limit;
	}
}
