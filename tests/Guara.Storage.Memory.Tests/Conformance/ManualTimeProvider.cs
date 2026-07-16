namespace Guara.Storage.Conformance;

/// <summary>Relógio controlável para testes determinísticos de lease/TTL.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
