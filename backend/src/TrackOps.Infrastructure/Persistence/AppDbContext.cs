using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TrackOps.Application.Abstractions;
using TrackOps.Domain.Entities;

namespace TrackOps.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
	private const int k_UniqueIndexViolation = 2601;
	private const int k_UniqueConstraintViolation = 2627;
	private const int k_MillisecondPrecision = 3;

	public AppDbContext(DbContextOptions<AppDbContext> i_Options)
		: base(i_Options)
	{
	}

	public DbSet<User> Users => Set<User>();

	public DbSet<Shift> Shifts => Set<Shift>();

	public DbSet<Break> Breaks => Set<Break>();

	public DbSet<ShiftAudit> ShiftAudits => Set<ShiftAudit>();

	public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

	public void ClearTracking()
	{
		ChangeTracker.Clear();
	}

	public bool IsUniqueViolation(DbUpdateException i_Exception, string i_ConstraintName)
	{
		return i_Exception.InnerException is SqlException sqlException
			&& (sqlException.Number == k_UniqueIndexViolation || sqlException.Number == k_UniqueConstraintViolation)
			&& sqlException.Message.Contains(i_ConstraintName, StringComparison.Ordinal);
	}

	// UPDLOCK makes a second writer on the same shift wait for this transaction instead of
	// reading a state that is about to change. Values are passed as SQL parameters.
	public Task<Shift?> LockOpenShiftAsync(int i_UserId, CancellationToken i_CancellationToken)
	{
		return Shifts
			.FromSql($"SELECT * FROM Shifts WITH (UPDLOCK, ROWLOCK) WHERE UserId = {i_UserId} AND ClockOutUtc IS NULL")
			.Include(shift => shift.Breaks)
			.FirstOrDefaultAsync(i_CancellationToken);
	}

	public Task<Shift?> LockShiftAsync(long i_ShiftId, int i_UserId, CancellationToken i_CancellationToken)
	{
		return Shifts
			.FromSql($"SELECT * FROM Shifts WITH (UPDLOCK, ROWLOCK) WHERE Id = {i_ShiftId} AND UserId = {i_UserId}")
			.Include(shift => shift.Breaks)
			.FirstOrDefaultAsync(i_CancellationToken);
	}

	protected override void ConfigureConventions(ModelConfigurationBuilder i_Builder)
	{
		// Every DateTime column is a UTC instant stored as datetime2(3).
		i_Builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HavePrecision(k_MillisecondPrecision);
	}

	protected override void OnModelCreating(ModelBuilder i_Builder)
	{
		i_Builder.Entity<User>(user =>
		{
			user.Property(entity => entity.Username).HasMaxLength(32);
			user.Property(entity => entity.DisplayName).HasMaxLength(32);
			user.HasIndex(entity => entity.Username).IsUnique().HasDatabaseName(DbNames.k_UsernameIndex);
			user.ToTable(table => table.HasCheckConstraint("CK_Users_Role", "[Role] IN (0, 1)"));
		});

		i_Builder.Entity<Shift>(shift =>
		{
			shift.Property(entity => entity.TimeSource).HasMaxLength(32);
			shift.Property(entity => entity.RowVersion).IsRowVersion();
			shift.HasOne(entity => entity.User).WithMany().HasForeignKey(entity => entity.UserId).OnDelete(DeleteBehavior.Restrict);

			// The invariant "one open shift per user" lives here, so it holds for any number of replicas.
			shift.HasIndex(entity => entity.UserId).IsUnique().HasFilter("[ClockOutUtc] IS NULL").HasDatabaseName(DbNames.k_OneOpenShiftIndex);
			shift.HasIndex(entity => new { entity.UserId, entity.ClockInUtc }).IsDescending(false, true).HasDatabaseName("IX_Shifts_User_ClockIn");
			shift.HasIndex(entity => entity.ClockInUtc).IsDescending().HasDatabaseName("IX_Shifts_ClockIn");
			shift.ToTable(table =>
			{
				table.HasCheckConstraint("CK_Shifts_ClockOutAfterClockIn", "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]");
				table.HasCheckConstraint(
					"CK_Shifts_StatusMatchesClockOut",
					"([Status] = 0 AND [ClockOutUtc] IS NULL) OR ([Status] IN (1, 2) AND [ClockOutUtc] IS NOT NULL)");
			});
		});

		i_Builder.Entity<Break>(pause =>
		{
			pause.HasOne(entity => entity.Shift).WithMany(shift => shift.Breaks).HasForeignKey(entity => entity.ShiftId).OnDelete(DeleteBehavior.Cascade);
			// Two indexes on the same column need explicit names, or EF treats them as one.
			pause.HasIndex(entity => entity.ShiftId, DbNames.k_OneOpenBreakIndex).IsUnique().HasFilter("[EndUtc] IS NULL");
			pause.HasIndex(entity => entity.ShiftId, "IX_Breaks_Shift");
			pause.ToTable(table => table.HasCheckConstraint("CK_Breaks_EndAfterStart", "[EndUtc] IS NULL OR [EndUtc] > [StartUtc]"));
		});

		i_Builder.Entity<ShiftAudit>(audit =>
		{
			audit.Property(entity => entity.Reason).HasMaxLength(300);
			audit.HasOne<Shift>().WithMany().HasForeignKey(entity => entity.ShiftId).OnDelete(DeleteBehavior.Cascade);
			audit.HasOne(entity => entity.ChangedByUser).WithMany().HasForeignKey(entity => entity.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
			audit.HasIndex(entity => entity.ShiftId).HasDatabaseName("IX_ShiftAudits_Shift");
		});

		i_Builder.Entity<IdempotencyRecord>(record =>
		{
			record.HasKey(entity => new { entity.UserId, entity.Key }).HasName(DbNames.k_IdempotencyKey);
			record.Property(entity => entity.Endpoint).HasMaxLength(32);
			record.HasIndex(entity => entity.CreatedAtUtc).HasDatabaseName("IX_IdempotencyRecords_CreatedAt");
		});
	}
}

/// <summary>SQL Server returns datetime2 without a kind; this marks every value as UTC when it is read.</summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
	public UtcDateTimeConverter()
		: base(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
	{
	}
}

/// <summary>Lets "dotnet ef migrations add" build the model without a running database.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
	public AppDbContext CreateDbContext(string[] i_Args)
	{
		DbContextOptionsBuilder<AppDbContext> builder = new DbContextOptionsBuilder<AppDbContext>();

		builder.UseSqlServer("Server=design-time;Database=TrackOps;Integrated Security=false;User Id=none;Password=none");

		return new AppDbContext(builder.Options);
	}
}
