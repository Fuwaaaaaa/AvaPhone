import 'package:avaphone/core/phone_parameters.dart';
import 'package:avaphone/data/backoff.dart';
import 'package:avaphone/domain/pairing_info.dart';
import 'package:avaphone/domain/ws_messages.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Backoff', () {
    test('仕様のバックオフ系列 1,2,5,10,以降10秒', () {
      final backoff = Backoff();
      expect(backoff.next(), const Duration(seconds: 1));
      expect(backoff.next(), const Duration(seconds: 2));
      expect(backoff.next(), const Duration(seconds: 5));
      expect(backoff.next(), const Duration(seconds: 10));
      expect(backoff.next(), const Duration(seconds: 10));
      expect(backoff.next(), const Duration(seconds: 10));

      backoff.reset();
      expect(backoff.next(), const Duration(seconds: 1));
    });
  });

  group('バッテリー段階変換', () {
    test('仕様の境界値(0=0-4%, 1=5-14%, ..., 10=95-100%)', () {
      expect(PhoneParameters.batteryLevelToStep(0), 0);
      expect(PhoneParameters.batteryLevelToStep(4), 0);
      expect(PhoneParameters.batteryLevelToStep(5), 1);
      expect(PhoneParameters.batteryLevelToStep(14), 1);
      expect(PhoneParameters.batteryLevelToStep(15), 2);
      expect(PhoneParameters.batteryLevelToStep(84), 8);
      expect(PhoneParameters.batteryLevelToStep(85), 9);
      expect(PhoneParameters.batteryLevelToStep(94), 9);
      expect(PhoneParameters.batteryLevelToStep(95), 10);
      expect(PhoneParameters.batteryLevelToStep(100), 10);
    });
  });

  group('QrPayload', () {
    test('正しいQRを解釈できる', () {
      final payload = QrPayload.tryParse(
          '{"protocol":1,"host":"192.168.1.10","port":27810,"token":"abc"}');

      expect(payload, isNotNull);
      expect(payload!.host, '192.168.1.10');
      expect(payload.port, 27810);
      expect(payload.token, 'abc');
      expect(payload.wsUri.toString(), 'ws://192.168.1.10:27810/ws');
    });

    test('プロトコル版不一致・不正な形式はnull', () {
      expect(QrPayload.tryParse('{"protocol":2,"host":"h","port":1,"token":"t"}'), isNull);
      expect(QrPayload.tryParse('{"protocol":1,"host":"","port":1,"token":"t"}'), isNull);
      expect(QrPayload.tryParse('{"protocol":1,"host":"h","port":0,"token":"t"}'), isNull);
      expect(QrPayload.tryParse('{"protocol":1,"host":"h","port":1}'), isNull);
      expect(QrPayload.tryParse('not json'), isNull);
      expect(QrPayload.tryParse('[1,2]'), isNull);
    });
  });

  group('ServerMessage.parse', () {
    test('auth.ack(初回=secret付き)', () {
      final msg = ServerMessage.parse(
          '{"v":1,"id":"a1","type":"auth.ack","deviceId":"d1","secret":"s1","serverName":"PC","timestamp":0}');

      final ack = msg as AuthAckMessage;
      expect(ack.deviceId, 'd1');
      expect(ack.secret, 's1');
      expect(ack.serverName, 'PC');
    });

    test('parameter.ack applied/timeout', () {
      final applied = ServerMessage.parse(
              '{"v":1,"id":"c1","type":"parameter.ack","parameter":"Phone/Page","value":4,"status":"applied","timestamp":0}')
          as ParameterAckMessage;
      expect(applied.applied, isTrue);
      expect(applied.value, 4);

      final timeout = ServerMessage.parse(
              '{"v":1,"id":"c2","type":"parameter.ack","parameter":"Phone/Page","value":2,"status":"timeout","timestamp":0}')
          as ParameterAckMessage;
      expect(timeout.applied, isFalse);
    });

    test('state.snapshot', () {
      final msg = ServerMessage.parse(
              '{"v":1,"id":"s1","type":"state.snapshot","avatarId":"avtr_x","supported":true,"vrchat":"connected","parameters":{"Phone/Visible":true,"Phone/Page":3},"timestamp":0}')
          as StateSnapshotMessage;

      expect(msg.avatarId, 'avtr_x');
      expect(msg.supported, isTrue);
      expect(msg.vrchat, 'connected');
      expect(msg.parameters['Phone/Visible'], true);
      expect(msg.parameters['Phone/Page'], 3);
    });

    test('error', () {
      final msg = ServerMessage.parse(
              '{"v":1,"id":"e1","type":"error","code":"RATE_LIMITED","message":"m","timestamp":0}')
          as ErrorMessage;
      expect(msg.code, 'RATE_LIMITED');
    });

    test('不正なJSONは例外を投げずUnknownMessage', () {
      expect(ServerMessage.parse('{broken'), isA<UnknownMessage>());
      expect(ServerMessage.parse('[1]'), isA<UnknownMessage>());
      expect(ServerMessage.parse('{"type":"unknown.kind","id":"x"}'), isA<UnknownMessage>());
    });
  });

  group('ClientMessages', () {
    test('parameter.setはエンベロープを持つ', () {
      final json = ClientMessages.parameterSet('id-1', 'Phone/Page', 4);

      expect(json, contains('"v":1'));
      expect(json, contains('"id":"id-1"'));
      expect(json, contains('"type":"parameter.set"'));
      expect(json, contains('"parameter":"Phone/Page"'));
      expect(json, contains('"value":4'));
    });
  });
}
