import '../core/protocol.dart';

/// 再接続バックオフ(1, 2, 5, 10, 以降10秒)。
class Backoff {
  Backoff([List<Duration>? table]) : _table = table ?? Protocol.reconnectBackoff;

  final List<Duration> _table;
  int _attempt = 0;

  int get attempt => _attempt;

  /// 次の待ち時間を返し、試行回数を進める。
  Duration next() {
    final index = _attempt < _table.length ? _attempt : _table.length - 1;
    _attempt++;
    return _table[index];
  }

  /// 接続成功時に呼ぶ。
  void reset() => _attempt = 0;
}
