using System.Text.Json;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchReviewPolicyTests
{
    [Fact] public void ApprovalRequiresReferenceAndExactMeasurand()
    {
        var r=new WaterBenchResultController.ReviewRequest(Guid.NewGuid(),"approved","review",Guid.NewGuid(),1,"mg/L TAN-N","lab",true);
        WaterBenchReviewPolicy.Validate(r,"NH3");
        Assert.Throws<AppException>(()=>WaterBenchReviewPolicy.Validate(r with{ReferenceMediaId=null},"NH3"));
        Assert.Throws<AppException>(()=>WaterBenchReviewPolicy.Validate(r with{SameSampleConfirmed=false},"NH3"));
        Assert.Throws<AppException>(()=>WaterBenchReviewPolicy.Validate(r,"NO2"));
    }
    [Fact] public void AppendPreservesOriginalAndRejectsChangedReplay()
    {
        var id=Guid.NewGuid();var hash=new string('a',64);
        var value=JsonSerializer.SerializeToElement(new{requestId=id,requestSha256=hash,decision="approved"});
        const string original="{\"reading\": {\"x\":1}}";
        var next=WaterBenchReviewPolicy.Append(original,"review",value);
        Assert.StartsWith(original[..^1],next);
        Assert.NotNull(WaterBenchReviewPolicy.Replay(next,"review",id,hash));
        Assert.Throws<AppException>(()=>WaterBenchReviewPolicy.Replay(next,"review",Guid.NewGuid(),hash));
        Assert.Throws<AppException>(()=>WaterBenchReviewPolicy.Read(next.Replace("approved","rejected"),"review"));
    }
}
