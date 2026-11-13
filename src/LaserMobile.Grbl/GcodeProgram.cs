using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LaserMobile.Domain;
namespace LaserMobile.Grbl;

/// <summary>Explicit XY laser dialect. Every accepted token participates in analysis.</summary>
public static partial class GcodeProgram
{
    [GeneratedRegex(@"([A-Z])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")]
    private static partial Regex Word();
    public static ProgramAnalysis Prepare(string content, JobLimits limits, WorkPosition? origin = null)
    {
        limits.Validate();
        if (content.Length is 0 or > 4_000_000) throw new FormatException("文件为空或超过 4 MB");
        if (content.Any(c => c > 127 || (char.IsControl(c) && c is not '\n' and not '\r' and not '\t') || c is '!' or '~' or '?' or '$'))
            throw new FormatException("加工文件含实时/配置字符或非 ASCII 字符");
        var lines = new List<string>(); var segments = new List<ToolSegment>();
        double x = origin?.X ?? 0, y = origin?.Y ?? 0, scale = 1, feed = 0, power = 0, seconds = 0;
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x > limits.Width || y > limits.Height)
            throw new FormatException("起始工作坐标超出工作区域");
        double minX = x, minY = y, maxX = x, maxY = y;
        bool absolute = true, unitsKnown = false, distanceKnown = false, enabled = false, emits = false;
        int motion = 0; bool laserInitialized = false, motionKnown = false;
        foreach (var raw in content.Replace("\r", "").Split('\n'))
        {
            var clean = new StringBuilder(); bool comment = false;
            foreach (var c in raw.ToUpperInvariant())
            {
                if (c == ';' && !comment) break;
                if (c == '(') { if (comment) throw new FormatException("嵌套注释"); comment = true; continue; }
                if (c == ')') { if (!comment) throw new FormatException("注释括号不匹配"); comment = false; continue; }
                if (!comment && !char.IsWhiteSpace(c)) clean.Append(c);
            }
            if (comment) throw new FormatException("注释未闭合");
            var line = clean.ToString(); if (line.Length == 0) continue;
            if (line.Length > 70 || lines.Count >= 200000) throw new FormatException("行长或行数超限");
            var matches = Word().Matches(line);
            if (string.Concat(matches.Select(m => m.Value)) != line) throw new FormatException($"无法解析: {line}");
            var words = new Dictionary<char, double>(); int? moveThisLine = null; bool? enableThisLine = null;
            var groups = new HashSet<string>();
            foreach (Match match in matches)
            {
                char key = match.Groups[1].Value[0];
                double value = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!double.IsFinite(value)) throw new FormatException("非法数值");
                if (key == 'G')
                {
                    string group = value switch { 0 or 1 => "motion", 20 or 21 => "units", 90 or 91 => "distance", 17 => "plane", 94 => "feed", 54 => "coordinate", _ => throw new FormatException($"暂未支持 G{value}") };
                    if (!groups.Add(group)) throw new FormatException("同一模态组重复");
                    if (value is 0 or 1) moveThisLine = (int)value;
                    if (value is 20 or 21) { scale = value == 20 ? 25.4 : 1; unitsKnown = true; }
                    if (value is 90 or 91) { absolute = value == 90; distanceKnown = true; }
                }
                else if (key == 'M')
                {
                    if (!groups.Add("spindle") || value is not (3 or 4 or 5)) throw new FormatException("不支持或重复的 M 指令");
                    enableThisLine = value != 5;
                }
                else
                {
                    if (key is not ('X' or 'Y' or 'F' or 'S') || !words.TryAdd(key, value)) throw new FormatException("不支持或重复的参数");
                }
            }
            if (moveThisLine is not null) { motion = moveThisLine.Value; motionKnown = true; }
            if (words.TryGetValue('F', out var f)) { feed = f * scale; if (feed <= 0 || feed > limits.MaxFeed) throw new FormatException("进给超限"); }
            if (words.TryGetValue('S', out var s)) { power = s; if (s < 0 || s > limits.MaxPower) throw new FormatException("功率超限"); }
            if (enableThisLine is not null)
            {
                if (enableThisLine == false) laserInitialized = true;
                else if (!laserInitialized) throw new FormatException("开启激光前必须先 M5 初始化");
                enabled = enableThisLine.Value;
            }
            emits |= enabled && power > 0;
            if (words.ContainsKey('X') || words.ContainsKey('Y'))
            {
                if (!laserInitialized) throw new FormatException("移动前必须先 M5 初始化");
                if (!motionKnown) throw new FormatException("首次移动必须声明 G0/G1");
                if (!unitsKnown || !distanceKnown) throw new FormatException("移动前必须声明 G20/G21 与 G90/G91");
                double nx = words.TryGetValue('X', out var vx) ? (absolute ? 0 : x) + vx * scale : x;
                double ny = words.TryGetValue('Y', out var vy) ? (absolute ? 0 : y) + vy * scale : y;
                if (nx < -0.00001 || ny < -0.00001 || nx > limits.Width + 0.00001 || ny > limits.Height + 0.00001) throw new FormatException("路径超出工作区域");
                if (motion == 1 && feed <= 0) throw new FormatException("G1 移动缺少进给速度");
                var activePower = motion == 1 && enabled ? power : 0;
                emits |= activePower > 0;
                segments.Add(new(x, y, nx, ny, motion == 0, activePower));
                seconds += Math.Sqrt(Math.Pow(nx - x, 2) + Math.Pow(ny - y, 2)) / (motion == 0 ? limits.MaxFeed : feed) * 60;
                x = nx; y = ny; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            lines.Add(string.Join(' ', matches.Select(m => m.Value)));
        }
        if (lines.Count == 0 || segments.Count == 0) throw new FormatException("文件没有可加工路径");
        // Canonical output always ends with an explicit laser-off command.
        if (lines[^1] != "M5") lines.Add("M5");
        var canonical = string.Join('\n', lines) + "\n";
        var plan = new JobPlan(Guid.NewGuid(), Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(canonical))), lines.AsReadOnly());
        return new(plan, segments.AsReadOnly(), minX, minY, maxX, maxY, seconds, emits);
    }
}
