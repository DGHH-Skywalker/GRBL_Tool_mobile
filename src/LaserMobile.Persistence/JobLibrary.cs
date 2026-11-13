using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using LaserMobile.Domain;
namespace LaserMobile.Persistence;

public sealed class JobLibrary
{
    private readonly string root;
    public JobLibrary(string root) { this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
    public StoredJob Commit(string name, JobPlan plan, JobLimits? limits = null, string? parameters = null)
    {
        var existing = Directory.EnumerateFiles(root, "*.json").Select(p => new FileInfo(p)).ToArray();
        if (existing.Length >= 100 || existing.Sum(p => p.Length) >= 64_000_000) throw new IOException("任务库达到 100 项或 64 MB 上限，请删除旧任务");
        var content = string.Join('\n', plan.Lines) + "\n";
        if (Hash(content) != plan.Sha256) throw new InvalidDataException("任务哈希不匹配");
        var job = new StoredJob(plan.Id, Path.GetFileName(name), plan.Sha256, content, DateTimeOffset.UtcNow) { Limits = limits, Parameters = parameters };
        var path = Path.Combine(root, job.Id + ".json"); var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, job); file.Flush(true); }
        File.Move(temp, path);
        return job;
    }
    public IReadOnlyList<StoredJob> List()
    {
        var jobs = new List<StoredJob>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
        {
            try { var job = ReadFile(path); jobs.Add(job); }
            catch (Exception e) when (e is JsonException or InvalidDataException or IOException) { }
        }
        return jobs.OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
    }
    public void Delete(Guid id) => File.Delete(Path.Combine(root, id + ".json"));
    public StoredJob Read(Guid id) => ReadFile(Path.Combine(root, id + ".json"));
    private static StoredJob ReadFile(string path)
    {
        var job = JsonSerializer.Deserialize<StoredJob>(File.ReadAllText(path)) ?? throw new InvalidDataException("任务记录为空");
        if (job.Content is null || job.Name is null || job.Hash is null || Path.GetFileName(path) != job.Id + ".json") throw new InvalidDataException("任务元数据损坏");
        if (Hash(job.Content) != job.Hash) throw new InvalidDataException("任务文件损坏");
        return job;
    }
    private static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(s)));
}
