using System.Text.Json;

namespace YeusepesModules.SPOTIOSC.Runtime.Events;

internal sealed class EventDeduplicator
{
    private const int Capacity = 1_000;
    private readonly HashSet<string> _keys = [];
    private readonly Queue<string> _order = [];
    private readonly object _gate = new();

    public bool IsDuplicate(JsonElement session)
    {
        if (session.ValueKind != JsonValueKind.Object ||
            !session.TryGetProperty("session_id", out var sessionId) ||
            !session.TryGetProperty("timestamp", out var timestamp))
            return false;

        var key = $"{sessionId}:{timestamp}";
        lock (_gate)
        {
            if (!_keys.Add(key)) return true;
            _order.Enqueue(key);
            if (_order.Count > Capacity) _keys.Remove(_order.Dequeue());
            return false;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _keys.Clear();
            _order.Clear();
        }
    }
}
