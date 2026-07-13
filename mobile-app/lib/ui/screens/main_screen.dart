import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/phone_parameters.dart';
import '../../data/connection_manager.dart';
import '../../state/app_controller.dart';
import 'settings_screen.dart';

/// メイン操作画面。
class MainScreen extends ConsumerWidget {
  const MainScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final app = ref.watch(appControllerProvider);
    final controller = ref.read(appControllerProvider.notifier);
    final effective = app.effective;

    // エラーをSnackBarで通知
    ref.listen(appControllerProvider.select((s) => s.lastError), (prev, next) {
      if (next != null && next.seq != prev?.seq) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('${next.code}: ${next.message}')),
        );
      }
    });

    return Scaffold(
      appBar: AppBar(
        title: const Text('AvaPhone'),
        actions: [
          IconButton(
            icon: const Icon(Icons.settings),
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute(builder: (_) => const SettingsScreen()),
            ),
          ),
        ],
      ),
      body: Column(children: [
        _StatusHeader(app: app),
        if (app.phase == ConnectionPhase.reconnecting)
          MaterialBanner(
            content: Text('再接続中… (${app.reconnectAttempt}回目)'),
            leading: const Icon(Icons.sync),
            actions: const [SizedBox.shrink()],
          ),
        Expanded(
          child: Stack(children: [
            ListView(
              padding: const EdgeInsets.all(16),
              children: [
                _section(context, 'スマートフォン'),
                _VisibleControls(effective: effective, controller: controller),
                const SizedBox(height: 16),
                _section(context, '画面'),
                _PageGrid(effective: effective, controller: controller),
                const SizedBox(height: 16),
                _section(context, 'ポーズ'),
                _PoseChips(effective: effective, controller: controller),
                const SizedBox(height: 16),
                _section(context, 'イベント'),
                _EventButtons(effective: effective, controller: controller),
                const SizedBox(height: 32),
              ],
            ),
            if (!app.operable)
              Positioned.fill(
                child: ColoredBox(
                  color: Colors.black54,
                  child: Center(
                    child: Card(
                      child: Padding(
                        padding: const EdgeInsets.all(24),
                        child: Text(
                          _inoperableReason(app),
                          key: const Key('inoperable-reason'),
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
          ]),
        ),
      ]),
    );
  }

  static String _inoperableReason(AppState app) {
    if (app.phase != ConnectionPhase.connected) return 'PC中継アプリに接続していません';
    if (app.vrchat == 'osc_disabled') return 'VRChatのOSCが無効です\n(Action Menu → OSC → Enabled)';
    if (app.vrchat != 'connected') return 'VRChatを検出できません';
    if (!app.supported) return '現在のアバターはAvaPhoneに対応していません';
    return '';
  }

  Widget _section(BuildContext context, String title) => Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Text(title, style: Theme.of(context).textTheme.titleMedium),
      );
}

class _StatusHeader extends StatelessWidget {
  const _StatusHeader({required this.app});

  final AppState app;

  @override
  Widget build(BuildContext context) {
    Widget chip(String label, bool ok, {String? warnLabel}) => Chip(
          avatar: Icon(ok ? Icons.check_circle : Icons.cancel,
              size: 18, color: ok ? Colors.greenAccent : Colors.redAccent),
          label: Text(ok ? label : (warnLabel ?? label)),
          visualDensity: VisualDensity.compact,
        );

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      child: Wrap(spacing: 8, runSpacing: 4, children: [
        chip('PC: ${app.serverName ?? "接続済み"}',
            app.phase == ConnectionPhase.connected,
            warnLabel: 'PC未接続'),
        chip('VRChat', app.vrchat == 'connected',
            warnLabel: app.vrchat == 'osc_disabled' ? 'OSC無効' : 'VRChat未検出'),
        chip(
          app.avatarId == null
              ? 'アバター未検出'
              : 'アバター${app.supported ? "対応" : "非対応"}',
          app.supported,
        ),
      ]),
    );
  }
}

class _VisibleControls extends StatelessWidget {
  const _VisibleControls({required this.effective, required this.controller});

  final Map<String, Object?> effective;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final visible = effective[PhoneParameters.visible] == true;
    final locked = effective[PhoneParameters.locked] == true;

    return Column(children: [
      SegmentedButton<bool>(
        segments: const [
          ButtonSegment(value: true, label: Text('表示'), icon: Icon(Icons.smartphone)),
          ButtonSegment(value: false, label: Text('収納'), icon: Icon(Icons.close_fullscreen)),
        ],
        selected: {visible},
        onSelectionChanged: (selection) =>
            controller.setParameter(PhoneParameters.visible, selection.first),
      ),
      SwitchListTile(
        title: const Text('ロック'),
        value: locked,
        onChanged: (v) => controller.setParameter(PhoneParameters.locked, v),
      ),
    ]);
  }
}

