using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain.Entities;

namespace TrackOps.Application.Services;

public class SignInOutcome
{
	public UserDto User { get; set; } = new UserDto();

	public string SessionId { get; set; } = string.Empty;
}

/// <summary>
/// Mock sign-in: any valid-looking username and password pass, and the username decides the role.
/// No password is stored or compared, so there is nothing to hash. Everything around it is real:
/// input validation, a server-side session that is rotated on sign-in and revoked on sign-out.
/// </summary>
public class AuthService
{
	public const string k_ManagerUsername = "admin";
	private const int k_MinPasswordLength = 8;
	private const int k_MaxPasswordLength = 128;
	private static readonly Regex sr_UsernamePattern = new Regex("^[a-zA-Z0-9._-]{3,32}$", RegexOptions.Compiled);
	private readonly IAppDbContext r_Db;
	private readonly ISessionStore r_Sessions;

	public AuthService(IAppDbContext i_Db, ISessionStore i_Sessions)
	{
		r_Db = i_Db;
		r_Sessions = i_Sessions;
	}

	public async Task<SignInOutcome> SignInAsync(SignInRequest i_Request, string? i_PreviousSessionId, CancellationToken i_CancellationToken)
	{
		string displayName = (i_Request.Username ?? string.Empty).Trim();
		string password = i_Request.Password ?? string.Empty;
		ValidationErrors errors = new ValidationErrors();

		if (!sr_UsernamePattern.IsMatch(displayName))
		{
			errors.Add("username", "username must be 3 to 32 characters: letters, digits, dot, underscore or dash");
		}

		if (password.Length < k_MinPasswordLength || password.Length > k_MaxPasswordLength)
		{
			errors.Add("password", $"password must be between {k_MinPasswordLength} and {k_MaxPasswordLength} characters");
		}

		errors.ThrowIfAny();

		User user = await findOrCreateUserAsync(displayName, i_CancellationToken);

		if (!string.IsNullOrEmpty(i_PreviousSessionId))
		{
			await r_Sessions.RevokeAsync(i_PreviousSessionId);
		}

		return new SignInOutcome { User = ShiftMapper.ToDto(user), SessionId = await r_Sessions.CreateAsync(user.Id) };
	}

	public Task SignOutAsync(string i_SessionId)
	{
		return r_Sessions.RevokeAsync(i_SessionId);
	}

	private async Task<User> findOrCreateUserAsync(string i_DisplayName, CancellationToken i_CancellationToken)
	{
		string username = i_DisplayName.ToLowerInvariant();
		User? user = await r_Db.Users.FirstOrDefaultAsync(candidate => candidate.Username == username, i_CancellationToken);

		if (user == null)
		{
			user = new User
			{
				Username = username,
				DisplayName = i_DisplayName,
				Role = username == k_ManagerUsername ? eUserRole.Manager : eUserRole.Employee,
			};
			r_Db.Users.Add(user);
			try
			{
				await r_Db.SaveChangesAsync(i_CancellationToken);
			}
			catch (DbUpdateException exception) when (r_Db.IsUniqueViolation(exception, DbNames.k_UsernameIndex))
			{
				// Two first sign-ins of the same name raced; the unique index kept one row.
				r_Db.ClearTracking();
				user = await r_Db.Users.FirstAsync(candidate => candidate.Username == username, i_CancellationToken);
			}
		}

		return user;
	}
}
