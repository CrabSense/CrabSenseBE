namespace CrabSenseBE.Application.Options;

public class FcmOptions
{
    public const string SectionName = "Fcm";

    public bool Enabled { get; set; }
    // Legacy ServerKey is ignored; HTTP v1 requires OAuth2.
    public string? ServerKey { get; set; }
    public string? ProjectId { get; set; }
    public string? CredentialsJson { get; set; }
    public string? CredentialsPath { get; set; }
}
