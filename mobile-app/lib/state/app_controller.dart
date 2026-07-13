import 'dart:async';

import 'package:battery_plus/battery_plus.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/phone_parameters.dart';
import '../data/battery_reporter.dart';
import '../data/connection_manager.dart';
import '../data/pairing_repository.dart';
import '../data/ws_connector.dart';
import '../domain/pairing_info.dart';
import '../domain/ws_messages.dart';

/// 保留中の parameter.set(optimistic UI 用の仮表示)。
class PendingOp {
  const PendingOp(this.parameter, this.value);

  final String parameter;
  final Object? value;
}

/// UIエラー通知(SnackBar用)。seqで同一コードの連続発生も区別する。
class UiError {
  const UiError(this.seq, this.code, this.message);

  final int seq;
  final String code;
  final String message;
}

class AppState {
  const AppState({
    this.phase = ConnectionPhase.unpaired,
    this.loaded = false,
    this.serverName,
    this.reconnectAttempt = 0,
    this.avatarId,
    this.supported = false,
    this.vrchat = 'not_found',
    this.confirmed = const {},
    this.pending = const {},
    this.lastError,
    this.batteryEnabled = true,
    this.wakelockEnabled = false,
  });

  /// 起動時の保存資格情報の読み込みが完了したか。
  final bool loaded;

  final ConnectionPhase phase;
  final String? serverName;
  final int reconnectAttempt;

  final String? avatarId;
  final bool supported;

  /// connected / not_found / osc_disabled
  final String vrchat;

  /// 確定値(state.snapshot / state.update / ack 由来)。
  final Map<String, Object?> confirmed;

  /// msgId → 保留操作。
  final Map<String, PendingOp> pending;

  final UiError? lastError;

  final bool batteryEnabled;
  final bool wakelockEnabled;

  /// 確定値+保留操作のオーバーレイ(UIはこれだけを見る)。
  Map<String, Object?> get effective => {
        ...confirmed,
        for (final op in pending.values) op.parameter: op.value,
      };

  /// 操作可能か(接続済み・VRChat検出済み・対応アバター)。
  bool get operable =>
      phase == ConnectionPhase.connected && supported && vrchat == 'connected';

  AppState copyWith({
    bool? loaded,
    ConnectionPhase? phase,
    String? serverName,
    int? reconnectAttempt,
    String? avatarId,
    bool? avatarIdToNull,
    bool? supported,
    String? vrchat,
    Map<String, Object?>? confirmed,
    Map<String, PendingOp>? pending,
    UiError? lastError,
    bool? batteryEnabled,
    bool? wakelockEnabled,
  }) =>
      AppState(
        loaded: loaded ?? this.loaded,
        phase: phase ?? this.phase,
        serverName: serverName ?? this.serverName,
        reconnectAttempt: reconnectAttempt ?? this.reconnectAttempt,
        avatarId: (avatarIdToNull ?? false) ? null : (avatarId ?? this.avatarId),
        supported: supported ?? this.supported,
        vrchat: vrchat ?? this.vrchat,
        confirmed: confirmed ?? this.confirmed,
        pending: pending ?? this.pending,
        lastError: lastError ?? this.lastError,
        batteryEnabled: batteryEnabled ?? this.batteryEnabled,
        wakelockEnabled: wakelockEnabled ?? this.wakelockEnabled,
      );
}

// ---- DI用プロバイダ(テストで差し替える) ----

final wsConnectorProvider = Provider<WsConnector>((_) => RealWsConnector());

final pairingRepositoryProvider =
    Provider<PairingRepository>((_) => SecurePairingRepository());

final appSettingsProvider =
    FutureProvider<SettingsStore>((_) => PrefsSettingsStore.load());

/// バッテリー残量%の取得(テストで差し替える)。
final batteryReaderProvider = Provider<Future<int> Function()>((_) {
  final battery = Battery();
  return () => battery.batteryLevel;
});

/// 充電状態変化のストリーム(テストで差し替える)。
final batteryChangesProvider =
    Provider<Stream<Object?>>((_) => Battery().onBatteryStateChanged);

final connectionManagerProvider = Provider<ConnectionManager>((ref) {
  final manager = ConnectionManager(connector: ref.watch(wsConnectorProvider));
  ref.onDispose(() => manager.dispose());
  return manager;
});

final appControllerProvider = NotifierProvider<AppController, AppState>(AppController.new);

/// 保留操作のローカルタイムアウト(ackが来ない場合に仮表示を破棄)。
const pendingOpTimeout = Duration(seconds: 3);

/// アプリ全体の状態機械。ConnectionManagerのストリームを購読し、
/// 確定状態+保留操作(optimistic UI)を1つのAppStateに束ねる。
class AppController extends Notifier<AppState> {
  final _subscriptions = <StreamSubscription<Object?>>[];
  final _pendingTimers = <String, Timer>{};
  BatteryReporter? _battery;
  int _errorSeq = 0;

  ConnectionManager get _manager => ref.read(connectionManagerProvider);

  @override
  AppState build() {
    final manager = ref.watch(connectionManagerProvider);

    _subscriptions.add(manager.phaseStream.listen(_onPhase));
    _subscriptions.add(manager.messages.listen(_onMessage));
    _subscriptions.add(manager.paired.listen(_onPaired));
    ref.onDispose(() {
      for (final s in _subscriptions) {
        s.cancel();
      }
      _subscriptions.clear();
      for (final t in _pendingTimers.values) {
        t.cancel();
      }
      _pendingTimers.clear();
      _battery?.stop();
      _battery = null;
    });

    unawaited(_initialize(manager));

    return const AppState();
  }

