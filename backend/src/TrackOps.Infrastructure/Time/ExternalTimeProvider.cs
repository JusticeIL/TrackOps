using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TrackOps.Application;
using TrackOps.Application.Abstractions;
using TrackOps.Domain;

namespace TrackOps.Infrastructure.Time;

public class TimeApiOptions
{
	public string PrimaryUrl { get; set; } = "https://timeapi.io/api/timezone/zone?timeZone=Europe%2FZurich";

	public string SecondaryUrl { get; set; } = "https://time.now/developer/api/timezone/Europe/Zurich";

	public int TimeoutSeconds { get; set; } = 2;

	public int CircuitBreakSeconds { get; set; } = 15;

	public int DisplayMaxAgeSeconds { get; set; } = 60;
}

/// <summary>
/// Reads the current Europe/Zurich time from an external API and returns it as a UTC instant.
/// Order: primary provider (short timeout, one retry), then the secondary provider, then fail closed.
/// There is deliberately no fallback to the server clock anywhere in this class.
/// </summary>
public class ExternalTimeProvider : ITimeProvider
{
	public const string k_PrimaryClient = "time-primary";
	public const string k_SecondaryClient = "time-secondary";
	private const int k_EarliestPlausibleYear = 2020;
	private const int k_LatestPlausibleYear = 2100;
	private readonly IHttpClientFactory r_HttpClientFactory;
	private readonly TimeApiOptions r_Options;
	private readonly ILogger<ExternalTimeProvider> r_Logger;

	public ExternalTimeProvider(IHttpClientFactory i_HttpClientFactory, TimeApiOptions i_Options, ILogger<ExternalTimeProvider> i_Logger)
	{
		r_HttpClientFactory = i_HttpClientFactory;
		r_Options = i_Options;
		r_Logger = i_Logger;
	}

	public async Task<TimeReading> GetUtcNowAsync(CancellationToken i_CancellationToken)
	{
		TimeReading? reading = await tryReadAsync(k_PrimaryClient, r_Options.PrimaryUrl, TimeApiParsers.ParseTimeApiIo, i_CancellationToken)
			?? await tryReadAsync(k_SecondaryClient, r_Options.SecondaryUrl, TimeApiParsers.ParseTimeNow, i_CancellationToken);

		if (reading == null)
		{
			r_Logger.LogError("security_event=time_source_unavailable all external time providers failed; failing closed");
			throw Problems.TimeSourceUnavailable();
		}

		return reading;
	}

	private async Task<TimeReading?> tryReadAsync(string i_ClientName, string i_Url, Func<string, DateTime> i_Parse, CancellationToken i_CancellationToken)
	{
		TimeReading? reading = null;

		try
		{
			HttpClient client = r_HttpClientFactory.CreateClient(i_ClientName);
			using HttpResponseMessage response = await client.GetAsync(i_Url, i_CancellationToken);

			response.EnsureSuccessStatusCode();

			string body = await response.Content.ReadAsStringAsync(i_CancellationToken);
			DateTime utc = Instants.TruncateToMilliseconds(i_Parse(body));

			if (utc.Year < k_EarliestPlausibleYear || utc.Year > k_LatestPlausibleYear)
			{
				throw new FormatException($"Implausible time {utc:O}");
			}

			reading = new TimeReading { Utc = utc, Source = i_ClientName };
		}
		catch (Exception exception) when (isProviderFailure(exception) && !i_CancellationToken.IsCancellationRequested)
		{
			r_Logger.LogWarning("security_event=time_api_failure provider={Provider} error={Error}", i_ClientName, exception.GetType().Name);
		}

		return reading;
	}

	private static bool isProviderFailure(Exception i_Exception)
	{
		return i_Exception is HttpRequestException
			|| i_Exception is TaskCanceledException
			|| i_Exception is TimeoutRejectedException
			|| i_Exception is BrokenCircuitException
			|| i_Exception is JsonException
			|| i_Exception is FormatException
			|| i_Exception is KeyNotFoundException
			|| i_Exception is InvalidOperationException;
	}
}

/// <summary>Turns each provider's JSON into a UTC instant. Both must report Europe/Zurich.</summary>
public static class TimeApiParsers
{
	/// <summary>timeapi.io: local wall time plus the current UTC offset, which stays unambiguous in the repeated DST hour.</summary>
	public static DateTime ParseTimeApiIo(string i_Json)
	{
		using JsonDocument document = JsonDocument.Parse(i_Json);
		JsonElement root = document.RootElement;

		ensureZone(root.GetProperty("timeZone").GetString());

		// RoundtripKind keeps a zone marker if the provider ever sends one, instead of converting the
		// value into this server's own time zone. A wall time must carry no zone; anything else is a fault.
		DateTime local = DateTime.Parse(root.GetProperty("currentLocalTime").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		int offsetSeconds = root.GetProperty("currentUtcOffset").GetProperty("seconds").GetInt32();

		if (local.Kind != DateTimeKind.Unspecified)
		{
			throw new FormatException("currentLocalTime must be a wall time without a zone");
		}

		return DateTime.SpecifyKind(local.AddSeconds(-offsetSeconds), DateTimeKind.Utc);
	}

	/// <summary>time.now: reports the UTC instant directly.</summary>
	public static DateTime ParseTimeNow(string i_Json)
	{
		using JsonDocument document = JsonDocument.Parse(i_Json);
		JsonElement root = document.RootElement;

		ensureZone(root.GetProperty("timezone").GetString());

		DateTimeOffset instant = DateTimeOffset.Parse(
			root.GetProperty("utc_datetime").GetString()!,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal);

		return instant.UtcDateTime;
	}

	private static void ensureZone(string? i_Zone)
	{
		if (i_Zone != ZurichCalendar.k_ZoneId)
		{
			throw new FormatException($"Unexpected time zone '{i_Zone}'");
		}
	}
}
