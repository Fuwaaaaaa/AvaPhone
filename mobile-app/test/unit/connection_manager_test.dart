import 'dart:async';
import 'dart:convert';

import 'package:avaphone/data/connection_manager.dart';
import 'package:avaphone/data/ws_connector.dart';
import 'package:avaphone/domain/pairing_info.dart';
import 'package:avaphone/domain/ws_messages.dart';
import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';

/// スクリプト可能な偽WebSocket接続。
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

  /// サーバー応答を注入する。
  void reply(Map<String, Object?> json) => controller.add(jsonEncode(json));

  Map<String, Object?> lastSentJson() =>
      jsonDecode(sent.last) as Map<String, Object?>;
}

class FakeConnector implements WsConnector {
  final connections = <FakeConnection>[];
  int connectCalls = 0;

  /// trueの間は接続確立自体が失敗する。
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

const _qr = QrPayload(host: '192.168.1.10', port: 27810, token: 'tok-1');

const _relay = PairedRelay(
  host: '192.168.1.10',
  port: 27810,
  deviceId: 'dev-1',
  secret: 'sec-1',
  serverName: 'PC',
);

Map<String, Object?> _authAck({String? secret}) => {
      'v': 1,
      'id': 'ack',
      'type': 'auth.ack',
      'deviceId': 'dev-1',
      if (secret != null) 'secret': secret,
      'serverName': 'PC',
      'timestamp': 0,
    };

void main() {
  test('ペアリング成功で connected になり資格情報が発行される', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector, deviceName: 'Test');
      PairedRelay? issued;
      manager.paired.listen((r) => issued = r);

      manager.startPairing(_qr);
      async.flushMicrotasks();

      final connection = connector.connections.single;
      final auth = connection.lastSentJson();
      expect(auth['type'], 'auth');
      expect(auth['token'], 'tok-1');
      expect(auth['deviceName'], 'Test');

      connection.reply(_authAck(secret: 'sec-1'));
      async.flushMicrotasks();

      expect(manager.phase, ConnectionPhase.connected);
      expect(issued, isNotNull);
      expect(issued!.deviceId, 'dev-1');
      expect(issued!.secret, 'sec-1');
      expect(issued!.host, '192.168.1.10');
    });
  });

  test('認証エラー(AUTH_FAILED)では自動リトライせず未ペアリングへ', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector);
      final errors = <ErrorMessage>[];
      manager.messages.listen((m) {
        if (m is ErrorMessage) errors.add(m);
      });

      manager.startPairing(_qr);
      async.flushMicrotasks();
      connector.connections.single.reply({
        'v': 1, 'id': 'e', 'type': 'error',
        'code': 'AUTH_FAILED', 'message': 'ng', 'timestamp': 0,
      });
      async.flushMicrotasks();

      expect(manager.phase, ConnectionPhase.unpaired);
      expect(errors.single.code, 'AUTH_FAILED');

      // 時間が経っても再接続しない
      async.elapse(const Duration(seconds: 30));
      expect(connector.connectCalls, 1);
    });
  });

  test('接続中はハートビートを2秒間隔で送る', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector);

      manager.startWithCredentials(_relay);
      async.flushMicrotasks();
      final connection = connector.connections.single;
      connection.reply(_authAck());
      async.flushMicrotasks();

      final before = connection.sent.length; // auth の1通
      async.elapse(const Duration(seconds: 6));
      // 6秒でping3通(サーバー応答が無いとwatchdogが落とすため、pongも返しておく)
      final pings = connection.sent
          .skip(before)
          .where((s) => s.contains('"type":"ping"'))
          .length;
      expect(pings, greaterThanOrEqualTo(2));
    });
  });

  test('6秒無応答で再接続し、バックオフ系列に従う', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector);

      manager.startWithCredentials(_relay);
      async.flushMicrotasks();
      connector.connections.single.reply(_authAck());
      async.flushMicrotasks();
      expect(manager.phase, ConnectionPhase.connected);

      // 以降サーバーは沈黙 → 6秒でwatchdog発火
      async.elapse(const Duration(seconds: 6));
      expect(manager.phase, ConnectionPhase.reconnecting);
      expect(connector.connectCalls, 1);

      // バックオフ1秒後に2回目の接続試行
      async.elapse(const Duration(seconds: 1));
      async.flushMicrotasks();
      expect(connector.connectCalls, 2);

      // 2回目も認証応答なし → authタイムアウト(5秒)→ バックオフ2秒 → 3回目
      async.elapse(const Duration(seconds: 5));
      expect(manager.phase, ConnectionPhase.reconnecting);
      async.elapse(const Duration(seconds: 2));
      async.flushMicrotasks();
      expect(connector.connectCalls, 3);

      // 3回目で認証成功 → connected へ復帰
      connector.connections.last.reply(_authAck());
      async.flushMicrotasks();
      expect(manager.phase, ConnectionPhase.connected);
    });
  });

  test('接続確立の失敗はバックオフ 1,2,5,10,10 で再試行する', () {
    fakeAsync((async) {
      final connector = FakeConnector()..failConnect = true;
      final manager = ConnectionManager(connector: connector);

      manager.startWithCredentials(_relay);
      async.flushMicrotasks();
      expect(connector.connectCalls, 1); // t=0

      async.elapse(const Duration(seconds: 1)); // +1s
      async.flushMicrotasks();
      expect(connector.connectCalls, 2);

      async.elapse(const Duration(seconds: 2)); // +2s
      async.flushMicrotasks();
      expect(connector.connectCalls, 3);

      async.elapse(const Duration(seconds: 5)); // +5s
      async.flushMicrotasks();
      expect(connector.connectCalls, 4);

      async.elapse(const Duration(seconds: 10)); // +10s
      async.flushMicrotasks();
      expect(connector.connectCalls, 5);

      async.elapse(const Duration(seconds: 10)); // +10s (以降10秒間隔)
      async.flushMicrotasks();
      expect(connector.connectCalls, 6);
    });
  });

  test('parameter.set は接続中のみ送信できる', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector);

      expect(manager.sendParameterSet('Phone/Page', 4), isNull);

      manager.startWithCredentials(_relay);
      async.flushMicrotasks();
      final connection = connector.connections.single;
      connection.reply(_authAck());
      async.flushMicrotasks();

      final id = manager.sendParameterSet('Phone/Page', 4);
      expect(id, isNotNull);
      final sent = connection.lastSentJson();
      expect(sent['type'], 'parameter.set');
      expect(sent['id'], id);
      expect(sent['parameter'], 'Phone/Page');
      expect(sent['value'], 4);
    });
  });

  test('サーバーメッセージがストリームに流れる', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector);
      final received = <ServerMessage>[];
      manager.messages.listen(received.add);

      manager.startWithCredentials(_relay);
      async.flushMicrotasks();
      final connection = connector.connections.single;
      connection.reply(_authAck());
      async.flushMicrotasks();

      connection.reply({
        'v': 1, 'id': 'u1', 'type': 'state.update',
        'parameter': 'Phone/Pose', 'value': 2, 'timestamp': 0,
      });
      async.flushMicrotasks();

      final update = received.whereType<StateUpdateMessage>().single;
      expect(update.parameter, 'Phone/Pose');
      expect(update.value, 2);
    });
  });

  test('stopで明示切断すると再接続しない', () {
    fakeAsync((async) {
      final connector = FakeConnector();
      final manager = ConnectionManager(connector: connector);

      manager.startWithCredentials(_relay);
      async.flushMicrotasks();
      connector.connections.single.reply(_authAck());
      async.flushMicrotasks();

      manager.stop();
      async.flushMicrotasks();
      expect(manager.phase, ConnectionPhase.disconnected);

      async.elapse(const Duration(seconds: 30));
      expect(connector.connectCalls, 1);
    });
  });
}
