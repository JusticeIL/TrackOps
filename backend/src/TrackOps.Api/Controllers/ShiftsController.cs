using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TrackOps.Api.Auth;
using TrackOps.Api.Http;
using TrackOps.Api.Middleware;
using TrackOps.Application;
using TrackOps.Application.Contracts;
using TrackOps.Application.Services;

namespace TrackOps.Api.Controllers;

[ApiController]
[Route("api/v1/shifts")]
[Authorize]
public class ShiftsController : ControllerBase
{
	private const string k_IdempotencyHeader = "Idempotency-Key";
	private const string k_ReplayedHeader = "Idempotency-Replayed";
	private const string k_JsonContentType = "application/json; charset=utf-8";
	private const int k_DefaultActivityLimit = 10;
	private readonly ShiftRecordingService r_Recording;
	private readonly ShiftReadService r_Reads;

	public ShiftsController(ShiftRecordingService i_Recording, ShiftReadService i_Reads)
	{
		r_Recording = i_Recording;
		r_Reads = i_Reads;
	}

	[HttpPost]
	[RateLimit(eRateLimitBucket.Recording)]
	public async Task<IActionResult> ClockIn([FromHeader(Name = k_IdempotencyHeader)] string? i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		RecordingResult result = await r_Recording.ClockInAsync(User.GetCurrentUser(), parseKey(i_IdempotencyKey), i_CancellationToken);

		if (!result.IsReplay)
		{
			Response.Headers.Location = $"/api/v1/shifts/{result.ShiftId}";
		}

		return recorded(result);
	}

	[HttpPost("current/clock-out")]
	[RateLimit(eRateLimitBucket.Recording)]
	public async Task<IActionResult> ClockOut(
		[FromHeader(Name = k_IdempotencyHeader)] string? i_IdempotencyKey,
		[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ClockOutRequest? i_Request,
		CancellationToken i_CancellationToken)
	{
		RecordingResult result = await r_Recording.ClockOutAsync(User.GetCurrentUser(), parseKey(i_IdempotencyKey), i_Request?.Note, i_CancellationToken);

		return recorded(result);
	}

	[HttpPost("current/breaks/current/start")]
	[RateLimit(eRateLimitBucket.Recording)]
	public async Task<IActionResult> StartBreak([FromHeader(Name = k_IdempotencyHeader)] string? i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		RecordingResult result = await r_Recording.StartBreakAsync(User.GetCurrentUser(), parseKey(i_IdempotencyKey), i_CancellationToken);

		return recorded(result);
	}

	[HttpPost("current/breaks/current/end")]
	[RateLimit(eRateLimitBucket.Recording)]
	public async Task<IActionResult> EndBreak([FromHeader(Name = k_IdempotencyHeader)] string? i_IdempotencyKey, CancellationToken i_CancellationToken)
	{
		RecordingResult result = await r_Recording.EndBreakAsync(User.GetCurrentUser(), parseKey(i_IdempotencyKey), i_CancellationToken);

		return recorded(result);
	}

	[HttpGet("current")]
	public async Task<IActionResult> GetCurrent(CancellationToken i_CancellationToken)
	{
		ShiftDto? shift = await r_Reads.GetCurrentAsync(User.GetCurrentUser().Id, i_CancellationToken);

		return shift == null ? NoContent() : Ok(shift);
	}

	[HttpGet("activity")]
	public async Task<IActionResult> GetActivity([FromQuery(Name = "limit")] int? i_Limit, CancellationToken i_CancellationToken)
	{
		return Ok(await r_Reads.GetActivityAsync(User.GetCurrentUser().Id, i_Limit ?? k_DefaultActivityLimit, i_CancellationToken));
	}

	[HttpGet("{shiftId:long}")]
	public async Task<IActionResult> GetById([FromRoute(Name = "shiftId")] long i_ShiftId, CancellationToken i_CancellationToken)
	{
		return Ok(await r_Reads.GetByIdAsync(User.GetCurrentUser().Id, i_ShiftId, i_CancellationToken));
	}

	/// <summary>HTTP QUERY (RFC 10008): a safe, idempotent read whose filter travels in the body.</summary>
	[HttpQuery]
	[RateLimit(eRateLimitBucket.Search)]
	public async Task<IActionResult> Search(
		[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ShiftQuery? i_Query,
		CancellationToken i_CancellationToken)
	{
		return Ok(await r_Reads.SearchAsync(User.GetCurrentUser().Id, i_Query ?? new ShiftQuery(), i_CancellationToken));
	}

	private static Guid parseKey(string? i_IdempotencyKey)
	{
		if (!Guid.TryParse(i_IdempotencyKey, out Guid key))
		{
			throw Problems.IdempotencyKeyRequired();
		}

		return key;
	}

	/// <summary>Returns the stored JSON as is, so a replay is byte-for-byte the original response.</summary>
	private IActionResult recorded(RecordingResult i_Result)
	{
		if (i_Result.IsReplay)
		{
			Response.Headers[k_ReplayedHeader] = "true";
		}

		return new ContentResult { StatusCode = i_Result.StatusCode, Content = i_Result.Json, ContentType = k_JsonContentType };
	}
}

[ApiController]
[Route("api/v1/calendar")]
[Authorize]
public class CalendarController : ControllerBase
{
	private readonly ShiftReadService r_Reads;

	public CalendarController(ShiftReadService i_Reads)
	{
		r_Reads = i_Reads;
	}

	[HttpGet("months/{month}")]
	public async Task<IActionResult> GetMonth([FromRoute(Name = "month")] string i_Month, CancellationToken i_CancellationToken)
	{
		return Ok(await r_Reads.GetMonthAsync(User.GetCurrentUser().Id, i_Month, i_CancellationToken));
	}

	[HttpGet("days/{date}")]
	public async Task<IActionResult> GetDay([FromRoute(Name = "date")] string i_Date, CancellationToken i_CancellationToken)
	{
		return Ok(await r_Reads.GetDayAsync(User.GetCurrentUser().Id, i_Date, i_CancellationToken));
	}
}

/// <summary>Everything here needs the manager role; the policy is on the class, so a new action cannot forget it.</summary>
[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = Policies.k_Manager)]
public class EmployeesController : ControllerBase
{
	private readonly EmployeeService r_Employees;

