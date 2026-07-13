import 'dart:async';

import 'package:web_socket_channel/web_socket_channel.dart';

import '../core/protocol.dart';

/// 確立済みWebSocket接続の抽象(テストで差し替える)。
abstract class WsConnection {
  /// 受信テキストメッセージ。接続が閉じるとdoneになる。
  Stream<String> get messages;

  void send(String data);

  Future<void> close();
}

/// 接続の確立方法の抽象。
abstract class WsConnector {
  /// 接続を確立する。失敗・タイムアウトは例外。
  Future<WsConnection> connect(Uri uri);
}

/// web_socket_channel による実装。
/// 重要: `WebSocketChannel.connect` は即座に返り、接続エラーは `.ready` まで分からない。
class RealWsConnector implements WsConnector {
  @override
  Future<WsConnection> connect(Uri uri) async {
    final channel = WebSocketChannel.connect(uri);
    await channel.ready.timeout(Protocol.connectTimeout);
    return _RealWsConnection(channel);
  }
}

class _RealWsConnection implements WsConnection {
  _RealWsConnection(this._channel);

  final WebSocketChannel _channel;

  @override
  Stream<String> get messages =>
      _channel.stream.where((e) => e is String).cast<String>();

  @override
  void send(String data) => _channel.sink.add(data);

  @override
  Future<void> close() async {
    await _channel.sink.close();
  }
}
