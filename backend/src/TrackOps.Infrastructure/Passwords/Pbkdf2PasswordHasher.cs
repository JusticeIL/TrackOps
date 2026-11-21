using System.Globalization;
using System.Security.Cryptography;
using TrackOps.Application.Abstractions;

namespace TrackOps.Infrastructure.Passwords;

/// <summary>
/// PBKDF2 with HMAC-SHA256, from the framework itself (no extra package): a 16-byte random salt
/// per password, a 32-byte key, and a configurable iteration count (600,000 by default, OWASP's
/// figure for this algorithm). The stored text names everything needed to check it later:
/// "pbkdf2-sha256$iterations$salt$key". Verification always uses the stored count, so raising the
/// count later does not lock anyone out. Keys are compared in constant time.
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
	private const string k_Scheme = "pbkdf2-sha256";
	private const char k_Separator = '$';
	private const int k_Parts = 4;
	private const int k_SaltBytes = 16;
	private const int k_KeyBytes = 32;
	private const int k_MinIterations = 1000;
	private readonly int r_Iterations;

	public Pbkdf2PasswordHasher(AuthRules i_Rules)
	{
		if (i_Rules.PasswordIterations < k_MinIterations)
		{
			throw new InvalidOperationException($"Auth:PasswordIterations must be at least {k_MinIterations}.");
		}

		r_Iterations = i_Rules.PasswordIterations;
	}

	public string Hash(string i_Password)
	{
		byte[] salt = RandomNumberGenerator.GetBytes(k_SaltBytes);
		byte[] key = derive(i_Password, salt, r_Iterations, k_KeyBytes);

		return string.Join(
			k_Separator,
			k_Scheme,
			r_Iterations.ToString(CultureInfo.InvariantCulture),
			Convert.ToBase64String(salt),
			Convert.ToBase64String(key));
	}

	public bool Verify(string i_Password, string i_StoredHash)
	{
		string[] parts = i_StoredHash.Split(k_Separator);
		bool isMatch = false;

		if (parts.Length == k_Parts
			&& parts[0] == k_Scheme
			&& int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations)
			&& iterations >= k_MinIterations)
		{
			try
			{
				byte[] salt = Convert.FromBase64String(parts[2]);
				byte[] expected = Convert.FromBase64String(parts[3]);

				isMatch = CryptographicOperations.FixedTimeEquals(derive(i_Password, salt, iterations, expected.Length), expected);
			}
			catch (FormatException)
			{
				// A damaged stored hash matches nothing.
				isMatch = false;
			}
		}

		return isMatch;
	}

	private static byte[] derive(string i_Password, byte[] i_Salt, int i_Iterations, int i_Length)
	{
		return Rfc2898DeriveBytes.Pbkdf2(i_Password, i_Salt, i_Iterations, HashAlgorithmName.SHA256, i_Length);
	}
}
