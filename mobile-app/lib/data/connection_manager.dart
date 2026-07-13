import 'dart:async';

import 'package:uuid/uuid.dart';

import '../core/protocol.dart';
import '../domain/pairing_info.dart';
import '../domain/ws_messages.dart';
import 'backoff.dart';
import 'ws_connector.dart';

/// 接続フェーズ(docs/protocol.md 2章の状態機械)。
enum ConnectionPhase {
  /// ペアリング未実施(QR読み取り待ち)。
  unpaired,

  /// 接続・認証試行中。
  connecting,

  /// 認証済み・稼働中。
  connected,

  /// 切断からの自動再接続待ち。
  reconnecting,

  /// 手動停止・認証失敗による停止。
  disconnected,
}

/// 接続・認証・ハートビート・自動再接続を担う状態機械。
/// UIから独立した純Dartクラス(fake_asyncで単体テスト可能)。
class ConnectionManager {
  ConnectionManager({required WsConnector connector, String deviceName = 'AvaPhone'})
      : _connector = connector,
        _deviceName = deviceName;

  final WsConnector _connector;
  final String _deviceName;
  final _uuid = const Uuid();

  final _phaseController = StreamController<ConnectionPhase>.broadcast();
  final _messageController = StreamController<ServerMessage>.broadcast();
  final _pairedController = StreamController<PairedRelay>.broadcast();

  ConnectionPhase _phase = ConnectionPhase.unpaired;
  WsConnection? _connection;
  StreamSubscription<String>? _subscription;
  Timer? _heartbeatTimer;
  Timer? _watchdogTimer;
  Timer? _reconnectTimer;
  final Backoff _backoff = Backoff();
  PairedRelay? _credentials;
  int _generation = 0; // 古い接続試行の残骸を無視するための世代番号
  bool _disposed = false;

  ConnectionPhase get phase => _phase;

  Stream<ConnectionPhase> get phaseStream => _phaseController.stream;

  /// auth.ack 以外の全サーバーメッセージ(snapshot / update / ack / error / pong)。
  Stream<ServerMessage> get messages => _messageController.stream;

  /// 初回ペアリング成立時に発行される保存用資格情報。
  Stream<PairedRelay> get paired => _pairedController.stream;

  PairedRelay? get credentials => _credentials;

  int get reconnectAttempt => _backoff.attempt;

  /// QRコードから初回ペアリングを行う。トークンは使い捨てのため自動リトライしない。
  Future<void> startPairing(QrPayload payload) async {
    _credentials = null;
    _cancelTimers();
    _generation++;
    await _connectOnce(
      uri: payload.wsUri,
      buildAuth: (id) => ClientMessages.authWithToken(id, payload.token, _deviceName),
      pairingHost: payload.host,
      pairingPort: payload.port,
    );
  }

  /// 保存済み資格情報で接続する(失敗時はバックオフ自動再接続)。
  void startWithCredentials(PairedRelay relay) {
    _credentials = relay;
    _cancelTimers();
    _generation++;
    _backoff.reset();
    unawaited(_connectOnce(
      uri: relay.wsUri,
      buildAuth: (id) => ClientMessages.authWithCredentials(id, relay.deviceId, relay.secret),
    ));
  }

  /// アプリのバックグラウンド移行などによる明示切断。再接続はしない。
  Future<void> stop() async {
    _generation++;
    _cancelTimers();
    _setPhase(_credentials == null ? ConnectionPhase.unpaired : ConnectionPhase.disconnected);
    await _closeConnection();
  }

  /// ペアリング解除。資格情報を忘れて未ペアリング状態へ。
  Future<void> unpair() async {
    _credentials = null;
    await stop();
  }

  /// パラメータ変更要求を送る。戻り値はメッセージID(ack照合用)。未接続時は null。
  String? sendParameterSet(String parameter, Object value) {
    final connection = _connection;
    if (_phase != ConnectionPhase.connected || connection == null) return null;

    final id = _uuid.v4();
    connection.send(ClientMessages.parameterSet(id, parameter, value));
    return id;
  }

