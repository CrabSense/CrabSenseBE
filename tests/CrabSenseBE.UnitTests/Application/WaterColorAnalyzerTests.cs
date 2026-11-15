using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using Xunit;
using SkiaSharp;
namespace CrabSenseBE.UnitTests.Application;
public class WaterColorAnalyzerTests
{
    private static ColorReading Read(double f,string hash,double light=150)=>new(f,light,light,light,0.01,hash);
    private static ColorPoint P(decimal v,double f,string hash)=>new(v,Guid.NewGuid(),Read(f,hash));
    [Fact] public void InterpolatesAndRejectsOutOfRange()
    {
        var points=WaterColorAnalyzer.Fit([P(0,1,"a"),P(1,.8,"b"),P(2,.6,"c")]);
        Assert.Equal(0.5m,WaterColorAnalyzer.Predict(points,Read(.9,"d")));
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.Predict(points,Read(.3,"e")));
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.Predict(points,Read(.9,"f",180)));
    }
    [Fact] public void RejectsNonMonotonicAndDuplicateImages()
    {
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.Fit([P(0,1,"a"),P(1,.5,"b"),P(2,.8,"c")]));
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.Fit([P(0,1,"a"),P(1,.8,"a"),P(2,.6,"c")]));
    }
    [Fact] public void ReadsActualPngAndRejectsClippedReference()
    {
        var p=new ColorProtocol(100,100,new(0,0,20,20),new(50,50,20,20),"G",60,5,5,"Kit instructions",10,20);
        using var image=new SKBitmap(100,100);
        image.Erase(new SKColor(150,150,150));
        for(int y=0;y<20;y++) for(int x=0;x<20;x++) image.SetPixel(x,y,new SKColor(150,75,150));
        using var first=SKImage.FromBitmap(image);
        using var png=first.Encode(SKEncodedImageFormat.Png,100);
        Assert.Equal(0.5,WaterColorAnalyzer.Read(png.ToArray(),p).Feature,5);
        for(int y=50;y<70;y++) for(int x=50;x<70;x++) image.SetPixel(x,y,new SKColor(255,255,255));
        using var second=SKImage.FromBitmap(image);
        using var bad=second.Encode(SKEncodedImageFormat.Png,100);
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.Read(bad.ToArray(),p));
    }
    [Fact] public void RequiresSeparateRoisAndValidImages()
    {
        var p=new ColorProtocol(100,100,new(0,0,20,20),new(50,50,20,20),"G",60,5,5,"Kit instructions",10,20);
        WaterColorAnalyzer.ValidateProtocol(p);
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.Read([1,2,3],p));
        Assert.Throws<AppException>(()=>WaterColorAnalyzer.ValidateProtocol(p with { Reference=p.Sample }));
    }
}
