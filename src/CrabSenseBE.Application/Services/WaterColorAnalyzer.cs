using System.Security.Cryptography;
using CrabSenseBE.Application.Common;
using SkiaSharp;

namespace CrabSenseBE.Application.Services;

public record ColorRoi(int X, int Y, int Width, int Height);
public record ColorGeometryPolicy(ColorRoi Anchor, int MaxShift = 4, double MinCorrelation = .92, double MinFocusRatio = .5);
public record ColorProtocol(int Width, int Height, ColorRoi Sample, ColorRoi Reference, string Channel,
    int ReactionSeconds, int TimingToleranceSeconds, decimal SampleVolumeMl, string Procedure,
    decimal MinSalinityPpt, decimal MaxSalinityPpt, ColorGeometryPolicy? GeometryPolicy = null);
public record ColorReading(double Feature, double ReferenceR, double ReferenceG, double ReferenceB, double Variation, string Sha256);
public record ColorPoint(decimal Concentration, Guid MediaId, ColorReading Reading);

/// <summary>Calibrated optical estimator, not a neural network or chemical ground truth.</summary>
public static class WaterColorAnalyzer
{
    public const string Version = "color-ratio-piecewise-v1";
    public static void ValidateProtocol(ColorProtocol p)
    {
        if (p.Width is < 100 or > 4096 || p.Height is < 100 or > 4096 || (long)p.Width*p.Height > 8_000_000 ||
            p.Channel is not ("R" or "G" or "B") || p.ReactionSeconds is < 1 or > 7200 ||
            p.TimingToleranceSeconds < 0 || p.TimingToleranceSeconds > Math.Min(120, p.ReactionSeconds/4) ||
            p.SampleVolumeMl <= 0 || p.SampleVolumeMl > 1000 || string.IsNullOrWhiteSpace(p.Procedure) || p.Procedure.Length > 2000 ||
            p.MinSalinityPpt < 0 || p.MaxSalinityPpt > 60 || p.MinSalinityPpt > p.MaxSalinityPpt)
            throw AppException.BadRequest("Thông số protocol không hợp lệ.");
        bool Valid(ColorRoi? a) => a is not null && a.X >= 0 && a.Y >= 0 && a.Width >= 16 && a.Height >= 16 &&
            (long)a.X+a.Width <= p.Width && (long)a.Y+a.Height <= p.Height;
        if (!Valid(p.Sample) || !Valid(p.Reference) ||
            (p.Sample.X < p.Reference.X+p.Reference.Width && p.Reference.X < p.Sample.X+p.Sample.Width &&
             p.Sample.Y < p.Reference.Y+p.Reference.Height && p.Reference.Y < p.Sample.Y+p.Sample.Height))
            throw AppException.BadRequest("ROI không hợp lệ hoặc chồng lên nhau.");
        if(p.GeometryPolicy is { } g)
        {
            var a=g.Anchor;var m=g.MaxShift;
            if(!Valid(a)||a.Width>64||a.Height>64||m is <1 or >8||!double.IsFinite(g.MinCorrelation)||g.MinCorrelation is <.85 or >.99||
               !double.IsFinite(g.MinFocusRatio)||g.MinFocusRatio is <.4 or >1||a.X<m||a.Y<m||(long)a.X+a.Width+m>p.Width||(long)a.Y+a.Height+m>p.Height||
               new[]{p.Sample,p.Reference}.Any(r=>a.X-m<r.X+r.Width&&r.X<a.X+a.Width+m&&a.Y-m<r.Y+r.Height&&r.Y<a.Y+a.Height+m))
                throw AppException.BadRequest("Mốc hình học phải 16–64 pixel, ngoài ROI màu và đủ vùng tìm kiếm.");
        }
    }

