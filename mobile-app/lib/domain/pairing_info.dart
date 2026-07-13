import 'dart:convert';

import '../core/protocol.dart';

/// QRコードのペイロード(docs/protocol.md 5章)。
class QrPayload {
  const QrPayload({required this.host, required this.port, required this.token});

  final String host;
  final int port;
  final String token;

  Uri get wsUri => Uri(scheme: 'ws', host: host, port: port, path: Protocol.wsPath);

  /// QR文字列を解釈する。形式不正・プロトコル版不一致は null。
  static QrPayload? tryParse(String raw) {
    try {
      final json = jsonDecode(raw);
      if (json is! Map<String, dynamic>) return null;
      if (json['protocol'] != Protocol.version) return null;

      final host = json['host'];
      final port = json['port'];
      final token = json['token'];
      if (host is! String || host.isEmpty) return null;
      if (port is! int || port <= 0 || port > 65535) return null;
      if (token is! String || token.isEmpty) return null;

      return QrPayload(host: host, port: port, token: token);
    } on FormatException {
      return null;
    }
  }
}

/// ペアリング成立後に保存する接続情報。
class PairedRelay {
  const PairedRelay({
    required this.host,
    required this.port,
    required this.deviceId,
    required this.secret,
    required this.serverName,
  });

  final String host;
  final int port;
  final String deviceId;
  final String secret;
  final String serverName;

  Uri get wsUri => Uri(scheme: 'ws', host: host, port: port, path: Protocol.wsPath);

  Map<String, Object?> toJson() => {
        'host': host,
        'port': port,
        'deviceId': deviceId,
        'secret': secret,
        'serverName': serverName,
      };

  static PairedRelay? fromJson(String raw) {
    try {
      final json = jsonDecode(raw);
      if (json is! Map<String, dynamic>) return null;
      return PairedRelay(
        host: json['host'] as String,
        port: json['port'] as int,
        deviceId: json['deviceId'] as String,
        secret: json['secret'] as String,
        serverName: json['serverName'] as String? ?? '',
      );
    } catch (_) {
      return null;
    }
  }
}
