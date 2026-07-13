import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../state/app_controller.dart';

class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final app = ref.watch(appControllerProvider);
    final controller = ref.read(appControllerProvider.notifier);

    return Scaffold(
      appBar: AppBar(title: const Text('設定')),
      body: ListView(children: [
        SwitchListTile(
          title: const Text('バッテリー連携'),
          subtitle: const Text('実機のバッテリー残量をアバターへ11段階で送信します'),
          value: app.batteryEnabled,
          onChanged: (v) => controller.setBatteryEnabled(v),
        ),
        SwitchListTile(
          title: const Text('画面スリープ防止'),
          subtitle: const Text('操作中に画面が消灯しないようにします'),
          value: app.wakelockEnabled,
          onChanged: (v) => controller.setWakelockEnabled(v),
        ),
        const Divider(),
        ListTile(
          title: const Text('ペアリング先'),
          subtitle: Text(app.serverName ?? '(未接続)'),
        ),
        ListTile(
          title: Text('ペアリング解除',
              style: TextStyle(color: Theme.of(context).colorScheme.error)),
          subtitle: const Text('保存された認証情報を削除します'),
          onTap: () async {
            final confirmed = await showDialog<bool>(
              context: context,
              builder: (dialogContext) => AlertDialog(
                title: const Text('ペアリング解除'),
                content: const Text('認証情報を削除します。再接続にはQRコードの再読み取りが必要です。'),
                actions: [
                  TextButton(
                      onPressed: () => Navigator.pop(dialogContext, false),
                      child: const Text('キャンセル')),
                  FilledButton(
                      onPressed: () => Navigator.pop(dialogContext, true),
                      child: const Text('解除する')),
                ],
              ),
            );
            if (confirmed == true) {
              await controller.unpair();
              if (context.mounted) Navigator.of(context).pop();
            }
          },
        ),
        const Divider(),
        const AboutListTile(
          applicationName: 'AvaPhone',
          applicationVersion: '0.1.0',
          child: Text('このアプリについて'),
        ),
      ]),
    );
  }
}
