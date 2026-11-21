using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TrackOps.Infrastructure.Persistence;

namespace TrackOps.Migrator;

/// <summary>
/// One-shot container that runs before the API replicas start:
/// waits for SQL Server, applies the EF Core migrations with the admin login, and creates the
/// least-privilege login the API uses (read/write on data, no schema rights).
/// Running it once, separately, means the two replicas never race to change the schema.
/// </summary>
public static class Program
{
	private const int k_MaxAttempts = 60;
	private const int k_RetryDelaySeconds = 2;
	private const int k_Failure = 1;
	private static readonly Regex sr_SafeIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,62}$");

	public static async Task<int> Main()
	{
		int exitCode = 0;

		try
		{
			string adminConnection = requireEnvironment("ConnectionStrings__Admin");
			string appLogin = requireEnvironment("Database__AppLogin");
			string appPassword = requireEnvironment("Database__AppPassword");
			DbContextOptionsBuilder<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>();

			options.UseSqlServer(adminConnection);
			await using AppDbContext db = new AppDbContext(options.Options);
			await waitForServerAsync(adminConnection);
			Console.WriteLine("Applying migrations...");
			await db.Database.MigrateAsync();
			Console.WriteLine("Creating the application login...");
			await createApplicationLoginAsync(adminConnection, appLogin, appPassword);
			Console.WriteLine("Database is ready.");
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"Migration failed: {exception.Message}");
			exitCode = k_Failure;
		}

		return exitCode;
	}

	/// <summary>
	/// Waits until SQL Server accepts connections and, if the database already exists, until it is
	/// ONLINE. After an unclean stop SQL Server answers before it has recovered the database; the
	/// migrations would then not see it and try to create it again ("database already exists").
	/// </summary>
	private static async Task waitForServerAsync(string i_AdminConnection)
	{
		SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(i_AdminConnection);
		string database = builder.InitialCatalog;
		bool isUp = false;

		builder.InitialCatalog = "master";
		for (int attempt = 1; attempt <= k_MaxAttempts && !isUp; attempt++)
		{
			try
			{
				await using SqlConnection connection = new SqlConnection(builder.ConnectionString);
				await using SqlCommand command = new SqlCommand("SELECT state_desc FROM sys.databases WHERE name = @name", connection);

				command.Parameters.AddWithValue("@name", database);
				await connection.OpenAsync();

				object? state = await command.ExecuteScalarAsync();

				isUp = state == null || (string)state == "ONLINE";
			}
			catch (SqlException)
			{
				isUp = false;
			}

			if (!isUp)
			{
				Console.WriteLine($"SQL Server or the {database} database is not ready yet (attempt {attempt}/{k_MaxAttempts})");
				await Task.Delay(TimeSpan.FromSeconds(k_RetryDelaySeconds));
			}
		}

		if (!isUp)
		{
			throw new InvalidOperationException("SQL Server did not become ready in time");
		}
	}

	// CREATE LOGIN and GRANT are DDL and cannot take SQL parameters. The login name is checked
	// against a strict pattern and the password is quote-escaped; both come from the environment,
	// never from a request.
	private static async Task createApplicationLoginAsync(string i_AdminConnection, string i_Login, string i_Password)
	{
		if (!sr_SafeIdentifier.IsMatch(i_Login))
		{
			throw new InvalidOperationException("Database__AppLogin must be a plain SQL identifier");
		}

		string password = i_Password.Replace("'", "''");
		string sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{i_Login}')
	CREATE LOGIN [{i_Login}] WITH PASSWORD = N'{password}';
ELSE
	ALTER LOGIN [{i_Login}] WITH PASSWORD = N'{password}';
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{i_Login}')
	CREATE USER [{i_Login}] FOR LOGIN [{i_Login}];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [{i_Login}];";

		await using SqlConnection connection = new SqlConnection(i_AdminConnection);
		await connection.OpenAsync();
		await using SqlCommand command = new SqlCommand(sql, connection);
		await command.ExecuteNonQueryAsync();
	}

	private static string requireEnvironment(string i_Name)
	{
		string? value = Environment.GetEnvironmentVariable(i_Name);

		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Missing required environment variable {i_Name}");
		}

		return value;
	}
}