  /// 起動処理: 設定と保存済み資格情報を読み、あれば自動接続。
  Future<void> _initialize(ConnectionManager manager) async {
    final settings = await ref.read(appSettingsProvider.future);
    final stored = await ref.read(pairingRepositoryProvider).load();
    state = state.copyWith(
      loaded: true,
      batteryEnabled: settings.batteryEnabled,
      wakelockEnabled: settings.wakelockEnabled,
      serverName: stored?.serverName,
    );
    if (stored != null) {
      manager.startWithCredentials(stored);
    }
  }

  // ---- ユーザー操作 ----

  /// QRコード(または手動入力)からペアリングする。
  Future<void> pairWith(QrPayload payload) => _manager.startPairing(payload);

  /// ペアリング解除(資格情報を破棄)。
  Future<void> unpair() async {
    await ref.read(pairingRepositoryProvider).clear();
    await _manager.unpair();
    state = state.copyWith(serverName: null, confirmed: const {}, pending: const {});
  }

  /// パラメータ変更(仮表示登録+送信)。falseは未送信(未接続・操作不可)。
  bool setParameter(String parameter, Object value) {
    if (!state.operable) return false;

    final id = _manager.sendParameterSet(parameter, value);
    if (id == null) return false;

    state = state.copyWith(pending: {
      ...state.pending,
      id: PendingOp(parameter, value),
    });

    // ローカルタイムアウト: ackが来なければ仮表示を破棄(確定値へ戻る)
    _pendingTimers[id] = Timer(pendingOpTimeout, () {
      _pendingTimers.remove(id);
      _removePending(id);
    });
    return true;
  }

  /// 通知演出(EventToggleの現在値を反転して送る)。
  bool triggerNotify() {
    final current = state.effective[PhoneParameters.eventToggle];
    return setParameter(PhoneParameters.eventToggle, !(current == true));
  }

  Future<void> setBatteryEnabled(bool value) async {
    state = state.copyWith(batteryEnabled: value);
    _syncBatteryReporter();
    final settings = await ref.read(appSettingsProvider.future);
    await settings.setBatteryEnabled(value);
  }

  Future<void> setWakelockEnabled(bool value) async {
    state = state.copyWith(wakelockEnabled: value);
    final settings = await ref.read(appSettingsProvider.future);
    await settings.setWakelockEnabled(value);
  }

  /// バッテリー段階の送信(BatteryReporterから呼ばれる)。
  void reportBattery(int step) {
    if (!state.batteryEnabled) return;
    final current = state.confirmed[PhoneParameters.battery];
    if (current == step) return; // 変化なしは送らない
    setParameter(PhoneParameters.battery, step);
  }

  /// ライフサイクル: バックグラウンド移行時。
  Future<void> onAppPaused() => _manager.stop();

  /// ライフサイクル: フォアグラウンド復帰時。
  void onAppResumed() {
    final credentials = _manager.credentials;
    if (credentials != null && state.phase != ConnectionPhase.connected) {
      _manager.startWithCredentials(credentials);
    }
  }

  // ---- 内部: ストリームハンドラ ----

  void _onPhase(ConnectionPhase phase) {
    state = state.copyWith(
      phase: phase,
      reconnectAttempt: _manager.reconnectAttempt,
      // 切断中は保留操作を破棄
      pending: phase == ConnectionPhase.connected ? null : const {},
    );
    _syncBatteryReporter();
  }

  /// 接続中かつ設定ONのときだけバッテリー送信を動かす。
  void _syncBatteryReporter() {
    final want = state.phase == ConnectionPhase.connected && state.batteryEnabled;
    if (want && _battery == null) {
      _battery = BatteryReporter(
        readPercent: ref.read(batteryReaderProvider),
        chargingChanges: ref.read(batteryChangesProvider),
        onStep: reportBattery,
      )..start();
    } else if (!want && _battery != null) {
      _battery!.stop();
      _battery = null;
    }
  }

  void _onPaired(PairedRelay relay) {
    state = state.copyWith(serverName: relay.serverName);
    unawaited(ref.read(pairingRepositoryProvider).save(relay));
  }

  void _onMessage(ServerMessage message) {
    switch (message) {
      case StateSnapshotMessage(:final avatarId, :final supported, :final vrchat, :final parameters):
        state = state.copyWith(
          avatarId: avatarId,
          avatarIdToNull: avatarId == null,
          supported: supported,
          vrchat: vrchat,
          confirmed: Map<String, Object?>.from(parameters),
        );
        // アバター変更・再接続後にバッテリー段階を再同期
        if (state.operable) _battery?.refresh();

      case StateUpdateMessage(:final parameter, :final value):
        state = state.copyWith(confirmed: {...state.confirmed, parameter: value});

      case ParameterAckMessage(:final id, :final parameter, :final value, :final applied):
        if (applied) {
          state = state.copyWith(confirmed: {...state.confirmed, parameter: value});
        }
        _removePending(id);

      case ErrorMessage(:final id, :final code, message: final msg):
        _removePending(id);
        state = state.copyWith(lastError: UiError(++_errorSeq, code, msg));

      case AuthAckMessage() || PongMessage() || UnknownMessage():
        break;
    }
  }

  void _removePending(String id) {
    if (!state.pending.containsKey(id)) return;
    final pending = {...state.pending}..remove(id);
    state = state.copyWith(pending: pending);
  }
}
