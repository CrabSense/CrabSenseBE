using System.Diagnostics;
using System.Security.Cryptography;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class IsolatedRecoveryFactAttribute : FactAttribute
{
    public IsolatedRecoveryFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRABSENSE_RECOVERY_TEST_CLUSTER")))
            Skip = "Run via the isolated PostgreSQL migration runner.";
    }
}

public sealed class DatabaseBackupRecoveryPostgresTests
{
    [IsolatedRecoveryFact]
    public async Task RealBackupRestorePreservesMigratedDataAndRejectsTruncatedArchive()
    {
        var cluster = Path.GetFullPath(Environment.GetEnvironmentVariable("CRABSENSE_RECOVERY_TEST_CLUSTER")!);
        Assert.StartsWith("crabsense-migration-", Path.GetFileName(cluster));
        Assert.True(Directory.Exists(cluster));
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable("CRABSENSE_RECOVERY_TEST_PORT"), out var port));
        Assert.InRange(port, 1024, 65535);
        var bin = Environment.GetEnvironmentVariable("CRABSENSE_RECOVERY_TEST_PGBIN")!;
        Assert.True(Directory.Exists(bin));
        string Conn(string db) => new NpgsqlConnectionStringBuilder {
            Host="127.0.0.1",Port=port,Username="postgres",Database=db,Pooling=false
        }.ConnectionString;
        await using var source = new NpgsqlConnection(Conn("clean_migration"));
        await using var target = new NpgsqlConnection(Conn("recovery_target"));
        await source.OpenAsync(); await target.OpenAsync();
        async Task<string> Scalar(NpgsqlConnection c,string sql) {
            await using var command = new NpgsqlCommand(sql,c);
            return Convert.ToString(await command.ExecuteScalarAsync())!;
        }
        foreach (var c in new[]{source,target}) {
            Assert.Equal(cluster,Path.GetFullPath(await Scalar(c,"SHOW data_directory")));
            Assert.Equal(c.Database,await Scalar(c,"SELECT current_database()"));
        }
        Assert.NotEqual(source.Database,target.Database);
        async Task<Dictionary<string,string>> Snapshot(NpgsqlConnection c) {
            var tables=new List<string>();
            await using(var command=new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='be' ORDER BY tablename",c))
            await using(var reader=await command.ExecuteReaderAsync())
                while(await reader.ReadAsync())tables.Add(reader.GetString(0));
            Assert.NotEmpty(tables);
            Assert.Contains("__EFMigrationsHistory",tables);
            var result=new Dictionary<string,string>();
            foreach(var table in tables) {
                var quoted="\""+table.Replace("\"","\"\"")+"\"";
                result[table]=await Scalar(c,$"SELECT count(*)::text || ':' || md5(coalesce(string_agg(to_jsonb(t)::text, E'\\n' ORDER BY to_jsonb(t)::text),'')) FROM be.{quoted} t");
            }
            return result;
        }
        var before=await Snapshot(source);
        Assert.Equal(before.OrderBy(x=>x.Key), (await Snapshot(target)).OrderBy(x=>x.Key));
        Assert.True(int.Parse(before["__EFMigrationsHistory"].Split(':')[0]) > 0);
        var directory=Path.Combine(cluster,"isolated-recovery-archives");
        DatabaseBackupService Service(string db)=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["ConnectionStrings:DefaultConnection"]=Conn(db),["Backup:Directory"]=directory,
            ["Backup:PgBin"]=bin,["Backup:MinimumFreeBytes"]="16777216"
        }).Build(),NullLogger<DatabaseBackupService>.Instance);
        var backup=Service("clean_migration");
        var restore=Service("recovery_target");
        var watch=Stopwatch.StartNew();
        var archive=await backup.CreateAsync();
        var backupMs=watch.ElapsedMilliseconds;
        var file=backup.ResolvePath(archive.Info.FileName)!;
        Assert.True(File.Exists(file));
        Assert.True(new FileInfo(file).Length>=1024);
        if(!OperatingSystem.IsWindows()) {
            Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(file));
        }
        var bytes=await File.ReadAllBytesAsync(file);
        var digest=SHA256.HashData(bytes);
        var modified=bytes.ToArray(); modified[modified.Length/2]^=1;
        Assert.False(CryptographicOperations.FixedTimeEquals(digest,SHA256.HashData(modified)));
        // Independent checksum held in memory, not a production signature.
        var broken=Path.Combine(directory,"truncated.dump");
        await File.WriteAllBytesAsync(broken,bytes.Take(16).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>restore.RestoreAsync("truncated.dump"));
        Assert.False(restore.IsMaintenanceMode);
        Assert.Equal(before.OrderBy(x=>x.Key),(await Snapshot(target)).OrderBy(x=>x.Key));
        File.Delete(broken);
        // Change only the cloned target. The source must remain unchanged.
        var changed=await Scalar(target,"UPDATE be.\"FarmSettings\" SET \"Value\"='recovery-target-only' WHERE \"Key\"='scope-test' RETURNING \"Value\"");
        Assert.Equal("recovery-target-only",changed);
        Assert.NotEqual(before["FarmSettings"],(await Snapshot(target))["FarmSettings"]);
        var archiveBytes = await File.ReadAllBytesAsync(file);
        var archiveDigest = SHA256.HashData(archiveBytes);
        Assert.True(CryptographicOperations.FixedTimeEquals(digest, archiveDigest));
        watch.Restart();
        await restore.RestoreAsync(archive.Info.FileName);
        var recoveryMs=watch.ElapsedMilliseconds;
        Assert.False(restore.IsMaintenanceMode);
        Assert.Equal(before.OrderBy(x=>x.Key),(await Snapshot(target)).OrderBy(x=>x.Key));
        Assert.Equal(before.OrderBy(x=>x.Key),(await Snapshot(source)).OrderBy(x=>x.Key));
        Assert.Equal(2,backup.List().Count); // original + target safety backup
        // Select exactly one restored row and a parent absent from Sensors.
        // Updating all rows to the same SensorId would collide on the replay
        // unique index (23505) before reaching the foreign-key check (23503).
        var missingSensor = Guid.NewGuid();
        await using var parentCheck = new NpgsqlCommand("SELECT count(*) FROM be.\"Sensors\" WHERE \"Id\"=@id",target);
        parentCheck.Parameters.AddWithValue("id",missingSensor);
        Assert.Equal(0L, (long)(await parentCheck.ExecuteScalarAsync())!);
        var measurement = Guid.Parse(await Scalar(target,"SELECT \"Id\"::text FROM be.\"WaterMeasurements\" ORDER BY \"Id\" LIMIT 1"));
        await using var tx=await target.BeginTransactionAsync();
        await using var invalid=new NpgsqlCommand("UPDATE be.\"WaterMeasurements\" SET \"SensorId\"=@sensor WHERE \"Id\"=@measurement",target,tx);
        invalid.Parameters.AddWithValue("sensor",missingSensor);
        invalid.Parameters.AddWithValue("measurement",measurement);
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>invalid.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,ex.SqlState);
        Assert.Equal("WaterMeasurements",ex.TableName);
        Assert.Equal("be",ex.SchemaName);
        await tx.RollbackAsync();
        Assert.Equal(before.OrderBy(x=>x.Key),(await Snapshot(target)).OrderBy(x=>x.Key));
        Console.WriteLine($"RECOVERY_DRILL backup_ms={backupMs} restore_ms={recoveryMs} tables={before.Count} sha256_verified=true");
    }
}
