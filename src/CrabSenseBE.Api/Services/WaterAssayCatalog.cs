using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Api.Services;

/// <summary>Versioned reference recipes, never executable pump instructions.</summary>
public static class WaterAssayCatalog
{
    public sealed record Step(string Code, string Label, decimal? Amount = null, string? Unit = null, int? Seconds = null);
    public sealed record Reference(string Version, string SourceUrl, string Status, string[] Levels, string? Unit);
    public sealed record Profile(string Id, string Analyte, string Kit, string ProductCode, string SourceUrl,
        string SourceStatus, string? AdditionalSourceUrl, int SampleMl, int ReactionSeconds, string View,
        Step[] Steps, Reference[] References, bool HardwareExecutable = false);
    private const string SeraOld = "https://www.sera.de/fileadmin/user_upload/manuals/sourcefiles/04410_sera_Nitrit-Test_2015-06_INT.pdf";
    private const string SeraNew = "https://www.sera.de/fileadmin/user_upload/manuals/sourcefiles/4001942044103-4001942560412-4001942560719-04410-GBI_Nitrite_Test_Farbkarte_INT.pdf";
    private const string Vasto = "https://manuals.plus/ae/3256812050042028";
    private const string Thuy = "https://thuyphuoc.com/dung-dich-kiem-tra-nh3-nh4-b1404";
    private const string ThuyDirections = "https://tecki.vn/p1.188708905.6944229731-dung-dich-kiem-tra-nong-do-amoniac-test-nh3-nh4-cho-ho-ca-canh-ho-thuy-sinh";
    private static Step S(string code,string label,decimal? amount=null,string? unit=null,int? seconds=null)=>new(code,label,amount,unit,seconds);
    public static Profile[] All() =>
    [
        new("sera-no2-04410-2015","NO2","Sera","04410",SeraOld,"manufacturer",null,5,300,"top",
            [S("rinse","Rửa lọ bằng nước mẫu"), S("sample","Cấp mẫu",5,"mL"),
             S("reagent1","Cấp thuốc thử 1",5,"drop"),S("mix1","Trộn đều"),
             S("reagent2","Cấp thuốc thử 2",5,"drop"),S("mix2","Trộn đều"),
             S("wait","Chờ phản ứng",seconds:300),S("capture","Chụp từ trên"),S("analyze","Phân tích ảnh"),S("cleanup","Thu gom chất thải và rửa lọ")],
            [new("sera-2015-06",SeraOld,"manufacturer_reference",["0","0.5","1","2","5"],"mg/L NO2"),
             new("sera-current-comparator",SeraNew,"manufacturer_reference_different_revision",["≤0.01","0.2","0.5","0.8","1","1.5","2","5"],"mg/L NO2")]),
        new("vastocean-no3-manual-v1","NO3","VASTOCEAN","unconfirmed",Vasto,"third_party_pending_verification",null,5,120,"side",
            [S("rinse","Rửa lọ hai lần bằng mẫu"),S("sample","Cấp mẫu",5,"mL"),
             S("reagent1","Cấp thuốc thử 1",7,"drop"),S("mix1","Trộn đều"),
             S("reagent2","Cấp thuốc thử 2",7,"drop"),S("mix2","Trộn đều"),
             S("powder","Cấp bột bằng muỗng đi kèm",1m/3m,"kit_scoop"),S("mix_powder","Lắc",seconds:10),
             S("reagent3","Cấp thuốc thử 3",7,"drop"),S("mix3","Trộn đều"),S("wait","Chờ phản ứng",seconds:120),
             S("capture","Chụp ngang với nền trắng"),S("analyze","Phân tích ảnh"),S("cleanup","Thu gom chất thải và rửa lọ")],
            [new("vastocean-no3-unverified",Vasto,"chart_not_verified",[],null)]),
        new("thuyphuoc-ammonia-manual-v1","NH3","Thủy Phước","NH3/NH4",Thuy,"retailer_directions_pending_verification",ThuyDirections,5,600,"side",
            [S("rinse","Rửa lọ bằng mẫu"),S("sample","Cấp mẫu",5,"mL"),
             S("reagent1","Cấp thuốc thử I",5,"drop"),S("mix1","Trộn đều"),
             S("reagent2","Cấp thuốc thử II",5,"drop"),S("mix2","Trộn đều"),S("wait","Chờ phản ứng",seconds:600),
             S("capture","Chụp ngang lọ"),S("analyze","Phân tích ảnh"),S("cleanup","Thu gom chất thải và rửa lọ")],
            [new("thuyphuoc-ammonia-unverified",Thuy,"chart_and_unit_not_verified",[],null)])
    ];
    public static Profile For(string analyte)=>All().Single(x=>x.Analyte==analyte);
    public static Profile Snapshot(WaterAssayChannel channel)=>JsonSerializer.Deserialize<Profile>(channel.ProfileJson,WaterBatchPlanning.Json)
        ??throw AppException.Conflict("Không đọc được quy trình đã lưu.");
    public static bool Compatible(WaterColorCalibration calibration)
    {
        var p=For(calibration.Analyte);
        var protocol=JsonSerializer.Deserialize<ColorProtocol>(calibration.ProtocolJson,WaterBatchPlanning.Json);
        return protocol is not null && protocol.SampleVolumeMl==p.SampleMl && protocol.ReactionSeconds==p.ReactionSeconds &&
            protocol.TimingToleranceSeconds>=5;
    }
    public static WaterAssayChannel New(Guid batchId,WaterColorCalibration calibration,DateTime now)
    {
        if(!Compatible(calibration))throw AppException.Conflict("Hiệu chuẩn không khớp thể tích/thời gian quy trình hoặc dung sai dưới 5 giây.");
        var p=For(calibration.Analyte);
        return new WaterAssayChannel { BatchId=batchId,CalibrationId=calibration.Id,Analyte=calibration.Analyte,
            ProfileId=p.Id,ProfileJson=JsonSerializer.Serialize(p,WaterBatchPlanning.Json),
            ProtocolJson=calibration.ProtocolJson,State="waiting",StepIndex=0,StepStartedAt=now,CreatedAt=now };
    }
}
