using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Cam;
using LaserMobile.Persistence;
using LaserMobile.Application;
using System.Text;
using System.Threading.Channels;
using Xunit;
namespace LaserMobile.Core.Tests;

public sealed class ProductionTests
{
    private static readonly JobLimits Limits = new(40, 40, 1200, 1000);
    [Fact] public void RelativeAndInchModesHaveCorrectBounds()
    {
        var p = GcodeProgram.Prepare("G20\nG90\nM5\nG1 X1 Y0 F10\nG91\nX-.5 Y.5\nM5", Limits);
        Assert.Equal(25.4, p.MaxX, 6); Assert.Equal(12.7, p.MaxY, 6);
        Assert.Equal(12.7, p.Segments[^1].X2, 6);
    }
    [Fact] public void StandardCoordinateHeaderIsAccepted()
    {
        var p=GcodeProgram.Prepare("G21 G90 G54 G17 G94\nM5\nG1 X1 Y1 F300\nM5",Limits);
        Assert.Equal(1,p.MaxX);Assert.Equal(1,p.MaxY);
    }
    [Fact] public void CanonicalReparseRetainsHash()
    {
        var p = GcodeProgram.Prepare("G21 (mm)\nG90\nM5\nM4 S100\nG1X1Y2F300;sample\nM5", Limits);
        Assert.Equal(p.Plan.Sha256, GcodeProgram.Prepare(string.Join('\n', p.Plan.Lines), Limits).Plan.Sha256);
        Assert.True(p.EmitsLaser);
    }
    [Theory]
    [InlineData("G2 X1 Y1 I1")][InlineData("G1 X50 Y0 F100")][InlineData("G1 X1 Y0 F1300")]
    [InlineData("M4 S1001")][InlineData("G1 X1 X2 F100")][InlineData("G1 XNaN F100")]
    [InlineData("G92 X0")][InlineData("G1 Z1 F100")][InlineData("G1 X1 F100 (!)")]
    [InlineData("G1 X1 F100 (abc")][InlineData("G90 G91")]
    public void UnsupportedOrUnsafeFilesRejected(string line) => Assert.Throws<FormatException>(() => GcodeProgram.Prepare("G21\nG90\nM5\n"+line+"\nM5", Limits));
    [Fact] public void MissingModalAndLaserInitializationRejected()
    {
        Assert.Throws<FormatException>(()=>GcodeProgram.Prepare("M5\nG1 X1 F100",Limits));
        Assert.Throws<FormatException>(()=>GcodeProgram.Prepare("G21\nG90\nG1 X1 F100",Limits));
    }
    [Fact] public void RasterWhiteIsOffBlackIsPowered()
    {
        var p = RasterCam.Generate([255,0], 2, 1, new(10,.1,300,100), Limits);
        Assert.Contains(p.Segments, x=> x.Power == 100); Assert.Contains(p.Segments, x=>x.Power==0 && !x.Rapid);
        Assert.Equal("M5",p.Plan.Lines[^1]);
    }
    [Fact] public void BidirectionalRasterUsesRequestedRows()
    {
        var p = RasterCam.Generate([0,0,0,0],2,2,new(10,.2,300,100),Limits);
        Assert.Equal(.2,p.MaxY,4);Assert.Equal(10,p.MaxX,4);
        Assert.Contains(p.Segments, x=>x.X2==0 && x.Y2==.2 && x.Power>0);
    }
    [Fact] public void FrameHasNoEmission()
    {
        var p=GcodeProgram.Prepare(SimulationSession.Sample,Limits);
        var frame=RasterCam.Frame(p,300,Limits); Assert.False(frame.EmitsLaser);Assert.All(frame.Segments,s=>Assert.Equal(0,s.Power));
    }
    [Fact] public void DiffusionProducesBinarySamplesAndIncludesFirstRow()
    {
        byte[] samples=[120,120,120,120]; Diffusion.Apply(samples,4,1);
        Assert.All(samples,x=>Assert.True(x is 0 or 255)); Assert.Contains((byte)255,samples);
    }
    [Fact] public void RasterCancellationAndBudgets()
    {
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RasterCam.Generate([0],1,1,new(1,.1,100,100),Limits,c.Token));
        Assert.Throws<ArgumentException>(()=>RasterCam.Generate([0],1,1,new(50,.1,100,100),Limits));
    }
    [Fact] public void ImmutableStoreDetectsTamper()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try
        {
            var store=new JobLibrary(dir);var p=GcodeProgram.Prepare(SimulationSession.Sample,Limits);
            var j=store.Commit("sample.nc",p.Plan);Assert.Equal(j.Hash,store.Read(j.Id).Hash);
            var path=Path.Combine(dir,j.Id+".json");File.WriteAllText(path,File.ReadAllText(path).Replace("F300","F301"));
            Assert.Throws<InvalidDataException>(()=>store.Read(j.Id));Assert.Empty(store.List());
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact] public void ResetCancellationWaitsForWelcome()
    {
        var s=new JobSender(DryRunJob.Parse(SimulationSession.Sample));s.Start(DateTimeOffset.UnixEpoch,true);s.DrainWrites();s.ConfirmedCancel();
        Assert.Equal(ExecutionState.Cancelling,s.State);Assert.Contains("\u0018",s.DrainWrites());
        s.Receive(new Welcome("Grbl 1.1h"),DateTimeOffset.UnixEpoch);Assert.Equal(ExecutionState.Cancelled,s.State);
    }
    [Fact] public async Task SessionReadsConfigurationAndStopsOnTransportFailure()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        try
        {
            var transport=new ScriptedTransport();var store=new ExecutionStore(Path.Combine(dir,"test.sqlite"));
            await using(var session=new DeviceSession(transport,store))
            {
                session.Run();await transport.Input.Writer.WriteAsync(Encoding.ASCII.GetBytes("Grbl 1.1h\n<Idle|WPos:0,0,0>\n"));
                await Wait(()=>session.Snapshot.Machine=="Idle");await session.ReadConfiguration();
                await Wait(()=>transport.Writes.Contains("$I\n"));
                await transport.Input.Writer.WriteAsync("[VER:1.1h]\nok\n"u8.ToArray());
                await Wait(()=>transport.Writes.Contains("$$\n"));
                await transport.Input.Writer.WriteAsync("$30=1000\n$32=1\nok\n"u8.ToArray());
                await Wait(()=>transport.Writes.Contains("$G\n"));
                await transport.Input.Writer.WriteAsync("[GC: G0 G54 G17 G21 G90 G94 M5]\nok\n"u8.ToArray());
                await Wait(()=>session.Snapshot.MaxPower==1000 && session.Snapshot.LaserMode);
                transport.Input.Writer.TryComplete(new IOException("detached"));
                await Wait(()=>session.Snapshot.Connection=="Lost");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true); }
    }
    private static async Task Wait(Func<bool> test)
    { for(int i=0;i<100;i++){if(test())return;await Task.Delay(20);}Assert.True(test()); }
    private sealed class ScriptedTransport:IByteTransport
    {
        public Channel<byte[]> Input {get;}=Channel.CreateUnbounded<byte[]>();
        public System.Collections.Concurrent.ConcurrentBag<string> Writes {get;}=[];
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken cancellationToken){Writes.Add(Encoding.ASCII.GetString(bytes.Span));return ValueTask.CompletedTask;}
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {await foreach(var x in Input.Reader.ReadAllAsync(token))yield return x;}
        public ValueTask DisposeAsync(){Input.Writer.TryComplete();return ValueTask.CompletedTask;}
    }
}
