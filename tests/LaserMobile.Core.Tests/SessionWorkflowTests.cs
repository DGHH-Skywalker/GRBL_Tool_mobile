using System.Text;
using System.Threading.Channels;
using LaserMobile.Application;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Persistence;
using Xunit;
namespace LaserMobile.Core.Tests;

public sealed class SessionWorkflowTests
{
    [Fact] public async Task EndToEndStartIsIdempotentAndRestartRemainsManual()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(root);
        try
        {
            var transport=new Controller();var store=new ExecutionStore(Path.Combine(root,"runs.sqlite"));
            await using(var session=new DeviceSession(transport,store))
            {
                session.Run();await Wait(()=>session.Snapshot.Machine=="Idle");await session.ReadConfiguration();
                await Wait(()=>transport.Writes.Contains("$G\n"));await Task.Delay(50);
                var job=new JobLibrary(Path.Combine(root,"jobs")).Commit("job",GcodeProgram.Prepare(SimulationSession.Sample,new(40,40,1200,1000)).Plan);
                var command=Guid.NewGuid();var epoch=session.Snapshot.ControllerEpoch;
                await Assert.ThrowsAsync<InvalidOperationException>(()=>session.Start(job,new(40,40,1200,1000),command,epoch,false));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>session.Start(job,new(40,40,1200,1000),command,epoch-1,true));
                await session.Start(job,new(40,40,1200,1000),command,epoch,true);
                await Wait(()=>session.Snapshot.Execution?.State==ExecutionState.Completed);
                await session.Start(job,new(40,40,1200,1000),command,epoch,true);
                Assert.Equal(1,transport.Writes.Count(x=>x=="G21\n"));
                Assert.Equal(ExecutionState.Completed,store.History()[0].State);
            }
            Assert.Equal(0,new ExecutionStore(Path.Combine(root,"runs.sqlite")).RecoverInterrupted());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true); }
    }
    [Fact] public void PresetParsingIsVersionedAndBounded()
    {
        var p=LaserMobile.Extensions.Abstractions.DevicePreset.Parse("""{"schemaVersion":1,"id":"custom","displayName":"Manual","firmwareFamily":"grbl1.1","width":40,"height":40,"maxFeed":1200,"maxPower":1000}""");
        Assert.Equal(40,p.Limits.Width);
        Assert.Throws<FormatException>(()=>LaserMobile.Extensions.Abstractions.DevicePreset.Parse("""{"schemaVersion":2}"""));
    }
    private static async Task Wait(Func<bool> condition)
    {for(int i=0;i<150;i++){if(condition())return;await Task.Delay(20);}Assert.True(condition());}
    private sealed class Controller:IByteTransport
    {
        private readonly Channel<byte[]> channel=Channel.CreateUnbounded<byte[]>();
        public System.Collections.Concurrent.ConcurrentBag<string> Writes {get;}=[];
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken token)
        {
            var line=Encoding.ASCII.GetString(bytes.Span);Writes.Add(line);
            string response=line switch
            {
                "?"=>"<Idle|WPos:0,0,0>\n",
                "$I\n"=>"[VER:1.1h]\nok\n",
                "$$\n"=>"$30=1000\n$32=1\nok\n",
                "$G\n"=>"[GC: G0 G54 G17 G21 G90 G94 M5]\nok\n",
                "!"=>"<Hold:0|WPos:0,0,0>\n",
                _=>"ok\n"
            };
            channel.Writer.TryWrite(Encoding.ASCII.GetBytes(response));return ValueTask.CompletedTask;
        }
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { await foreach(var data in channel.Reader.ReadAllAsync(token))yield return data; }
        public ValueTask DisposeAsync(){channel.Writer.TryComplete();return ValueTask.CompletedTask;}
    }
}
