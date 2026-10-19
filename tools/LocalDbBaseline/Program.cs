using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("CRABSENSE_BASELINE_CONNECTION")
    ?? throw new InvalidOperationException(
        "Set CRABSENSE_BASELINE_CONNECTION before running this local-only utility.");

var migrations = new[]
{
    "20260905140000_FarmingRowKhuFields",
    "20260905170000_CrabOwnerProfile",
    "20260905180000_CrabCarapaceLength",
    "20260905190000_CrabLotInbound",
    "20260905200000_CrabLotWorkflowStatus"
};

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync();

foreach (var migration in migrations)
{
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO be."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ($1, '8.0.0')
        ON CONFLICT ("MigrationId") DO NOTHING;
        """,
        connection,
        transaction);
    command.Parameters.AddWithValue(migration);
    await command.ExecuteNonQueryAsync();
}

await transaction.CommitAsync();
Console.WriteLine($"Baselined {migrations.Length} local migrations.");
