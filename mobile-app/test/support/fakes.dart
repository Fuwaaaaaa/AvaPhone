import 'dart:async';
import 'dart:convert';

import 'package:avaphone/data/ws_connector.dart';

/// スクリプト可能な偽WebSocket接続(単体・widgetテスト共用)。
class FakeConnection implements WsConnection {
  final controller = StreamController<String>();
  final sent = <String>[];
  bool closed = false;

  @override
  Stream<String> get messages => controller.stream;

  @override
  void send(String data) => sent.add(data);

  @override
  Future<void> close() async {
    closed = true;
    if (!controller.isClosed) await controller.close();
  }

  void reply(Map<String, Object?> json) => controller.add(jsonEncode(json));

  Map<String, Object?> lastSentJson() =>
      jsonDecode(sent.last) as Map<String, Object?>;

  List<Map<String, Object?>> sentOfType(String type) => sent
      .map((s) => jsonDecode(s) as Map<String, Object?>)
      .where((m) => m['type'] == type)
      .toList();
}

class FakeConnector implements WsConnector {
  final connections = <FakeConnection>[];
  int connectCalls = 0;
  bool failConnect = false;

  @override
  Future<WsConnection> connect(Uri uri) {
    connectCalls++;
    if (failConnect) {
      return Future.error(Exception('接続失敗'));
    }
    final connection = FakeConnection();
    connections.add(connection);
    return Future.value(connection);
  }
}

Map<String, Object?> authAckJson({String? secret}) => {
      'v': 1,
      'id': 'ack',
      'type': 'auth.ack',
      'deviceId': 'dev-1',
      if (secret != null) 'secret': secret,
      'serverName': 'TestPC',
      'timestamp': 0,
    };

Map<String, Object?> snapshotJson({
  String avatarId = 'avtr_test',
  bool supported = true,
  String vrchat = 'connected',
  Map<String, Object?>? parameters,
}) =>
    {
      'v': 1,
      'id': 'snap',
      'type': 'state.snapshot',
      'avatarId': avatarId,
      'supported': supported,
      'vrchat': vrchat,
      'parameters': parameters ??
          {
            'Phone/Visible': true,
            'Phone/Connected': true,
            'Phone/Locked': false,
            'Phone/Page': 1,
            'Phone/Pose': 1,
            'Phone/Battery': 8,
            'Phone/CallState': 0,
            'Phone/MediaState': 0,
            'Phone/NotifyType': 0,
            'Phone/EventToggle': false,
          },
      'timestamp': 0,
    };
