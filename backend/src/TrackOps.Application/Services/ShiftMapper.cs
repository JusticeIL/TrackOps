using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Services;

public static class ShiftMapper
{
	/// <summary>Selects only the columns the API returns, with the break, in one SQL query (no N+1).</summary>
	public static readonly Expression<Func<Shift, ShiftDto>> sr_Projection = shift => new ShiftDto
	{
		Id = shift.Id,
		UserId = shift.UserId,
		Username = shift.User!.Username,
		DisplayName = shift.User!.DisplayName,
		ClockInUtc = shift.ClockInUtc,
		ClockOutUtc = shift.ClockOutUtc,
		Status = shift.Status,
		IsCorrected = shift.IsCorrected,
		RowVersionBytes = shift.RowVersion,
		Break = shift.Break == null
			? null
			: new BreakDto { Id = shift.Break.Id, StartUtc = shift.Break.StartUtc, EndUtc = shift.Break.EndUtc },
	};

	public static ShiftDto FromEntity(Shift i_Shift, string i_Username, string i_DisplayName)
	{
		ShiftDto dto = new ShiftDto
		{
			Id = i_Shift.Id,
			UserId = i_Shift.UserId,
			Username = i_Username,
			DisplayName = i_DisplayName,
			ClockInUtc = i_Shift.ClockInUtc,
			ClockOutUtc = i_Shift.ClockOutUtc,
			Status = i_Shift.Status,
			IsCorrected = i_Shift.IsCorrected,
			RowVersionBytes = i_Shift.RowVersion,
			Break = i_Shift.Break == null
				? null
				: new BreakDto { Id = i_Shift.Break.Id, StartUtc = i_Shift.Break.StartUtc, EndUtc = i_Shift.Break.EndUtc },
		};

		return Complete(dto);
	}

	/// <summary>Fills the values that are derived, never stored: the row version text and all minute counts.</summary>
	public static ShiftDto Complete(ShiftDto i_Dto)
	{
		double breakSeconds = 0;
		BreakDto? pause = i_Dto.Break;

		i_Dto.RowVersion = Convert.ToBase64String(i_Dto.RowVersionBytes);
		if (pause != null && pause.EndUtc.HasValue)
		{
			pause.Minutes = WorkCalculator.MinutesBetween(pause.StartUtc, pause.EndUtc.Value);
			breakSeconds = (pause.EndUtc.Value - pause.StartUtc).TotalSeconds;
		}

		if (i_Dto.ClockOutUtc.HasValue)
		{
			double shiftSeconds = (i_Dto.ClockOutUtc.Value - i_Dto.ClockInUtc).TotalSeconds;

			i_Dto.ShiftMinutes = (int)(shiftSeconds / 60);
			i_Dto.BreakMinutes = (int)(breakSeconds / 60);
			i_Dto.WorkedMinutes = Math.Max(0, (int)((shiftSeconds - breakSeconds) / 60));
		}

		return i_Dto;
	}

	public static WorkSpan ToWorkSpan(ShiftDto i_Dto)
	{
		WorkSpan span = new WorkSpan { StartUtc = i_Dto.ClockInUtc, EndUtc = i_Dto.ClockOutUtc };

		if (i_Dto.Break != null)
		{
			span.Breaks.Add(new WorkSpan { StartUtc = i_Dto.Break.StartUtc, EndUtc = i_Dto.Break.EndUtc });
		}

		return span;
	}

	public static UserDto ToDto(User i_User)
	{
		return new UserDto { Id = i_User.Id, Username = i_User.Username, DisplayName = i_User.DisplayName, Role = i_User.Role };
	}

	public static UserDto ToDto(CurrentUser i_User)
	{
		return new UserDto { Id = i_User.Id, Username = i_User.Username, DisplayName = i_User.DisplayName, Role = i_User.Role };
	}
}

/// <summary>
/// Filtering and paging shared by "my history" and the manager's "all shifts".
/// Text search matches employee names, so it applies to the manager's list only.
/// </summary>
public static class ShiftSearch
{
	public const int k_MaxSearchLength = 100;
	public const string k_LikeEscape = "\\";
	private const int k_DefaultPageSize = 20;
	private const int k_MaxPageSize = 100;

