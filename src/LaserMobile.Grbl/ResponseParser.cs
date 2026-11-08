using System.Globalization;
using System.Text;
namespace LaserMobile.Grbl;

public abstract record GrblResponse;
public sealed record Acknowledgement : GrblResponse;
public sealed record CommandError(int Code) : GrblResponse;
public sealed record Alarm(int Code) : GrblResponse;
public sealed record Welcome(string Text) : GrblResponse;
public sealed record Status(string State, IReadOnlyDictionary<string, string> Fields) : GrblResponse;
public sealed record Message(string Text) : GrblResponse;

public static class ResponseParser
{
    public static GrblResponse Parse(string line)
    {
        if (line == "ok") return new Acknowledgement();
        if (line.StartsWith("error:", StringComparison.Ordinal) && int.TryParse(line.AsSpan(6), CultureInfo.InvariantCulture, out var e)) return new CommandError(e);
        if (line.StartsWith("ALARM:", StringComparison.Ordinal) && int.TryParse(line.AsSpan(6), CultureInfo.InvariantCulture, out var a)) return new Alarm(a);
        if (line.StartsWith("Grbl ", StringComparison.Ordinal)) return new Welcome(line);
        if (line.StartsWith('<') && line.EndsWith('>'))
        {
            var parts = line[1..^1].Split('|');
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in parts.Skip(1))
            {
                var colon = part.IndexOf(':');
                if (colon > 0) fields[part[..colon]] = part[(colon + 1)..];
            }
            return new Status(parts[0], fields);
        }
        return new Message(line);
    }
}

public sealed class ResponseFramer(int maxLineBytes = 1024)
{
    private readonly List<byte> buffer = [];
    public IReadOnlyList<GrblResponse> Feed(ReadOnlySpan<byte> bytes)
    {
        var responses = new List<GrblResponse>();
        foreach (var b in bytes)
        {
            if (b is 10 or 13)
            {
                if (buffer.Count > 0)
                {
                    responses.Add(ResponseParser.Parse(Encoding.ASCII.GetString(buffer.ToArray())));
                    buffer.Clear();
                }
            }
            else
            {
                if (b > 127 || b < 32) throw new FormatException("Non-ASCII/control byte in response.");
                if (buffer.Count >= maxLineBytes) throw new FormatException("Response line exceeds configured limit.");
                buffer.Add(b);
            }
        }
        return responses;
    }
}
