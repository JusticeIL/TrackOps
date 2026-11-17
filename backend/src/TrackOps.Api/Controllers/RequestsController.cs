using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TrackOps.Api.Auth;
using TrackOps.Api.Middleware;
using TrackOps.Application.Contracts;
using TrackOps.Application.Services;

namespace TrackOps.Api.Controllers;

/// <summary>The signed-in user's own requests and reports. Nobody sees another user's.</summary>
[ApiController]
[Route("api/v1/requests")]
[Authorize]
public class RequestsController : ControllerBase
{
	private readonly EmployeeRequestService r_Requests;

	public RequestsController(EmployeeRequestService i_Requests)
	{
		r_Requests = i_Requests;
	}

	[HttpGet]
	public async Task<IActionResult> List(CancellationToken i_CancellationToken)
	{
		return Ok(await r_Requests.ListOwnAsync(User.GetCurrentUser().Id, i_CancellationToken));
	}

	[HttpPost]
	[RateLimit(eRateLimitBucket.Recording)]
	public async Task<IActionResult> Create(
		[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] NewEmployeeRequest? i_Request,
		CancellationToken i_CancellationToken)
	{
		EmployeeRequestDto created = await r_Requests.CreateAsync(User.GetCurrentUser().Id, i_Request ?? new NewEmployeeRequest(), i_CancellationToken);

		return StatusCode(StatusCodes.Status201Created, created);
	}
}
