using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("CRABSENSE_INSPECT_CONNECTION")
    ?? throw new InvalidOperationException("Set CRABSENSE_INSPECT_CONNECTION.");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

await PrintAsync(
    connection,
    "FARMING AREAS",
    """
    SELECT "Id", "Code", "Name"
    FROM be."FarmingAreas"
    ORDER BY "CreatedAt" NULLS LAST
    LIMIT 10
    """);

await PrintAsync(
    connection,
    "FARMING ROWS",
    """
    SELECT "Id", "Code", "Name", "FarmingAreaId"
    FROM be."FarmingRows"
    ORDER BY "SortOrder", "Name"
    LIMIT 20
    """);

await PrintAsync(
    connection,
    "BOXES",
    """
    SELECT "Id", "Code", "FarmingRowId", "Status", "IsOccupied"
    FROM be."Boxes"
    ORDER BY "Code"
    LIMIT 20
    """);

await PrintAsync(
    connection,
    "CRAB LOTS",
    """
    SELECT "Id", "LotCode", "Name", "Status"
    FROM be."CrabLots"
    ORDER BY "CreatedAt" NULLS LAST
    LIMIT 10
    """);

await PrintAsync(
    connection,
    "CRABS",
    """
    SELECT "Id", "Code", "BoxId", "CrabLotId", "WeightGram"
    FROM be."Crabs"
    ORDER BY "CreatedAt" NULLS LAST
    LIMIT 20
    """);

static async Task PrintAsync(
    NpgsqlConnection connection,
    string title,
    string sql)
{
    Console.WriteLine($"\n=== {title} ===");
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var values = Enumerable.Range(0, reader.FieldCount)
            .Select(index => $"{reader.GetName(index)}={reader.GetValue(index)}");
        Console.WriteLine(string.Join(" | ", values));
    }
}
