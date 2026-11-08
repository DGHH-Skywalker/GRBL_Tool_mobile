using LaserMobile.Application;
using LaserMobile.Domain;
var session = new SimulationSession();
session.Start();
for (var i = 0; i < 200 && !session.Sender.IsTerminal; i++) session.Step();
var snapshot = session.Sender.Snapshot;
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
return snapshot.State == ExecutionState.Completed ? 0 : 1;