    public static ColorReading Read(byte[] bytes, ColorProtocol p)
    {
        ValidateProtocol(p);
        if (bytes.Length < 8 || bytes.Length > 5*1024*1024 ||
            !(bytes[0]==0xff && bytes[1]==0xd8 || bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})))
            throw AppException.BadRequest("Chỉ chấp nhận JPEG/PNG tối đa 5 MB.");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width != p.Width || codec.Info.Height != p.Height || codec.FrameCount > 1 ||
            codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
            throw AppException.BadRequest("Ảnh hỏng, sai kích thước hoặc không đúng hướng camera.");
        using var bitmap = new SKBitmap(new SKImageInfo(p.Width,p.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info,bitmap.GetPixels()) != SKCodecResult.Success)
            throw AppException.BadRequest("Không giải mã được ảnh mẫu.");
        var a = Region(bitmap,p.Sample); var b = Region(bitmap,p.Reference);
        int channel = p.Channel == "R" ? 0 : p.Channel == "G" ? 1 : 2;
        if (b.mean.Any(x=>x<40 || x>245) || a.mean[channel]<5 || a.mean[channel]>250 ||
            a.variation>0.15 || b.variation>0.10)
            throw AppException.BadRequest("Ảnh thiếu sáng, bão hòa hoặc vùng màu không đồng nhất; chụp lại.");
        return new ColorReading(a.mean[channel]/b.mean[channel],b.mean[0],b.mean[1],b.mean[2],a.variation,Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static (double[] mean,double variation) Region(SKBitmap img, ColorRoi roi)
    {
        var sum=new double[3]; var square=new double[3]; int n=0, clipped=0;
        for(int y=roi.Y;y<roi.Y+roi.Height;y+=2)
        for(int x=roi.X;x<roi.X+roi.Width;x+=2)
        {
            var px=img.GetPixel(x,y); if(px.Alpha!=255) throw AppException.BadRequest("ROI không được trong suốt.");
            if(px.Red<=2||px.Green<=2||px.Blue<=2||px.Red>=253||px.Green>=253||px.Blue>=253) clipped++;
            sum[0]+=px.Red;sum[1]+=px.Green;sum[2]+=px.Blue;
            square[0]+=px.Red*px.Red;square[1]+=px.Green*px.Green;square[2]+=px.Blue*px.Blue;n++;
        }
        if(clipped>n*0.05) throw AppException.BadRequest("ROI có quá nhiều pixel bão hòa.");
        var mean=sum.Select(v=>v/n).ToArray();
        var cv=Enumerable.Range(0,3).Max(i=>Math.Sqrt(Math.Max(0,square[i]/n-mean[i]*mean[i]))/Math.Max(1,mean[i]));
        return (mean,cv);
    }
    public static ColorPoint[] Fit(IEnumerable<ColorPoint> input)
    {
        var points=input.OrderBy(x=>x.Concentration).ToArray();
        if(points.Length is < 3 or > 12 || points.Any(x=>x.Concentration<0||x.Concentration>10000||!double.IsFinite(x.Reading.Feature)) ||
            points[0].Concentration!=0 || points.Select(x=>x.MediaId).Distinct().Count()!=points.Length ||
            points.Select(x=>x.Reading.Sha256).Distinct().Count()!=points.Length)
            throw AppException.BadRequest("Cần 3–12 ảnh chuẩn riêng biệt, gồm mẫu trắng nồng độ 0.");
        var sign=Math.Sign(points[^1].Reading.Feature-points[0].Reading.Feature);
        for(int i=1;i<points.Length;i++)
            if(points[i].Concentration<=points[i-1].Concentration ||
                sign*(points[i].Reading.Feature-points[i-1].Reading.Feature)<0.005)
                throw AppException.BadRequest("Đường màu không đơn điệu hoặc các mức quá gần.");
        foreach(var point in points) EnsureLighting(points[0].Reading,point.Reading);
        return points;
    }
    public static void EnsureLighting(ColorReading baseline, ColorReading sample)
    {
        if (Math.Abs(sample.ReferenceR-baseline.ReferenceR)>baseline.ReferenceR*0.10 ||
            Math.Abs(sample.ReferenceG-baseline.ReferenceG)>baseline.ReferenceG*0.10 ||
            Math.Abs(sample.ReferenceB-baseline.ReferenceB)>baseline.ReferenceB*0.10)
            throw AppException.BadRequest("Ánh sáng tham chiếu lệch quá 10%; kiểm tra LED/camera.");
    }
    public static decimal Predict(ColorPoint[] points,ColorReading reading)
    {
        EnsureLighting(points[0].Reading,reading);
        for(int i=1;i<points.Length;i++)
        {
            double a=points[i-1].Reading.Feature,b=points[i].Reading.Feature;
            if(reading.Feature>=Math.Min(a,b)&&reading.Feature<=Math.Max(a,b))
                return Math.Round(points[i-1].Concentration+(decimal)((reading.Feature-a)/(b-a))*(points[i].Concentration-points[i-1].Concentration),4);
        }
        throw AppException.BadRequest("Màu ngoài dải hiệu chuẩn; không ngoại suy nồng độ.");
    }
}
