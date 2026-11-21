using System.Buffers.Text;
using System.Security.Cryptography;
using StackExchange.Redis;
using TrackOps.Application.Abstractions;

namespace TrackOps.Infrastructure.Redis;

/// <summary>
/// Server-side sessions. The JWT carries the session id in its "sid" claim; a request is accepted
/// only while this key exists, so deleting it (sign-out) revokes the JWT at once on every replica.
/// </summary>
public class RedisSessionStore : ISessionStore
{
	private const int k_LifetimeSeconds = 86400;
	private const int k_SessionIdBytes = 32;
	private readonly IConnectionMultiplexer r_Redis;

	public RedisSessionStore(IConnectionMultiplexer i_Redis)
	{
		r_Redis = i_Redis;
	}

	public async Task<string> CreateAsync(int i_UserId)
	{
		string sessionId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(k_SessionIdBytes));

		await r_Redis.GetDatabase().StringSetAsync(key(sessionId), i_UserId, TimeSpan.FromSeconds(k_LifetimeSeconds));

		return sessionId;
	}

	public async Task<bool> IsActiveAsync(string i_SessionId, int i_UserId)
	{
		RedisValue stored = await r_Redis.GetDatabase().StringGetAsync(key(i_SessionId));

		return stored.HasValue && stored == i_UserId;
	}

	public Task RevokeAsync(string i_SessionId)
	{
		return r_Redis.GetDatabase().KeyDeleteAsync(key(i_SessionId));
	}

	private static RedisKey key(string i_SessionId)
	{
		return "session:" + i_SessionId;
	}
}

/// <summary>
/// Fixed one-minute window per bucket and subject. INCR and EXPIRE run in one Lua script, so the
/// count is atomic and shared by every replica.
/// </summary>
public class RedisRateLimiter : IRateLimiter
{
	private const int k_WindowSeconds = 60;
	private const string k_Script = @"
local count = redis.call('INCR', KEYS[1])
if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
return { count, redis.call('TTL', KEYS[1]) }";

	private readonly IConnectionMultiplexer r_Redis;

	public RedisRateLimiter(IConnectionMultiplexer i_Redis)
	{
		r_Redis = i_Redis;
	}

	public async Task<RateLimitDecision> CheckAsync(string i_Bucket, string i_Subject, int i_LimitPerMinute)
	{
		RedisKey[] keys = { $"rate:{i_Bucket}:{i_Subject}" };
		RedisValue[] arguments = { k_WindowSeconds };
		RedisResult[] reply = (RedisResult[])(await r_Redis.GetDatabase().ScriptEvaluateAsync(k_Script, keys, arguments))!;
		long count = (long)reply[0];
		long secondsLeft = (long)reply[1];

		return new RateLimitDecision
		{
			IsAllowed = count <= i_LimitPerMinute,
			RetryAfterSeconds = (int)Math.Max(1, secondsLeft),
		};
	}
}

/// <summary>
/// Read cache for calendar months. Each user has a version number that is part of every cache key;
/// any write to the user's shifts increments it, which orphans all of that user's cached months.
/// </summary>
public class RedisCalendarCache : ICalendarCache
{
	private const int k_LifetimeSeconds = 60;
	private readonly IConnectionMultiplexer r_Redis;

	public RedisCalendarCache(IConnectionMultiplexer i_Redis)
	{
		r_Redis = i_Redis;
	}

	public async Task<CachedValue> GetAsync(int i_UserId, string i_Key)
	{
		IDatabase database = r_Redis.GetDatabase();
		RedisValue version = await database.StringGetAsync(versionKey(i_UserId));
		CachedValue result = new CachedValue { Version = version.HasValue ? version.ToString() : "0" };
		RedisValue cached = await database.StringGetAsync(valueKey(i_UserId, result.Version, i_Key));

		result.Json = cached.HasValue ? cached.ToString() : null;

		return result;
	}

	public async Task SetAsync(int i_UserId, string i_Key, string i_Version, string i_Json)
	{
		await r_Redis.GetDatabase().StringSetAsync(valueKey(i_UserId, i_Version, i_Key), i_Json, TimeSpan.FromSeconds(k_LifetimeSeconds));
	}

	public Task InvalidateAsync(int i_UserId)
	{
		return r_Redis.GetDatabase().StringIncrementAsync(versionKey(i_UserId));
	}

	private static RedisKey valueKey(int i_UserId, string i_Version, string i_Key)
	{
		return $"calendar:{i_UserId}:{i_Version}:{i_Key}";
	}

	private static RedisKey versionKey(int i_UserId)
	{
		return $"calendar-version:{i_UserId}";
	}
}
