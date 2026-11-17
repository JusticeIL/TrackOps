using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using TrackOps.Api.Auth;
using TrackOps.Api.Middleware;
using TrackOps.Application;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Application.Services;
using TrackOps.Domain;
using TrackOps.Domain.Entities;
using TrackOps.Infrastructure;

namespace TrackOps.Api;

public static class Program
{
	private const int k_MaxRequestBodyBytes = 64 * 1024;
	private const int k_ClockSkewSeconds = 30;
	private const string k_CorsPolicy = "spa";

	public static void Main(string[] i_Args)
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(i_Args);

		configureServices(builder);

		WebApplication app = builder.Build();

		configurePipeline(app);

		// Fail at startup, not on the first request, if the container has no time-zone data.
		ZurichCalendar.StartOfDayUtc(new DateOnly(2026, 1, 1));
		app.Run();
	}

	private static void configureServices(WebApplicationBuilder i_Builder)
	{
		IConfiguration configuration = i_Builder.Configuration;
		JwtOptions jwtOptions = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
		SecurityOptions securityOptions = configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();
		RateLimitOptions rateLimitOptions = configuration.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();
		WorkRules workRules = configuration.GetSection("WorkRules").Get<WorkRules>() ?? new WorkRules();
		AuthRules authRules = configuration.GetSection("Auth").Get<AuthRules>() ?? new AuthRules();
		IServiceCollection services = i_Builder.Services;

		i_Builder.Logging.ClearProviders();
		i_Builder.Logging.AddJsonConsole();
		i_Builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = k_MaxRequestBodyBytes;
		});

		services.AddSingleton(jwtOptions);
		services.AddSingleton(securityOptions);
		services.AddSingleton(rateLimitOptions);
		services.AddSingleton(workRules);
		services.AddSingleton(authRules);
		services.AddSingleton<JwtIssuer>();
		services.AddSingleton<AuthCookies>();
		services.AddSingleton<CsrfTokens>();
		services.AddInfrastructure(configuration);
		services.AddScoped<AuthService>();
		services.AddScoped<ShiftRecordingService>();
		services.AddScoped<ShiftReadService>();
		services.AddScoped<EmployeeService>();

		services
			.AddControllers()
			.AddJsonOptions(json => JsonDefaults.Apply(json.JsonSerializerOptions))
			.ConfigureApiBehaviorOptions(behavior => behavior.InvalidModelStateResponseFactory = invalidModelResponse);
		services.AddHealthChecks();
		services.AddResponseCompression(compression =>
		{
			compression.EnableForHttps = true;
			compression.Providers.Add<BrotliCompressionProvider>();
			compression.Providers.Add<GzipCompressionProvider>();
		});
		services.Configure<BrotliCompressionProviderOptions>(brotli => brotli.Level = CompressionLevel.Fastest);

		// Same-origin in normal use; this policy only names the one origin that may call with cookies.
		services.AddCors(cors => cors.AddPolicy(k_CorsPolicy, policy =>
		{
			if (!string.IsNullOrEmpty(securityOptions.AllowedOrigin))
			{
				policy.WithOrigins(securityOptions.AllowedOrigin)
					.WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Patch, HttpMethods.Delete, HttpMethods.Query)
					.WithHeaders("Content-Type", CsrfTokens.k_HeaderName, "Idempotency-Key")
					.AllowCredentials();
			}
		}));

		services
			.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(bearer => configureJwt(bearer, jwtOptions));
		services
			.AddAuthorizationBuilder()
			.AddPolicy(Policies.k_Manager, policy => policy.RequireClaim(ClaimNames.k_Role, nameof(eUserRole.Manager)));
	}

	private static void configureJwt(JwtBearerOptions i_Bearer, JwtOptions i_JwtOptions)
	{
		i_Bearer.MapInboundClaims = false;
		i_Bearer.TokenValidationParameters = new TokenValidationParameters
		{
			ValidIssuer = i_JwtOptions.Issuer,
			ValidAudience = i_JwtOptions.Issuer,
			IssuerSigningKey = JwtIssuer.CreateKey(i_JwtOptions),
			ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
			ClockSkew = TimeSpan.FromSeconds(k_ClockSkewSeconds),
			NameClaimType = ClaimNames.k_Username,
			RoleClaimType = ClaimNames.k_Role,
		};
		i_Bearer.Events = new JwtBearerEvents
		{
			// The token lives in an HttpOnly cookie, not in an Authorization header.
			OnMessageReceived = context =>
			{
				context.Token = context.Request.Cookies[AuthCookies.k_Jwt];

				return Task.CompletedTask;
			},

			// A valid signature is not enough: the session named by "sid" must still exist in Redis.
			OnTokenValidated = async context =>
			{
				ISessionStore sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
				string? sessionClaim = context.Principal?.FindFirstValue(ClaimNames.k_SessionId);
				string? sessionCookie = context.Request.Cookies[AuthCookies.k_SessionId];
				bool hasUserId = int.TryParse(context.Principal?.FindFirstValue(ClaimNames.k_UserId), out int userId);
				bool isLive = hasUserId
					&& !string.IsNullOrEmpty(sessionClaim)
					&& sessionClaim == sessionCookie
					&& await sessions.IsActiveAsync(sessionClaim, userId);

				if (!isLive)
				{
					context.Fail("The session is not active");
				}
			},
			OnChallenge = async context =>
			{
				context.HandleResponse();
				await ProblemWriter.WriteAsync(context.HttpContext, Problems.Unauthenticated());
			},
			OnForbidden = async context =>
			{
				ILogger logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Security");

				logger.LogWarning(
					"security_event=forbidden user={UserId} method={Method} path={Path}",
					context.HttpContext.User.FindFirstValue(ClaimNames.k_UserId),
					context.Request.Method,
					context.Request.Path);
				await ProblemWriter.WriteAsync(context.HttpContext, Problems.Forbidden());
			},
		};
	}

	private static void configurePipeline(WebApplication i_App)
	{
		ForwardedHeadersOptions forwardedHeaders = new ForwardedHeadersOptions
		{
			ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
			ForwardLimit = 1,
		};

		// The backend is reachable only from the gateway on the internal Docker network,
		// so the single proxy hop in front of it is trusted.
		forwardedHeaders.KnownIPNetworks.Clear();
		forwardedHeaders.KnownProxies.Clear();

		i_App.UseForwardedHeaders(forwardedHeaders);
		i_App.UseMiddleware<SecurityHeadersMiddleware>();
		i_App.UseMiddleware<ExceptionMiddleware>();
		i_App.UseResponseCompression();
		i_App.UseRouting();
		i_App.UseCors(k_CorsPolicy);
		i_App.UseMiddleware<CsrfMiddleware>();
		i_App.UseAuthentication();
		i_App.UseMiddleware<RateLimitMiddleware>();
		i_App.UseAuthorization();
		i_App.MapControllers();
		i_App.MapHealthChecks("/health/live");
		i_App.MapHealthChecks("/health/ready");
	}

	/// <summary>Reports a body that cannot be bound (bad JSON, wrong types) in the same shape as every other error.</summary>
	private static IActionResult invalidModelResponse(ActionContext i_Context)
	{
		Dictionary<string, string[]> errors = i_Context.ModelState
			.Where(entry => entry.Value != null && entry.Value.Errors.Count > 0)
			.ToDictionary(entry => entry.Key, entry => new[] { "The value is not valid" });

		throw Problems.Validation(errors);
	}
}