	public static async Task<PagedResult<ShiftDto>> RunAsync(
		IQueryable<Shift> i_Source,
		ShiftQuery i_Query,
		bool i_SearchUserNames,
		CancellationToken i_CancellationToken)
	{
		int page = i_Query.Page ?? 1;
		int pageSize = i_Query.PageSize ?? k_DefaultPageSize;

		validate(i_Query, page, pageSize);

		IQueryable<Shift> filtered = applyFilters(i_Source.AsNoTracking(), i_Query, i_SearchUserNames);
		int totalItems = await filtered.CountAsync(i_CancellationToken);
		List<ShiftDto> items = await filtered
			.OrderByDescending(shift => shift.ClockInUtc)
			.ThenByDescending(shift => shift.Id)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.Select(ShiftMapper.sr_Projection)
			.ToListAsync(i_CancellationToken);

		items.ForEach(item => ShiftMapper.Complete(item));

		return new PagedResult<ShiftDto>
		{
			Items = items,
			Page = page,
			PageSize = pageSize,
			TotalItems = totalItems,
			TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
		};
	}

	/// <summary>Turns user text into a LIKE pattern where the user's own %, _ and [ match literally.</summary>
	public static string ToLikePattern(string i_Text, bool i_PrefixOnly)
	{
		string escaped = i_Text
			.Replace(k_LikeEscape, k_LikeEscape + k_LikeEscape)
			.Replace("%", k_LikeEscape + "%")
			.Replace("_", k_LikeEscape + "_")
			.Replace("[", k_LikeEscape + "[");

		return i_PrefixOnly ? escaped + "%" : "%" + escaped + "%";
	}

	private static void validate(ShiftQuery i_Query, int i_Page, int i_PageSize)
	{
		ValidationErrors errors = new ValidationErrors();

		if (i_Page < 1)
		{
			errors.Add("page", "page must be 1 or greater");
		}

		if (i_PageSize < 1 || i_PageSize > k_MaxPageSize)
		{
			errors.Add("pageSize", $"pageSize must be between 1 and {k_MaxPageSize}");
		}

		if (i_Query.Search != null && i_Query.Search.Length > k_MaxSearchLength)
		{
			errors.Add("search", $"search must be at most {k_MaxSearchLength} characters");
		}

		if (i_Query.From.HasValue && i_Query.To.HasValue && i_Query.From.Value > i_Query.To.Value)
		{
			errors.Add("from", "from must not be after to");
		}

		errors.ThrowIfAny();
	}

	private static IQueryable<Shift> applyFilters(IQueryable<Shift> i_Source, ShiftQuery i_Query, bool i_SearchUserNames)
	{
		IQueryable<Shift> query = i_Source;

		if (i_Query.UserId.HasValue)
		{
			int userId = i_Query.UserId.Value;

			query = query.Where(shift => shift.UserId == userId);
		}

		if (i_Query.From.HasValue)
		{
			DateTime fromUtc = ZurichCalendar.StartOfDayUtc(i_Query.From.Value);

			query = query.Where(shift => shift.ClockInUtc >= fromUtc);
		}

		if (i_Query.To.HasValue)
		{
			DateTime toUtc = ZurichCalendar.StartOfDayUtc(i_Query.To.Value.AddDays(1));

			query = query.Where(shift => shift.ClockInUtc < toUtc);
		}

		if (i_Query.Status != null && i_Query.Status.Count > 0)
		{
			List<eShiftStatus> statuses = i_Query.Status;

			query = query.Where(shift => statuses.Contains(shift.Status));
		}

		if (i_SearchUserNames && !string.IsNullOrWhiteSpace(i_Query.Search))
		{
			const bool v_PrefixOnly = true;
			string pattern = ToLikePattern(i_Query.Search.Trim(), !v_PrefixOnly);

			query = query.Where(shift => EF.Functions.Like(shift.User!.Username, pattern, k_LikeEscape)
				|| EF.Functions.Like(shift.User!.DisplayName, pattern, k_LikeEscape));
		}

		return query;
	}
}
