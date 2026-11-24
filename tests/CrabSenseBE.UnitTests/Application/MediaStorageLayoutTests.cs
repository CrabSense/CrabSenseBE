using CrabSenseBE.Application.Common;

namespace CrabSenseBE.UnitTests.Application;

public class MediaStorageLayoutTests
{
    [Theory]
    [InlineData("NhapHang/LO-001", "01_NhapHang/LO-001")]
    [InlineData("AREA-01/ROW-01/BOX-01/CRAB-01", "02_NuoiCua/AREA-01/ROW-01/BOX-01/CRAB-01")]
    [InlineData("AREA-01/ROW-01/ThietBi/CAM-01", "07_ThietBi/AREA-01/ROW-01/CAM-01")]
    [InlineData("AREA-01/ThuHoach/HV-01", "05_ThuHoach/AREA-01/HV-01")]
    [InlineData("DongLanh/FZ-01", "06_KhoDongLanh/FZ-01")]
    [InlineData("Users/U-01", "08_NguoiDung/U-01")]
    [InlineData("Media/log", "10_HeThong/Logs")]
    [InlineData("Media/image", "99_TamThoi/image")]
    [InlineData("03_GhiHinh/AREA-01/CAM-01", "03_GhiHinh/AREA-01/CAM-01")]
    [InlineData("02_NuoiCua/../BOX-01", "02_NuoiCua/_pending/BOX-01")]
    public void CanonicalizesExistingContracts(string input,string expected)
        => Assert.Equal(expected, MediaStorageLayout.Canonicalize(input));

    [Theory]
    [InlineData("Scheduled","DinhKy")]
    [InlineData("Continuous","DinhKy")]
    [InlineData("Manual","ThuCong")]
    [InlineData("Event","SuKien")]
    public void CameraPathUsesCaptureDateAndSource(string mode,string expected)
    {
        var time = new DateTime(2026,11,24,23,58,0,DateTimeKind.Utc);
        Assert.Equal($"03_GhiHinh/AREA-01/ROW-01/CAM-01/2026/11/24/{expected}",
            MediaStorageLayout.CameraRecording("AREA-01","ROW-01",Guid.NewGuid(),"CAM-01",time,mode));
    }

    [Theory]
    [InlineData("no2","NO2")]
    [InlineData("NO3","NO3")]
    [InlineData("nh3","NH3")]
    [InlineData("../unknown","_pending")]
    public void WaterBatchUsesSharedBatchAndChemical(string input,string expected)
    {
        var batch=Guid.NewGuid();
        var path=MediaStorageLayout.WaterRun("AREA-01",Guid.NewGuid(),batch,input,
            new DateTime(2026,11,24,0,0,0,DateTimeKind.Utc));
        Assert.Equal($"04_PhanTichNuoc/AREA-01/2026/11/24/{batch:D}/{expected}/Raw",path);
    }
}
