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
/// Sign-in and sign-up are one step ("trust on first use"): the first sign-in with a username
/// creates that user and stores a hash of the password given then; every later sign-in must give
/// the same password. The user's shifts and requests stay with the username. Everyone is an
/// employee, unless a manager username is configured (AuthRules); it is not by default.
/// After too many wrong passwords a username is locked for a while (AuthRules), whatever is typed.
/// Around it: input validation, a server-side session rotated on sign-in and revoked on sign-out.
/// </summary>
public class AuthService
{
	private const int k_MinPasswordLength = 8;
	private const int k_MaxPasswordLength = 128;
	private static readonly Regex sr_UsernamePattern = new Regex("^[a-zA-Z0-9._-]{3,32}$", RegexOptions.Compiled);
	private readonly IAppDbContext r_Db;
	private readonly ISessionStore r_Sessions;
	private readonly AuthRules r_AuthRules;
	private readonly IPasswordHasher r_PasswordHasher;
	private readonly ISignInAttempts r_SignInAttempts;

	public AuthService(
		IAppDbContext i_Db,
		ISessionStore i_Sessions,
		AuthRules i_AuthRules,
		IPasswordHasher i_PasswordHasher,
		ISignInAttempts i_SignInAttempts)
	{
		r_Db = i_Db;
		r_Sessions = i_Sessions;
		r_AuthRules = i_AuthRules;
		r_PasswordHasher = i_PasswordHasher;
		r_SignInAttempts = i_SignInAttempts;
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

		User user = await authenticateAsync(displayName, password, i_CancellationToken);

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

	/// <summary>
	/// Returns the user whose password this is, creating the user on a first sign-in. Throws
	/// invalid_credentials otherwise. Who was first is decided by the database, not by this
	/// replica: a unique index for a new username, a conditional UPDATE for a user who has no
	/// password yet. The losing request is then checked against the winner's password.
	/// </summary>
	private async Task<User> authenticateAsync(string i_DisplayName, string i_Password, CancellationToken i_CancellationToken)
	{
		string username = i_DisplayName.ToLowerInvariant();
		eUserRole role = roleOf(username);
		string? newHash = null;
		User? user = await r_Db.Users.FirstOrDefaultAsync(candidate => candidate.Username == username, i_CancellationToken);

		if (user?.PasswordHash != null)
		{
			// Checked before the password, so a locked username learns nothing and costs no hashing.
			int secondsLocked = await r_SignInAttempts.SecondsLockedAsync(username, r_AuthRules.MaxFailedSignIns);

			if (secondsLocked > 0)
			{
				throw Problems.SignInLocked(secondsLocked);
			}
		}

		if (user == null)
		{
			newHash = r_PasswordHasher.Hash(i_Password);
			user = await registerAsync(username, i_DisplayName, role, newHash, i_CancellationToken);
		}
		else if (user.PasswordHash == null)
		{
			newHash = r_PasswordHasher.Hash(i_Password);
			user = await claimPasswordAsync(user.Id, newHash, i_CancellationToken);
		}

		// The hash this request stored needs no second check; anyone else's must match.
		bool isOwnNewPassword = newHash != null && user.PasswordHash == newHash;
		bool isCorrect = isOwnNewPassword || (user.PasswordHash != null && r_PasswordHasher.Verify(i_Password, user.PasswordHash));

		if (!isCorrect)
		{
			await r_SignInAttempts.RecordFailureAsync(username, r_AuthRules.FailedSignInWindowSeconds);
			throw Problems.InvalidCredentials();
		}

		if (!isOwnNewPassword)
		{
			// A correct password wipes earlier mistakes.
			await r_SignInAttempts.ClearAsync(username);
		}

		if (user.Role != role)
		{
			// The manager setting changed since this user last signed in; the stored role follows it.
			user.Role = role;
			await r_Db.SaveChangesAsync(i_CancellationToken);
		}

		return user;
	}

	private async Task<User> registerAsync(string i_Username, string i_DisplayName, eUserRole i_Role, string i_PasswordHash, CancellationToken i_CancellationToken)
	{
		User user = new User
		{
			Username = i_Username,
			DisplayName = i_DisplayName,
			Role = i_Role,
			PasswordHash = i_PasswordHash,
		};

		r_Db.Users.Add(user);
		try
		{
			await r_Db.SaveChangesAsync(i_CancellationToken);
		}
		catch (DbUpdateException exception) when (r_Db.IsUniqueViolation(exception, DbNames.k_UsernameIndex))
		{
			// Two first sign-ins of the same name raced; the unique index kept the other one.
			r_Db.ClearTracking();
			user = await r_Db.Users.FirstAsync(candidate => candidate.Username == i_Username, i_CancellationToken);
		}

		return user;
	}

	/// <summary>Stores the password of a user who has none yet, unless another request did so first.</summary>
	private async Task<User> claimPasswordAsync(int i_UserId, string i_PasswordHash, CancellationToken i_CancellationToken)
	{
		await r_Db.Users
			.Where(candidate => candidate.Id == i_UserId && candidate.PasswordHash == null)
			.ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.PasswordHash, i_PasswordHash), i_CancellationToken);
		r_Db.ClearTracking();

		return await r_Db.Users.FirstAsync(candidate => candidate.Id == i_UserId, i_CancellationToken);
	}

	private eUserRole roleOf(string i_Username)
	{
		string managerUsername = r_AuthRules.ManagerUsername.Trim();
		bool isManager = managerUsername.Length > 0 && string.Equals(i_Username, managerUsername, StringComparison.OrdinalIgnoreCase);

		return isManager ? eUserRole.Manager : eUserRole.Employee;
	}
}
