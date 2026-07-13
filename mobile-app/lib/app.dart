import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

import 'data/connection_manager.dart';
import 'state/app_controller.dart';
import 'ui/screens/main_screen.dart';
import 'ui/screens/pairing_screen.dart';

class AvaPhoneApp extends ConsumerStatefulWidget {
  const AvaPhoneApp({super.key});

  @override
  ConsumerState<AvaPhoneApp> createState() => _AvaPhoneAppState();
}

class _AvaPhoneAppState extends ConsumerState<AvaPhoneApp>
    with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// フォアグラウンド前提の設計: pause時に明示切断、resume時に即再接続。
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final controller = ref.read(appControllerProvider.notifier);
    switch (state) {
      case AppLifecycleState.paused:
        controller.onAppPaused();
      case AppLifecycleState.resumed:
        controller.onAppResumed();
      default:
        break;
    }
  }

  @override
  Widget build(BuildContext context) {
    final app = ref.watch(appControllerProvider);

    // 画面スリープ防止(設定に追従)。プラグイン未対応環境(テスト等)では無視
    unawaited(
        WakelockPlus.toggle(enable: app.wakelockEnabled).catchError((_) {}));

    return MaterialApp(
      title: 'AvaPhone',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF7C4DFF),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      home: !app.loaded
          ? const Scaffold(body: Center(child: CircularProgressIndicator()))
          : app.phase == ConnectionPhase.unpaired
              ? const PairingScreen()
              : const MainScreen(),
    );
  }
}