  Future<void> _connectOnce({
    required Uri uri,
    required String Function(String id) buildAuth,
    String? pairingHost,
    int? pairingPort,
  }) async {
    final generation = _generation;
    _setPhase(_backoff.attempt == 0 ? ConnectionPhase.connecting : ConnectionPhase.reconnecting);

    WsConnection connection;
    try {
      connection = await _connector.connect(uri);
    } catch (_) {
      if (generation != _generation) return;
      _handleFailure();
      return;
    }

    if (generation != _generation) {
      unawaited(connection.close());
      return;
    }

    _connection = connection;
    final authId = _uuid.v4();
    var authenticated = false;

    _subscription = connection.messages.listen(
      (raw) {
        if (generation != _generation) return;
        _resetWatchdog();

        final message = ServerMessage.parse(raw);

        if (!authenticated) {
          if (message is AuthAckMessage) {
            authenticated = true;
            _onAuthenticated(message, pairingHost, pairingPort, uri);
          } else if (message is ErrorMessage) {
            // AUTH_FAILED / PAIRING_REQUIRED: 資格情報が無効。自動リトライしない
            _messageController.add(message);
            _credentials = null;
            _generation++;
            _cancelTimers();
            unawaited(_closeConnection());
            _setPhase(ConnectionPhase.unpaired);
          }
          return;
        }

        if (message is! UnknownMessage) {
          _messageController.add(message);
        }
      },
      onDone: () {
        if (generation != _generation) return;
        _handleFailure();
      },
      onError: (Object _) {
        if (generation != _generation) return;
        _handleFailure();
      },
    );

    // 認証要求は接続後最初の1通
    connection.send(buildAuth(authId));

    // auth.ack が来ない場合のタイムアウト
    Timer(Protocol.authTimeout, () {
      if (generation != _generation || authenticated) return;
      _handleFailure();
    });
  }

  void _onAuthenticated(
      AuthAckMessage ack, String? pairingHost, int? pairingPort, Uri uri) {
    _backoff.reset();
    _setPhase(ConnectionPhase.connected);
    _startHeartbeat();

    // 初回ペアリング: 発行された資格情報を通知(保存は呼び出し側)
    if (ack.secret != null) {
      final relay = PairedRelay(
        host: pairingHost ?? uri.host,
        port: pairingPort ?? uri.port,
        deviceId: ack.deviceId,
        secret: ack.secret!,
        serverName: ack.serverName,
      );
      _credentials = relay;
      _pairedController.add(relay);
    }
  }

  void _startHeartbeat() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = Timer.periodic(Protocol.heartbeatInterval, (_) {
      _connection?.send(ClientMessages.ping(_uuid.v4()));
    });
    _resetWatchdog();
  }

  void _resetWatchdog() {
    _watchdogTimer?.cancel();
    _watchdogTimer = Timer(Protocol.disconnectTimeout, () {
      // 6秒間何も受信しなかった → 切断とみなす
      _handleFailure();
    });
  }

  /// 接続失敗・切断時の共通処理。資格情報があればバックオフ再接続、なければ未ペアリングへ。
  void _handleFailure() {
    if (_disposed) return;
    _generation++;
    _cancelTimers();
    unawaited(_closeConnection());

    final relay = _credentials;
    if (relay == null) {
      _setPhase(ConnectionPhase.unpaired);
      return;
    }

    _setPhase(ConnectionPhase.reconnecting);
    final delay = _backoff.next();
    final generation = _generation;
    _reconnectTimer = Timer(delay, () {
      if (generation != _generation) return;
      unawaited(_connectOnce(
        uri: relay.wsUri,
        buildAuth: (id) =>
            ClientMessages.authWithCredentials(id, relay.deviceId, relay.secret),
      ));
    });
  }

  void _cancelTimers() {
    _heartbeatTimer?.cancel();
    _watchdogTimer?.cancel();
    _reconnectTimer?.cancel();
    _heartbeatTimer = null;
    _watchdogTimer = null;
    _reconnectTimer = null;
  }

  Future<void> _closeConnection() async {
    final subscription = _subscription;
    final connection = _connection;
    _subscription = null;
    _connection = null;
    await subscription?.cancel();
    if (connection != null) {
      // close()の完了は待たない: リスナー解除後のStreamController.close()は
      // 完了しないことがある(Dartの仕様)ため、待つとここで固まる
      unawaited(connection.close().catchError((_) {}));
    }
  }

  void _setPhase(ConnectionPhase phase) {
    if (_phase == phase || _disposed) return;
    _phase = phase;
    _phaseController.add(phase);
  }

  Future<void> dispose() async {
    _disposed = true;
    _generation++;
    _cancelTimers();
    await _closeConnection();
    await _phaseController.close();
    await _messageController.close();
    await _pairedController.close();
  }
}
