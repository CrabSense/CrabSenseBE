namespace CrabSenseBE.Application.Options;

public class FcmOptions
{
    public const string SectionName = "Fcm";

    public bool Enabled { get; set; }
    public string? ServerKey { get; set; }
}
