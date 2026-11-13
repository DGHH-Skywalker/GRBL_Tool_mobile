namespace LaserMobile.Application;

/// <summary>Rejects stale conversion results and requires regeneration after any input edit.</summary>
public sealed class PreparationRevision
{
    public long Revision { get; private set; }
    private long? prepared;
    public bool IsCurrent => prepared == Revision;
    public long Invalidate() { prepared = null; return ++Revision; }
    public bool Commit(long revision)
    {
        if (revision != Revision) return false;
        prepared = revision; return true;
    }
}
