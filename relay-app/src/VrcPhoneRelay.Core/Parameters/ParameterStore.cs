namespace VrcPhoneRelay.Core.Parameters;

/// <summary>ack待ちの parameter.set 1件。</summary>
public sealed record PendingOp(string MessageId, string Parameter, ParamValue Expected, DateTimeOffset Deadline);

/// <summary>確定またはタイムアウトした parameter.set。Applied=false はタイムアウト。</summary>
public sealed record ResolvedOp(string MessageId, string Parameter, ParamValue Value, bool Applied);

public sealed record CommitResult(
    bool ValueChanged,
    IReadOnlyList<ResolvedOp> Resolved,
    /// <summary>保留中の要求と無関係に値が変わった(=Expression Menu等、VRChat側起点の変更)。</summary>
    bool OriginatedExternally);

/// <summary>
/// パラメータ状態の正本。VRChatから最後に出力された値を保持する(docs/protocol.md 12.3)。
/// スレッドセーフ。
/// </summary>
public sealed class ParameterStore
{
    /// <summary>parameter.set から確定確認までの期限(仕様 12.4)。</summary>
    public static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(1.5);

    private readonly object _lock = new();
    private readonly Dictionary<string, ParamValue> _confirmed = new(StringComparer.Ordinal);
    private readonly List<PendingOp> _pending = [];

    /// <summary>アバター検出時に現在値で初期化する。保留中の要求はすべて破棄する。</summary>
    public void Initialize(IReadOnlyDictionary<string, ParamValue>? currentValues = null)
    {
        lock (_lock)
        {
            _pending.Clear();
            _confirmed.Clear();
            foreach (var def in PhoneParameters.All)
            {
                _confirmed[def.Name] = def.Default;
            }

            if (currentValues is null) return;
            foreach (var (name, value) in currentValues)
            {
                if (PhoneParameters.TryGet(name, out _))
                {
                    _confirmed[name] = value;
                }
            }
        }
    }

    /// <summary>アバター変更時: 前アバター向けの未処理コマンドを破棄する(仕様 11)。</summary>
    public IReadOnlyList<PendingOp> DropAllPending()
    {
        lock (_lock)
        {
            var dropped = _pending.ToArray();
            _pending.Clear();
            return dropped;
        }
    }

    public IReadOnlyDictionary<string, ParamValue> Snapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, ParamValue>(_confirmed, StringComparer.Ordinal);
        }
    }

    public bool TryGet(string parameter, out ParamValue value)
    {
        lock (_lock)
        {
            return _confirmed.TryGetValue(parameter, out value);
        }
    }

    /// <summary>OSC送信直前に呼び、VRChatエコー待ちとして登録する。</summary>
    public PendingOp RegisterPending(string messageId, string parameter, ParamValue expected, DateTimeOffset now)
    {
        var op = new PendingOp(messageId, parameter, expected, now + AckTimeout);
        lock (_lock)
        {
            _pending.Add(op);
        }

        return op;
    }

    /// <summary>
    /// VRChatからのパラメータ出力を確定値として取り込み、一致した保留要求を解決する。
    /// </summary>
    public CommitResult Commit(string parameter, ParamValue value)
    {
        lock (_lock)
        {
            var changed = !_confirmed.TryGetValue(parameter, out var prev) || prev != value;
            _confirmed[parameter] = value;

            List<ResolvedOp>? resolved = null;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var op = _pending[i];
                if (op.Parameter != parameter || op.Expected != value) continue;
                (resolved ??= []).Add(new ResolvedOp(op.MessageId, parameter, value, Applied: true));
                _pending.RemoveAt(i);
            }

            return new CommitResult(
                changed,
                (IReadOnlyList<ResolvedOp>?)resolved ?? [],
                OriginatedExternally: changed && resolved is null);
        }
    }

    /// <summary>期限切れの保留要求を取り除き、タイムアウトとして返す。定期的に呼ぶ。</summary>
    public IReadOnlyList<ResolvedOp> SweepExpired(DateTimeOffset now)
    {
        lock (_lock)
        {
            List<ResolvedOp>? expired = null;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var op = _pending[i];
                if (op.Deadline > now) continue;
                (expired ??= []).Add(new ResolvedOp(op.MessageId, op.Parameter, op.Expected, Applied: false));
                _pending.RemoveAt(i);
            }

            return (IReadOnlyList<ResolvedOp>?)expired ?? [];
        }
    }
}
