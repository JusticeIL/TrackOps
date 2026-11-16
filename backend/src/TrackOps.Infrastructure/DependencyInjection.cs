using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using StackExchange.Redis;
using TrackOps.Application.Abstractions;
using TrackOps.Infrastructure.Jobs;
using TrackOps.Infrastructure.Persistence;
using TrackOps.Infrastructure.Redis;
using TrackOps.Infrastructure.Time;

namespace TrackOps.Infrastructure;

public static class DependencyInjection
{
	private const int k_CircuitSamplingSeconds = 30;
	private const int k_CircuitMinimumCalls = 4;
	private const double k_CircuitFailureRatio = 0.5;
	private const int k_RetryDelayMilliseconds = 200;

	public static IServiceCollection AddInfrastructure(this IServiceCollection i_Services, IConfiguration i_Configuration)
	{
		TimeApiOptions timeApiOptions = i_Configuration.GetSection("TimeApi").Get<TimeApiOptions>() ?? new TimeApiOptions();
		AutoCloseOptions autoCloseOptions = i_Configuration.GetSection("AutoClose").Get<AutoCloseOptions>() ?? new AutoCloseOptions();
		string databaseConnection = requireSetting(i_Configuration, "ConnectionStrings:Database");
		string redisConnection = requireSetting(i_Configuration, "ConnectionStrings:Redis");

		// The connection pool is on by default; the two replicas each keep their own pool.
		i_Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(databaseConnection));
		i_Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

		i_Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
		i_Services.AddSingleton<ISessionStore, RedisSessionStore>();
		i_Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
		i_Services.AddSingleton<ICalendarCache, RedisCalendarCache>();

		i_Services.AddSingleton(timeApiOptions);
		i_Services.AddSingleton<ITimeProvider, ExternalTimeProvider>();
		i_Services.AddSingleton<IDisplayClock, DisplayClock>();
		i_Services.AddSingleton<ISystemClock, SystemClock>();
		addTimeClient(i_Services, ExternalTimeProvider.k_PrimaryClient, timeApiOptions, 1);
		addTimeClient(i_Services, ExternalTimeProvider.k_SecondaryClient, timeApiOptions, 0);

		i_Services.AddSingleton(autoCloseOptions);
		i_Services.AddHostedService<ShiftAutoCloseJob>();

		return i_Services;
	}

	/// <summary>Per attempt: a short timeout. Around it: a circuit breaker, then a bounded retry.</summary>
	private static void addTimeClient(IServiceCollection i_Services, string i_Name, TimeApiOptions i_Options, int i_Retries)
	{
		i_Services.AddHttpClient(i_Name).AddResilienceHandler(i_Name, pipeline =>
		{
			if (i_Retries > 0)
			{
				pipeline.AddRetry(new HttpRetryStrategyOptions
				{
					MaxRetryAttempts = i_Retries,
					Delay = TimeSpan.FromMilliseconds(k_RetryDelayMilliseconds),
					BackoffType = DelayBackoffType.Constant,
				});
			}

			pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
			{
				SamplingDuration = TimeSpan.FromSeconds(k_CircuitSamplingSeconds),
				MinimumThroughput = k_CircuitMinimumCalls,
				FailureRatio = k_CircuitFailureRatio,
				BreakDuration = TimeSpan.FromSeconds(i_Options.CircuitBreakSeconds),
			});
			pipeline.AddTimeout(TimeSpan.FromSeconds(i_Options.TimeoutSeconds));
		});
	}

	private static string requireSetting(IConfiguration i_Configuration, string i_Key)
	{
		string? value = i_Configuration[i_Key];

		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Missing required setting '{i_Key}'. Set it through the environment; see .env.example.");
		}

		return value;
	}
}
