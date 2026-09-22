using CrabSenseBE.Application.Common;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Application;

public class MediaFolderPathTests
{
    [Fact]
    public void Join_FarmTree_AreaRowBoxCrab()
    {
        MediaFolderPath.Join("AREA-A01", "DAY-A01", "BOX-001", "CRAB-0001")
            .Should().Be("AREA-A01/DAY-A01/BOX-001/CRAB-0001");
        MediaFolderPath.Join("AREA-A01", MediaFolderPath.AreasLeaf)
            .Should().Be("AREA-A01/_khu");
        MediaFolderPath.Join(MediaFolderPath.InboundRoot, "LOT-20260922-001")
            .Should().Be("NhapHang/LOT-20260922-001");
        MediaFolderPath.Join("AREA-A01", "DAY-A01", "BOX-001", "CRAB-0001", MediaFolderPath.MoltFolder, "20260922")
            .Should().Be("AREA-A01/DAY-A01/BOX-001/CRAB-0001/LotXac/20260922");
    }

    [Fact]
    public void CrabLot_UsesInboundAndId()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        MediaFolderPath.Build("CrabLot", id).Should().Be($"NhapHang/{id:D}");
        MediaFolderPath.Build("crab-lots", id).Should().Be($"NhapHang/{id:D}");
    }

    [Fact]
    public void Crab_UsesTableAndId()
    {
        var id = Guid.NewGuid();
        MediaFolderPath.Build("crabs", id).Should().Be($"Crabs/{id:D}");
    }

    [Fact]
    public void MissingId_UsesPending()
    {
        MediaFolderPath.Build("CrabLots", null).Should().Be("NhapHang/_pending");
        MediaFolderPath.Build("Crabs", Guid.Empty).Should().Be("Crabs/_pending");
    }

    [Fact]
    public void GenericImage_GoesToMediaImage()
    {
        MediaFolderPath.Build(null, null, "image").Should().Be("Media/image");
        MediaFolderPath.Build("video", null).Should().Be("Media/video");
    }

    [Fact]
    public void PrefersBusinessCode()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        MediaFolderPath.Build("CrabLots", id, folderName: "LOT-20260922-001")
            .Should().Be("NhapHang/LOT-20260922-001");
        MediaFolderPath.Build("FarmingAreas", id, folderName: "AREA-A01")
            .Should().Be("FarmingAreas/AREA-A01");
        MediaFolderPath.Build("Crabs", id, folderName: "CRAB-0001")
            .Should().Be("Crabs/CRAB-0001");
        MediaFolderPath.Build("HarvestVouchers", id, folderName: "HV-20260922-001")
            .Should().Be("ThuHoach/HV-20260922-001");
        MediaFolderPath.Build("Devices", id, folderName: "ESP-01")
            .Should().Be("ThietBi/ESP-01");
        MediaFolderPath.Build("FrozenLots", id, folderName: "FZ-001")
            .Should().Be("DongLanh/FZ-001");
    }

    [Fact]
    public void OperationsAndHarvest_MapToTables()
    {
        var id = Guid.NewGuid();
        MediaFolderPath.Build("FarmOperation", id).Should().Be($"NhatKy/{id:D}");
        MediaFolderPath.Build("HarvestVouchers", id).Should().Be($"ThuHoach/{id:D}");
        MediaFolderPath.Build("farming-area", id).Should().Be($"FarmingAreas/{id:D}");
    }
}
