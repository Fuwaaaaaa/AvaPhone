import 'dart:async';

import '../core/phone_parameters.dart';

/// バッテリー残量の定期取得と11段階変換。
/// battery_plusへの依存は関数注入で切り離す(テスト可能性のため)。
class BatteryReporter {
  BatteryReporter({
    required Future<int> Function() readPercent,
    Stream<Object?>? chargingChanges,
    required void Function(int step) onStep,
    this.interval = const Duration(seconds: 30),
  })  : _readPercent = readPercent,
        _chargingChanges = chargingChanges,
        _onStep = onStep;

  final Future<int> Function() _readPercent;
  final Stream<Object?>? _chargingChanges;
  final void Function(int step) _onStep;
  final Duration interval;

  Timer? _timer;
  StreamSubscription<Object?>? _subscription;

  void start() {
    stop();
    unawaited(_tick());
    _timer = Timer.periodic(interval, (_) => unawaited(_tick()));
    // 充電状態の変化時は即時更新
    _subscription = _chargingChanges?.listen((_) => unawaited(_tick()));
  }

  /// 即時に1回取得・送信する(アバター変更・snapshot受信時の再同期用)。
  void refresh() => unawaited(_tick());

  Future<void> _tick() async {
    try {
      final percent = await _readPercent();
      _onStep(PhoneParameters.batteryLevelToStep(percent));
    } catch (_) {
      // 取得失敗は無視(次の周期で再試行)
    }
  }

  void stop() {
    _timer?.cancel();
    _timer = null;
    _subscription?.cancel();
    _subscription = null;
  }
}
