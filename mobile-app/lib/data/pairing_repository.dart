import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../domain/pairing_info.dart';

/// 資格情報の保存先の抽象(テストではメモリ実装に差し替える)。
abstract class PairingRepository {
  Future<PairedRelay?> load();

  Future<void> save(PairedRelay relay);

  Future<void> clear();
}

/// 資格情報 → flutter_secure_storage(iOS Keychain / Android Keystore)。
class SecurePairingRepository implements PairingRepository {
  SecurePairingRepository([FlutterSecureStorage? storage])
      : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'avaphone.paired_relay';

  final FlutterSecureStorage _storage;

  @override
  Future<PairedRelay?> load() async {
    try {
      final raw = await _storage.read(key: _key);
      if (raw == null) return null;
      return PairedRelay.fromJson(raw);
    } catch (_) {
      return null; // 読めない保存データは未ペアリング扱い
    }
  }

  @override
  Future<void> save(PairedRelay relay) =>
      _storage.write(key: _key, value: relay.toJsonString());

  @override
  Future<void> clear() => _storage.delete(key: _key);
}

/// テスト・開発用のメモリ実装。
class InMemoryPairingRepository implements PairingRepository {
  PairedRelay? stored;

  @override
  Future<PairedRelay?> load() async => stored;

  @override
  Future<void> save(PairedRelay relay) async => stored = relay;

  @override
  Future<void> clear() async => stored = null;
}

/// アプリ設定(非機密)の抽象。
abstract class SettingsStore {
  bool get batteryEnabled;

  Future<void> setBatteryEnabled(bool value);

  bool get wakelockEnabled;

  Future<void> setWakelockEnabled(bool value);
}

/// shared_preferences による実装。
class PrefsSettingsStore implements SettingsStore {
  PrefsSettingsStore(this._prefs);

  static const _batteryKey = 'avaphone.battery_enabled';
  static const _wakelockKey = 'avaphone.wakelock_enabled';

  final SharedPreferencesWithCache _prefs;

  static Future<PrefsSettingsStore> load() async {
    final prefs = await SharedPreferencesWithCache.create(
      cacheOptions: const SharedPreferencesWithCacheOptions(
        allowList: {_batteryKey, _wakelockKey},
      ),
    );
    return PrefsSettingsStore(prefs);
  }

  @override
  bool get batteryEnabled => _prefs.getBool(_batteryKey) ?? true;

  @override
  Future<void> setBatteryEnabled(bool value) => _prefs.setBool(_batteryKey, value);

  @override
  bool get wakelockEnabled => _prefs.getBool(_wakelockKey) ?? false;

  @override
  Future<void> setWakelockEnabled(bool value) => _prefs.setBool(_wakelockKey, value);
}

/// テスト・開発用のメモリ実装。
class InMemorySettingsStore implements SettingsStore {
  @override
  bool batteryEnabled = true;

  @override
  bool wakelockEnabled = false;

  @override
  Future<void> setBatteryEnabled(bool value) async => batteryEnabled = value;

  @override
  Future<void> setWakelockEnabled(bool value) async => wakelockEnabled = value;
}
