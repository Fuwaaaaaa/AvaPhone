/// docs/protocol.md のトランスポート定数(単一情報源に従う)。
class Protocol {
  Protocol._();

  static const int version = 1;

  static const int defaultWsPort = 27810;
  static const String wsPath = '/ws';

  /// ハートビート送信間隔。
  static const Duration heartbeatInterval = Duration(seconds: 2);

  /// 最終受信からこの時間応答が無ければ切断とみなす。
  static const Duration disconnectTimeout = Duration(seconds: 6);

  /// WebSocket接続確立のタイムアウト。
  static const Duration connectTimeout = Duration(seconds: 5);

  /// auth送信からauth.ack受信までのタイムアウト。
  static const Duration authTimeout = Duration(seconds: 5);

  /// 再接続バックオフ(仕様 5.3)。最後の値を以降繰り返す。
  static const List<Duration> reconnectBackoff = [
    Duration(seconds: 1),
    Duration(seconds: 2),
    Duration(seconds: 5),
    Duration(seconds: 10),
  ];
}
