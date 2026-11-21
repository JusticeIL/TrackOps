using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrackOps.Api.Auth;
using TrackOps.Api.Middleware;
using TrackOps.Application;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Application.Services;
using TrackOps.Domain;

namespace TrackOps.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
	private readonly AuthService r_AuthService;
	private readonly JwtIssuer r_JwtIssuer;
	private readonly AuthCookies r_Cookies;
	private readonly CsrfTokens r_CsrfTokens;
	private readonly ILogger<AuthController> r_Logger;

	public AuthController(AuthService i_AuthService, JwtIssuer i_JwtIssuer, AuthCookies i_Cookies, CsrfTokens i_CsrfTokens, ILogger<AuthController> i_Logger)
	{
		r_AuthService = i_AuthService;
		r_JwtIssuer = i_JwtIssuer;
		r_Cookies = i_Cookies;
		r_CsrfTokens = i_CsrfTokens;
		r_Logger = i_Logger;
	}

	[HttpGet("csrf")]
	[AllowAnonymous]
	public IActionResult GetCsrfToken()
	{
		string token = r_CsrfTokens.Create(Request.Cookies[AuthCookies.k_SessionId]);

		r_Cookies.Set(Response, AuthCookies.k_Csrf, token);

		return Ok(new { token });
	}

	[HttpPost("session")]
	[AllowAnonymous]
	[RateLimit(eRateLimitBucket.SignIn)]
	public async Task<IActionResult> SignIn([FromBody] SignInRequest i_Request, CancellationToken i_CancellationToken)
	{
		SignInOutcome result;

		try
		{
			result = await r_AuthService.SignInAsync(i_Request, Request.Cookies[AuthCookies.k_SessionId], i_CancellationToken);
		}
		catch (AppException)
		{
			r_Logger.LogWarning("security_event=sign_in_rejected ip={Ip}", HttpContext.Connection.RemoteIpAddress);
			throw;
		}

		r_Cookies.Set(Response, AuthCookies.k_Jwt, r_JwtIssuer.Issue(result.User, result.SessionId));
		r_Cookies.Set(Response, AuthCookies.k_SessionId, result.SessionId);
		r_Cookies.Clear(Response, AuthCookies.k_Csrf);
		r_Logger.LogInformation("security_event=sign_in user={UserId} role={Role}", result.User.Id, result.User.Role);

		return Ok(result.User);
	}

	[HttpGet("session")]
	[Authorize]
	public IActionResult GetSession()
	{
		return Ok(ShiftMapper.ToDto(User.GetCurrentUser()));
	}

	[HttpDelete("session")]
	[Authorize]
	public async Task<IActionResult> SignOutSession()
	{
		string? sessionId = Request.Cookies[AuthCookies.k_SessionId];

		if (!string.IsNullOrEmpty(sessionId))
		{
			await r_AuthService.SignOutAsync(sessionId);
		}

		r_Cookies.Clear(Response, AuthCookies.k_Jwt);
		r_Cookies.Clear(Response, AuthCookies.k_SessionId);
		r_Cookies.Clear(Response, AuthCookies.k_Csrf);

		return NoContent();
	}
}

[ApiController]
[Route("api/v1/time")]
[Authorize]
public class TimeController : ControllerBase
{
	private readonly IDisplayClock r_DisplayClock;

	public TimeController(IDisplayClock i_DisplayClock)
	{
		r_DisplayClock = i_DisplayClock;
	}

	/// <summary>Seeds the browser's display clock. It is not used for recording; recording asks the time API itself.</summary>
	[HttpGet]
	public async Task<IActionResult> GetTime(CancellationToken i_CancellationToken)
	{
		TimeReading reading = await r_DisplayClock.GetUtcNowAsync(i_CancellationToken);

		return Ok(new TimeDto
		{
			Utc = reading.Utc,
			Zone = ZurichCalendar.k_ZoneId,
			OffsetSeconds = ZurichCalendar.UtcOffsetSecondsAt(reading.Utc),
		});
	}
}