class _PageGrid extends StatelessWidget {
  const _PageGrid({required this.effective, required this.controller});

  final Map<String, Object?> effective;
  final AppController controller;

  static const _icons = [
    Icons.lock, Icons.home, Icons.notifications, Icons.call,
    Icons.photo_camera, Icons.music_note, Icons.settings, Icons.error_outline,
  ];

  @override
  Widget build(BuildContext context) {
    final current = effective[PhoneParameters.page];

    return GridView.count(
      crossAxisCount: 4,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 8,
      crossAxisSpacing: 8,
      children: [
        for (var i = 0; i < PhoneParameters.pageNames.length; i++)
          _PageTile(
            index: i,
            icon: _icons[i],
            label: PhoneParameters.pageNames[i],
            selected: current == i,
            onTap: () => controller.setParameter(PhoneParameters.page, i),
          ),
      ],
    );
  }
}

class _PageTile extends StatelessWidget {
  const _PageTile({
    required this.index,
    required this.icon,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final int index;
  final IconData icon;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: selected ? scheme.primaryContainer : scheme.surfaceContainerHighest,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        key: Key('page-$index'),
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
          Icon(icon, size: 28),
          const SizedBox(height: 4),
          Text(label, style: const TextStyle(fontSize: 11)),
        ]),
      ),
    );
  }
}

class _PoseChips extends StatelessWidget {
  const _PoseChips({required this.effective, required this.controller});

  final Map<String, Object?> effective;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final current = effective[PhoneParameters.pose];

    return Wrap(spacing: 8, runSpacing: 4, children: [
      for (var i = 0; i < PhoneParameters.poseNames.length; i++)
        ChoiceChip(
          key: Key('pose-$i'),
          label: Text(PhoneParameters.poseNames[i]),
          selected: current == i,
          onSelected: (_) => controller.setParameter(PhoneParameters.pose, i),
        ),
    ]);
  }
}

class _EventButtons extends StatelessWidget {
  const _EventButtons({required this.effective, required this.controller});

  final Map<String, Object?> effective;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final call = effective[PhoneParameters.callState];
    final media = effective[PhoneParameters.mediaState];

    Widget button(String label, IconData icon, VoidCallback onPressed, {Key? key}) =>
        OutlinedButton.icon(
            key: key, onPressed: onPressed, icon: Icon(icon, size: 18), label: Text(label));

    return Wrap(spacing: 8, runSpacing: 8, children: [
      button('通知演出', Icons.notifications_active, () => controller.triggerNotify(),
          key: const Key('event-notify')),
      button('着信開始', Icons.ring_volume,
          () => controller.setParameter(PhoneParameters.callState, 1)),
      button(call == 3 ? '通話終了' : '通話開始', Icons.call,
          () => controller.setParameter(PhoneParameters.callState, call == 3 ? 0 : 3)),
      button(media == 1 ? '一時停止' : 'メディア再生', Icons.play_circle,
          () => controller.setParameter(PhoneParameters.mediaState, media == 1 ? 2 : 1)),
      button('メディア停止', Icons.stop_circle,
          () => controller.setParameter(PhoneParameters.mediaState, 0)),
    ]);
  }
}
