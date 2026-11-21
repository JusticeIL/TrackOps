using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Services;

/// <summary>
/// Requests and reports an employee files: a vacation, an expense, a sickness report and so on.
/// A request is stored as filed and stays "in progress"; there is no approval flow yet.
/// </summary>
public class EmployeeRequestService
{
	private const int k_MaxListed = 100;
	private const int k_MinTextLength = 3;
	private const int k_MaxTitleLength = 100;
	private const int k_MaxContentLength = 2000;
	private const int k_MinSickDays = 1;
	private const int k_MaxSickDays = 30;
	private static readonly Expression<Func<EmployeeRequest, EmployeeRequestDto>> sr_Projection = request => new EmployeeRequestDto
	{
		Id = request.Id,
		Type = request.Type,
		Status = request.Status,
		Title = request.Title,
		Content = request.Content,
		SickDays = request.SickDays,
		CreatedAtUtc = request.CreatedAtUtc,
	};

	private static readonly Func<EmployeeRequest, EmployeeRequestDto> sr_ToDto = sr_Projection.Compile();
	private readonly IAppDbContext r_Db;
	private readonly ITimeProvider r_TimeProvider;

	public EmployeeRequestService(IAppDbContext i_Db, ITimeProvider i_TimeProvider)
	{
		r_Db = i_Db;
		r_TimeProvider = i_TimeProvider;
	}

	/// <summary>The caller's own requests, newest first.</summary>
	public async Task<ItemsResult<EmployeeRequestDto>> ListOwnAsync(int i_UserId, CancellationToken i_CancellationToken)
	{
		List<EmployeeRequestDto> items = await r_Db.EmployeeRequests
			.AsNoTracking()
			.Where(request => request.UserId == i_UserId)
			.OrderByDescending(request => request.Id)
			.Take(k_MaxListed)
			.Select(sr_Projection)
			.ToListAsync(i_CancellationToken);

		return new ItemsResult<EmployeeRequestDto> { Items = items };
	}

	public async Task<EmployeeRequestDto> CreateAsync(int i_UserId, NewEmployeeRequest i_Request, CancellationToken i_CancellationToken)
	{
		const bool v_AllowLineBreaks = true;
		string? title = UserText.NormalizeLine(i_Request.Title);
		string? content = UserText.NormalizeLines(i_Request.Content);
		bool isKnownType = i_Request.Type.HasValue && Enum.IsDefined(i_Request.Type.Value);
		bool isSickness = i_Request.Type == eRequestType.Sickness;
		ValidationErrors errors = new ValidationErrors();

		if (!isKnownType)
		{
			errors.Add("type", "type must be one of the request types");
		}

		// Lengths are checked on the normalised text, which is exactly what is stored.
		if (title == null || UserText.HasForbiddenCharacters(title, !v_AllowLineBreaks))
		{
			errors.Add("title", "title must be one line without control characters or invisible formatting characters");
		}
		else if (title.Length < k_MinTextLength || title.Length > k_MaxTitleLength)
		{
			errors.Add("title", $"title must be between {k_MinTextLength} and {k_MaxTitleLength} characters");
		}

		// An ordinary request must say what is asked. A sickness report may be sent without an explanation.
		if (content == null || UserText.HasForbiddenCharacters(content, v_AllowLineBreaks))
		{
			errors.Add("content", "content must not contain control characters (other than line breaks and tabs) or invisible formatting characters");
		}
		else if (content.Length > k_MaxContentLength || (!isSickness && content.Length < k_MinTextLength))
		{
			errors.Add("content", $"content must be between {k_MinTextLength} and {k_MaxContentLength} characters");
		}

		if (isSickness && (i_Request.SickDays == null || i_Request.SickDays < k_MinSickDays || i_Request.SickDays > k_MaxSickDays))
		{
			errors.Add("sickDays", $"sickDays must be between {k_MinSickDays} and {k_MaxSickDays}");
		}

		if (!isSickness && i_Request.SickDays != null)
		{
			errors.Add("sickDays", "sickDays belongs to a sickness report only");
		}

		errors.ThrowIfAny();

		// The filing time comes from the external time source. If it cannot be read, nothing is stored.
		TimeReading reading = await r_TimeProvider.GetUtcNowAsync(i_CancellationToken);
		EmployeeRequest request = new EmployeeRequest
		{
			UserId = i_UserId,
			Type = i_Request.Type!.Value,
			Status = eRequestStatus.InProgress,
			Title = title!,
			Content = content!,
			SickDays = i_Request.SickDays,
			CreatedAtUtc = reading.Utc,
		};

		r_Db.EmployeeRequests.Add(request);
		await r_Db.SaveChangesAsync(i_CancellationToken);

		return sr_ToDto(request);
	}
}
