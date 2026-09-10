using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>
/// Reads S3 settings from AwsS3:* or env names UPLOAD_STORAGE / S3_BUCKET / S3_REGION / AWS_ACCESS_KEY_ID.
/// </summary>
internal static class S3Settings
{
    public static string? First(IConfiguration config, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = config[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
            value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    public static string? Bucket(IConfiguration config) =>
        First(config, "AwsS3:Bucket", "S3_BUCKET");

    public static string Region(IConfiguration config) =>
        First(config, "AwsS3:Region", "S3_REGION") ?? "ap-southeast-1";

    public static string? AccessKey(IConfiguration config) =>
        First(config, "AwsS3:AccessKey", "AWS_ACCESS_KEY_ID");

    public static string? SecretKey(IConfiguration config) =>
        First(config, "AwsS3:SecretKey", "AWS_SECRET_ACCESS_KEY");

    public static string MediaProvider(IConfiguration config)
    {
        var value = First(config, "UPLOAD_STORAGE", "MediaStorage:Provider") ?? "Local";
        return value.Equals("s3", StringComparison.OrdinalIgnoreCase) ? "S3" : value;
    }
}
