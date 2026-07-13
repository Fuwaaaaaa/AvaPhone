import 'dart:convert';

import '../core/protocol.dart';

/// 中継アプリ → スマホ のメッセージ(docs/protocol.md 4章)。
sealed class ServerMessage {
  const ServerMessage(this.id);

  final String id;

  /// JSONを型付きメッセージへ変換する。不正・未知のメッセージは [UnknownMessage]。
  static ServerMessage parse(String raw) {
    try {
      final json = jsonDecode(raw);
      if (json is! Map<String, dynamic>) return const UnknownMessage('');

      final id = json['id'] is String ? json['id'] as String : '';
      final type = json['type'];

      switch (type) {
        case 'auth.ack':
          return AuthAckMessage(
            id,
            deviceId: json['deviceId'] as String? ?? '',
            secret: json['secret'] as String?,
            serverName: json['serverName'] as String? ?? '',
          );
        case 'pong':
          return PongMessage(id);
        case 'parameter.ack':
          return ParameterAckMessage(
            id,
            parameter: json['parameter'] as String? ?? '',
            value: json['value'],
            applied: json['status'] == 'applied',
          );
        case 'state.snapshot':
          final params = json['parameters'];
          return StateSnapshotMessage(
            id,
            avatarId: json['avatarId'] as String?,
            supported: json['supported'] == true,
            vrchat: json['vrchat'] as String? ?? 'not_found',
            parameters: params is Map<String, dynamic>
                ? Map<String, Object?>.from(params)
                : const {},
          );
        case 'state.update':
          return StateUpdateMessage(
            id,
            parameter: json['parameter'] as String? ?? '',
            value: json['value'],
          );
        case 'error':
          return ErrorMessage(
            id,
            code: json['code'] as String? ?? 'UNKNOWN',
            message: json['message'] as String? ?? '',
          );
        default:
          return UnknownMessage(id);
      }
    } on FormatException {
      return const UnknownMessage('');
    }
  }
}

class AuthAckMessage extends ServerMessage {
  const AuthAckMessage(super.id,
      {required this.deviceId, this.secret, required this.serverName});

  final String deviceId;

  /// 初回ペアリング時のみ発行される。安全に保存すること。
  final String? secret;
  final String serverName;
}

class PongMessage extends ServerMessage {
  const PongMessage(super.id);
}

class ParameterAckMessage extends ServerMessage {
  const ParameterAckMessage(super.id,
      {required this.parameter, required this.value, required this.applied});

  final String parameter;
  final Object? value;

  /// false はタイムアウト(VRChatから確定値を確認できなかった)。
  final bool applied;
}

class StateSnapshotMessage extends ServerMessage {
  const StateSnapshotMessage(super.id,
      {required this.avatarId,
      required this.supported,
      required this.vrchat,
      required this.parameters});

  final String? avatarId;
  final bool supported;

  /// connected / not_found / osc_disabled
  final String vrchat;
  final Map<String, Object?> parameters;
}

class StateUpdateMessage extends ServerMessage {
  const StateUpdateMessage(super.id, {required this.parameter, required this.value});

  final String parameter;
  final Object? value;
}

class ErrorMessage extends ServerMessage {
  const ErrorMessage(super.id, {required this.code, required this.message});

  final String code;
  final String message;
}

class UnknownMessage extends ServerMessage {
  const UnknownMessage(super.id);
}

/// スマホ → 中継アプリ のメッセージ生成。
class ClientMessages {
  ClientMessages._();

  static Map<String, Object?> _envelope(String id, String type) => {
        'v': Protocol.version,
        'id': id,
        'type': type,
        'timestamp': DateTime.now().millisecondsSinceEpoch,
      };

  /// 初回ペアリング認証(QRコードのトークン)。
  static String authWithToken(String id, String token, String deviceName) =>
      jsonEncode({
        ..._envelope(id, 'auth'),
        'token': token,
        'deviceName': deviceName,
      });

  /// 再接続認証(発行済み資格情報)。
  static String authWithCredentials(String id, String deviceId, String secret) =>
      jsonEncode({
        ..._envelope(id, 'auth'),
        'deviceId': deviceId,
        'secret': secret,
      });

  static String ping(String id) => jsonEncode(_envelope(id, 'ping'));

  static String parameterSet(String id, String parameter, Object value) =>
      jsonEncode({
        ..._envelope(id, 'parameter.set'),
        'parameter': parameter,
        'value': value,
      });
}
