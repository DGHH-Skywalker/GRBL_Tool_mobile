using System.Text;
using LaserMobile.Cam;
using LaserMobile.Domain;
using LaserMobile.Imaging.Skia;
using SkiaSharp;
using Xunit;
namespace LaserMobile.Core.Tests;
public sealed class ImagingTests
{
    private static readonly JobLimits Limits=new(40,40,1200,1000);
    private static readonly RasterSettings Settings=new(10,.5,300,100);
    [Fact] public void PngConvertsWithCroppingRotationAndAlphaComposition()
    {
        using var bitmap=new SKBitmap(4,2);using(var canvas=new SKCanvas(bitmap)){canvas.Clear(SKColors.Black);}
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        var result=ArtworkImporter.Convert(png.ToArray(),"sample.png",Settings,Limits,false,default,new(0,0,.5,1,1));
        Assert.True(result.EmitsLaser);Assert.Equal(10,result.MaxX,3);Assert.NotEmpty(result.Segments);
    }
    [Fact] public void SvgRasterAndOutlineBothProduceValidPrograms()
    {
        byte[] svg=Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect x="0" y="0" width="10" height="10" fill="black"/></svg>""");
        var raster=ArtworkImporter.Convert(svg,"sample.svg",Settings,Limits,false,default);
        var vector=ArtworkImporter.Convert(svg,"sample.svg",Settings,Limits,true,default);
        Assert.True(raster.EmitsLaser);Assert.True(vector.EmitsLaser);Assert.Equal(10,vector.MaxX,2);Assert.Equal(10,vector.MaxY,2);
    }
    [Theory]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    [InlineData("<svg><image href='https://example.com/a.png'/></svg>")]
    [InlineData("<svg><rect style='fill:url(https://example.com)'/></svg>")]
    public void ExternalResourcesAndScriptsRejected(string xml) => Assert.Throws<FormatException>(()=>ArtworkImporter.Convert(Encoding.UTF8.GetBytes(xml),"a.svg",Settings,Limits,false,default));
    [Fact] public void DoctypeAndUnsupportedOutlineSemanticsRejected()
    {
        Assert.Throws<System.Xml.XmlException>(()=>ArtworkImporter.Convert(Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><svg>&x;</svg>"),"a.svg",Settings,Limits,false,default));
        Assert.Throws<FormatException>(()=>ArtworkImporter.Convert(Encoding.UTF8.GetBytes("<svg><g transform='translate(1)'><rect width='2' height='2'/></g></svg>"),"a.svg",Settings,Limits,true,default));
    }
}
