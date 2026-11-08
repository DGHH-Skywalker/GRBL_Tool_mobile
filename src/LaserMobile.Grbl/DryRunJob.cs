using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using LaserMobile.Domain;
namespace LaserMobile.Grbl;

/// <summary>Initial bounded, no-emission dialect for simulator development.</summary>
public static partial class DryRunJob
{
    [GeneratedRegex(@"^G[01] X(\d+(?:\.\d+)?) Y(\d+(?:\.\d+)?) F(\d+(?:\.\d+)?)$")]
    private static partial Regex Move();
    public static JobPlan Parse(string content)
    {
        if (content.Length is 0 or > 64000 || content.Any(c => c > 127 || (char.IsControl(c) && c is not '\r' and not '\n')))
            throw new FormatException("Expected bounded ASCII dry-run job.");
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToArray();
        if (lines.Length is < 4 or > 2000 || !lines.Take(3).SequenceEqual(["G21", "G90", "M5"]) || lines[^1] != "M5")
            throw new FormatException("Expected G21/G90/M5 prefix and final M5.");
        foreach (var line in lines)
        {
            if (line is "G21" or "G90" or "M5" or "S0") continue;
            var match = Move().Match(line);
            if (!match.Success || line.Length > 70) throw new FormatException("Unsupported dry-run command.");
            double Value(int group) => double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
            if (Value(1) > 40 || Value(2) > 40 || Value(3) is <= 0 or > 1000) throw new FormatException("Outside simulator envelope.");
        }
        var canonical = string.Join('\n', lines) + "\n";
        return new JobPlan(Guid.NewGuid(), Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(canonical))), Array.AsReadOnly(lines));
    }
}
