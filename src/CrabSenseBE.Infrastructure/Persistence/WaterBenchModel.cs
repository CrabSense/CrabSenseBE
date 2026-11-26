using CrabSenseBE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Infrastructure.Persistence;

internal static class WaterBenchModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<WaterBenchStation>(b =>
        {
            b.ToTable("WaterBenchStations",t =>
            {
                t.HasCheckConstraint("CK_WaterBenchStation_Config",
                    "\"ConfigurationVersion\" >= 0 AND \"GraceMinutes\" BETWEEN 1 AND 15 AND jsonb_typeof(\"DailyTimesJson\"::jsonb) = 'array' AND jsonb_typeof(\"TargetsJson\"::jsonb) = 'array'");
                t.HasCheckConstraint("CK_WaterBenchStation_Hardware",
                    "\"MasterHardwareId\" ~ '^[0-9a-f]{12}$' AND \"SlaveHardwareId\" ~ '^[0-9a-f]{12}$' AND \"MasterHardwareId\" <> \"SlaveHardwareId\"");
            });
            b.Property(x=>x.ConfigurationVersion).IsConcurrencyToken();
            b.Property(x=>x.DailyTimesJson).HasColumnType("text");
            b.Property(x=>x.TargetsJson).HasColumnType("text");
            b.Property(x=>x.SampleSource).HasMaxLength(32);
            b.Property(x=>x.MasterHardwareId).HasMaxLength(12);
            b.Property(x=>x.SlaveHardwareId).HasMaxLength(12);
            b.HasOne<FarmKiosk>().WithMany().HasForeignKey(x=>x.KioskId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.KioskId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
            b.HasIndex(x=>x.MasterHardwareId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
            b.HasIndex(x=>x.SlaveHardwareId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        });
        model.Entity<WaterBenchEvidence>(b =>
        {
            b.ToTable("WaterBenchEvidence",t =>
            {
                t.HasCheckConstraint("CK_WaterBenchEvidence_Identity",
                    "\"EventId\" <> '00000000-0000-0000-0000-000000000000' AND \"ConnectionId\" <> '00000000-0000-0000-0000-000000000000'");
                t.HasCheckConstraint("CK_WaterBenchEvidence_Correlation",
                    "(\"BootId\" IS NULL OR \"BootId\" ~ '^[0-9a-f]{16}$') AND (\"FirmwareBatchId\" IS NULL OR \"FirmwareBatchId\" ~ '^[0-9a-f]{16}$') AND (\"FirmwareSessionId\" IS NULL OR \"FirmwareSessionId\" ~ '^[0-9a-f]{16}(-[12])?$') AND (\"CaptureId\" IS NULL OR \"CaptureId\" BETWEEN 1 AND 65536) AND (\"Analyte\" IS NULL OR \"Analyte\" IN ('NO2','NO3','NH3'))");
                t.HasCheckConstraint("CK_WaterBenchEvidence_Payload",
                    "\"PayloadSha256\" ~ '^[0-9a-f]{64}$' AND octet_length(\"PayloadJson\") BETWEEN 2 AND 65536 AND jsonb_typeof(\"PayloadJson\"::jsonb) = 'object'");
                t.HasCheckConstraint("CK_WaterBenchEvidence_Kind",
                    "\"Kind\" IN ('inventory','connection_open','connection_closed','telemetry_observation','batch_start','batch_status','session','image_request','jpeg','sample_evidence','unassociated_sample_evidence','nh3_image_link','clean_status','operator_event','camera_error','clock_observation','command','command_outcome','schedule_slot')");
            });
            b.HasOne<WaterBenchStation>().WithMany().HasForeignKey(x=>x.StationId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x=>x.Kind).HasMaxLength(32);
            b.Property(x=>x.BootId).HasMaxLength(16);
            b.Property(x=>x.FirmwareBatchId).HasMaxLength(16);
            b.Property(x=>x.FirmwareSessionId).HasMaxLength(18);
            b.Property(x=>x.Analyte).HasMaxLength(3);
            b.Property(x=>x.PayloadSha256).HasMaxLength(64);
            b.Property(x=>x.PayloadJson).HasColumnType("text");
            b.HasIndex(x=>new{x.StationId,x.EventId}).IsUnique();
            b.HasIndex(x=>new{x.StationId,x.CreatedAt});
            b.HasIndex(x=>new{x.StationId,x.ConnectionId,x.BootId,x.FirmwareBatchId}).HasDatabaseName("IX_WaterBenchEvidence_Batch");
            b.HasIndex(x=>new{x.StationId,x.RequestId});
        });

        model.Entity<WaterBenchResultAssessment>(b =>
        {
            b.ToTable("WaterBenchResultAssessments",t =>
            {
                t.HasCheckConstraint("CK_WaterBenchAssessment_Status","(\"Status\" = 'needs_retest' AND \"CandidateValue\" IS NULL AND \"Unit\" IS NULL AND \"HoldoutError\" IS NULL AND \"MediaId\" IS NULL AND \"CalibrationId\" IS NULL AND \"ImageEvidenceId\" IS NULL) OR (\"Status\" = 'estimate_review_required' AND \"CandidateValue\" BETWEEN 0 AND 10000 AND \"CandidateValue\" IS NOT NULL AND \"Unit\" IS NOT NULL AND \"HoldoutError\" BETWEEN 0 AND 1000 AND \"HoldoutError\" IS NOT NULL AND \"MediaId\" IS NOT NULL AND \"CalibrationId\" IS NOT NULL AND \"ImageEvidenceId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_WaterBenchAssessment_Hash","\"RequestHash\" ~ '^[0-9a-f]{64}$' AND octet_length(\"EvidenceJson\") BETWEEN 2 AND 65536 AND jsonb_typeof(\"EvidenceJson\"::jsonb) = 'object'");
            });
            b.HasOne<WaterBenchEvidence>().WithMany().HasForeignKey(x=>x.SessionEvidenceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WaterBenchEvidence>().WithMany().HasForeignKey(x=>x.ImageEvidenceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MediaAsset>().WithMany().HasForeignKey(x=>x.MediaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WaterColorCalibration>().WithMany().HasForeignKey(x=>x.CalibrationId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SessionEvidenceId).IsUnique();
            b.HasIndex(x=>x.ImageEvidenceId).IsUnique();
            b.HasIndex(x=>x.MediaId).IsUnique();
            b.Property(x=>x.Status).HasMaxLength(32);
            b.Property(x=>x.RequestHash).HasMaxLength(64);
            b.Property(x=>x.Reason).HasMaxLength(500);
            b.Property(x=>x.Unit).HasMaxLength(32);
        });
    }
}
