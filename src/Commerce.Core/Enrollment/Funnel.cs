using System.Collections.Concurrent;
using Commerce.Core.Catalog;

namespace Commerce.Core.Enrollment;

public enum FunnelStage { EnrollmentStarted, CheckoutOpened, PaymentCompleted }

/// <summary>One step of the discovery-to-payment path. Holds no personal data.</summary>
public sealed record FunnelEvent(
    DateTimeOffset At,
    FunnelStage Stage,
    string ReferenceId,
    string? ProductId = null,
    string? OfferId = null,
    string? Channel = null,
    Money? Amount = null);

public interface IFunnelLog
{
    void Record(FunnelEvent funnelEvent);
    IReadOnlyList<FunnelEvent> Recent(int max);
    FunnelEvent? FindStart(string referenceId);
}

/// <summary>Prototype storage: the last events in memory. Production writes to a database.</summary>
public sealed class InMemoryFunnelLog : IFunnelLog
{
    private const int Capacity = 1_000;
    private readonly ConcurrentQueue<FunnelEvent> _events = new();

    public void Record(FunnelEvent funnelEvent)
    {
        _events.Enqueue(funnelEvent);
        while (_events.Count > Capacity)
            _events.TryDequeue(out _);
    }

    public IReadOnlyList<FunnelEvent> Recent(int max) => _events.Reverse().Take(max).ToList();

    public FunnelEvent? FindStart(string referenceId) =>
        _events.LastOrDefault(e => e.Stage == FunnelStage.EnrollmentStarted && e.ReferenceId == referenceId);
}
