using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TrackOps.Application;
using TrackOps.Application.Abstractions;
using TrackOps.Application.Contracts;
using TrackOps.Domain.Entities;

namespace TrackOps.Api.Auth;

public class JwtOptions
{
	public const int k_MinKeyLength = 32;

	public string Key { get; set; } = string.Empty;

	public string Issuer { get; set; } = "trackops";

	public int LifetimeSeconds { get; set; } = 86400;
}

public class SecurityOptions
{
	/// <summary>On by default: cookies get the Secure flag and responses get HSTS. Off only for local plain HTTP.</summary>
	public bool RequireHttps { get; set; } = true;

	public string CsrfKey { get; set; } = string.Empty;

	public string AllowedOrigin { get; set; } = string.Empty;

	public string ReplicaName { get; set; } = string.Empty;
}

public static class ClaimNames
{
	public const string k_UserId = "sub";
	public const string k_Username = "username";
	public const string k_DisplayName = "name";
	public const string k_Role = "role";
	public const string k_SessionId = "sid";
}

public static class Policies
{
	public const string k_Manager = "Manager";
}

/// <summary>Issues the signed JWT that travels in an HttpOnly cookie. The "sid" claim ties it to a Redis session.</summary>
public class JwtIssuer
{
	private readonly JwtOptions r_Options;
	private readonly ISystemClock r_Clock;

	public JwtIssuer(JwtOptions i_Options, ISystemClock i_Clock)
	{
		r_Options = i_Options;
		r_Clock = i_Clock;
	}

	public static SymmetricSecurityKey CreateKey(JwtOptions i_Options)
	{
		if (i_Options.Key.Length < JwtOptions.k_MinKeyLength)
		{
			throw new InvalidOperationException($"Jwt:Key must be at least {JwtOptions.k_MinKeyLength} characters. Set it through the environment.");
		}

		return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(i_Options.Key));
	}

	public string Issue(UserDto i_User, string i_SessionId)
	{
		DateTime issuedAt = r_Clock.UtcNow;
		SecurityTokenDescriptor descriptor = new SecurityTokenDescriptor
		{
			Issuer = r_Options.Issuer,
			Audience = r_Options.Issuer,
			IssuedAt = issuedAt,
			NotBefore = issuedAt,
			Expires = issuedAt.AddSeconds(r_Options.LifetimeSeconds),
			SigningCredentials = new SigningCredentials(CreateKey(r_Options), SecurityAlgorithms.HmacSha256),
			Claims = new Dictionary<string, object>
			{
				[ClaimNames.k_UserId] = i_User.Id.ToString(),
				[ClaimNames.k_Username] = i_User.Username,
				[ClaimNames.k_DisplayName] = i_User.DisplayName,
				[ClaimNames.k_Role] = i_User.Role.ToString(),
				[ClaimNames.k_SessionId] = i_SessionId,
			},
		};

		return new JsonWebTokenHandler().CreateToken(descriptor);
	}
}

/// <summary>Writes and clears the auth cookies. Both are HttpOnly and SameSite=Strict; Secure follows the HTTPS switch.</summary>
public class AuthCookies
{
	public const string k_Jwt = "trackops_jwt";
	public const string k_SessionId = "trackops_sid";
	public const string k_Csrf = "trackops_csrf";
	private const string k_Path = "/api";
	private readonly SecurityOptions r_Security;
	private readonly JwtOptions r_Jwt;

	public AuthCookies(SecurityOptions i_Security, JwtOptions i_Jwt)
	{
		r_Security = i_Security;
		r_Jwt = i_Jwt;
	}

	public void Set(HttpResponse i_Response, string i_Name, string i_Value)
	{
		i_Response.Cookies.Append(i_Name, i_Value, createOptions(TimeSpan.FromSeconds(r_Jwt.LifetimeSeconds)));
	}

	public void Clear(HttpResponse i_Response, string i_Name)
	{
		i_Response.Cookies.Delete(i_Name, createOptions(null));
	}

	private CookieOptions createOptions(TimeSpan? i_MaxAge)
	{
		return new CookieOptions
		{
			HttpOnly = true,
			Secure = r_Security.RequireHttps,
			SameSite = SameSiteMode.Strict,
			Path = k_Path,
			MaxAge = i_MaxAge,
			IsEssential = true,
		};
	}
}

/// <summary>
/// CSRF defence for cookie authentication: a signed double-submit token.
/// The token is "nonce.signature", where the signature is an HMAC of the nonce and the current
/// session id. The server sets it as a cookie and returns it in the body; the SPA sends it back in
/// the X-CSRF-Token header. Another site can make the browser send cookies but cannot read the token
/// to put it in the header. Signing with a server key means an attacker cannot forge a matching pair,
/// and binding to the session id means a token from before sign-in stops working after it.
/// </summary>
public class CsrfTokens
{
	public const string k_HeaderName = "X-CSRF-Token";
	private const int k_NonceBytes = 16;
	private readonly byte[] r_Key;

	public CsrfTokens(SecurityOptions i_Security)
	{
		if (i_Security.CsrfKey.Length < JwtOptions.k_MinKeyLength)
		{
			throw new InvalidOperationException($"Security:CsrfKey must be at least {JwtOptions.k_MinKeyLength} characters. Set it through the environment.");
		}

		r_Key = Encoding.UTF8.GetBytes(i_Security.CsrfKey);
	}

	public string Create(string? i_SessionId)
	{
		string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(k_NonceBytes));

		return nonce + "." + sign(nonce, i_SessionId);
	}

	public bool IsValid(string? i_HeaderToken, string? i_CookieToken, string? i_SessionId)
	{
		bool isValid = false;

		if (!string.IsNullOrEmpty(i_HeaderToken) && !string.IsNullOrEmpty(i_CookieToken) && fixedTimeEquals(i_HeaderToken, i_CookieToken))
		{
			string[] parts = i_HeaderToken.Split('.');

			isValid = parts.Length == 2 && fixedTimeEquals(parts[1], sign(parts[0], i_SessionId));
		}

		return isValid;
	}

	private string sign(string i_Nonce, string? i_SessionId)
	{
		byte[] signature = HMACSHA256.HashData(r_Key, Encoding.UTF8.GetBytes(i_Nonce + "." + (i_SessionId ?? string.Empty)));

		return Convert.ToHexString(signature);
	}

	private static bool fixedTimeEquals(string i_Left, string i_Right)
	{
		return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(i_Left), Encoding.UTF8.GetBytes(i_Right));
	}
}

public static class CurrentUserExtensions
{
	public static CurrentUser GetCurrentUser(this ClaimsPrincipal i_Principal)
	{
		string? userId = i_Principal.FindFirstValue(ClaimNames.k_UserId);

		if (userId == null)
		{
			throw Problems.Unauthenticated();
		}

		return new CurrentUser
		{
			Id = int.Parse(userId),
			Username = i_Principal.FindFirstValue(ClaimNames.k_Username) ?? string.Empty,
			DisplayName = i_Principal.FindFirstValue(ClaimNames.k_DisplayName) ?? string.Empty,
			Role = Enum.Parse<eUserRole>(i_Principal.FindFirstValue(ClaimNames.k_Role) ?? nameof(eUserRole.Employee)),
		};
	}
}