	public EmployeesController(EmployeeService i_Employees)
	{
		r_Employees = i_Employees;
	}

	[HttpQuery]
	[RateLimit(eRateLimitBucket.Search)]
	public async Task<IActionResult> SearchEmployees(
		[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EmployeeQuery? i_Query,
		CancellationToken i_CancellationToken)
	{
		return Ok(await r_Employees.SearchEmployeesAsync(i_Query ?? new EmployeeQuery(), i_CancellationToken));
	}

	[HttpQuery("shifts")]
	[RateLimit(eRateLimitBucket.Search)]
	public async Task<IActionResult> SearchShifts(
		[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ShiftQuery? i_Query,
		CancellationToken i_CancellationToken)
	{
		return Ok(await r_Employees.SearchShiftsAsync(i_Query ?? new ShiftQuery(), i_CancellationToken));
	}

	[HttpPatch("{userId:int}/shifts/{shiftId:long}")]
	public async Task<IActionResult> CorrectShift(
		[FromRoute(Name = "userId")] int i_UserId,
		[FromRoute(Name = "shiftId")] long i_ShiftId,
		[FromBody] CorrectShiftRequest i_Request,
		CancellationToken i_CancellationToken)
	{
		return Ok(await r_Employees.CorrectShiftAsync(User.GetCurrentUser(), i_UserId, i_ShiftId, i_Request, i_CancellationToken));
	}

	[HttpGet("{userId:int}/shifts/{shiftId:long}/audit")]
	public async Task<IActionResult> GetAudit(
		[FromRoute(Name = "userId")] int i_UserId,
		[FromRoute(Name = "shiftId")] long i_ShiftId,
		CancellationToken i_CancellationToken)
	{
		return Ok(await r_Employees.GetAuditAsync(i_UserId, i_ShiftId, i_CancellationToken));
	}
}
