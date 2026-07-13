import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../core/protocol.dart';
import '../../data/connection_manager.dart';
import '../../domain/pairing_info.dart';
import '../../state/app_controller.dart';

/// 初回ペアリング画面: QRスキャン(モバイルのみ)+手動入力フォールバック。
class PairingScreen extends ConsumerStatefulWidget {
  const PairingScreen({super.key});

  @override
  ConsumerState<PairingScreen> createState() => _PairingScreenState();
}

class _PairingScreenState extends ConsumerState<PairingScreen> {
  final _hostController = TextEditingController();
  final _portController = TextEditingController(text: '${Protocol.defaultWsPort}');
  final _tokenController = TextEditingController();
  final _jsonController = TextEditingController();
  String? _errorText;

  bool get _canScan => Platform.isAndroid || Platform.isIOS;

  @override
  void dispose() {
    _hostController.dispose();
    _portController.dispose();
    _tokenController.dispose();
    _jsonController.dispose();
    super.dispose();
  }

  Future<void> _pair(QrPayload payload) async {
    setState(() => _errorText = null);
    await ref.read(appControllerProvider.notifier).pairWith(payload);
  }

  void _pairFromJson() {
    final payload = QrPayload.tryParse(_jsonController.text.trim());
    if (payload == null) {
      setState(() => _errorText = 'QRコードのJSONを解釈できません(プロトコル版の不一致の可能性)');
      return;
    }
    _pair(payload);
  }

  void _pairFromFields() {
    final port = int.tryParse(_portController.text.trim());
    final host = _hostController.text.trim();
    final token = _tokenController.text.trim();
    if (host.isEmpty || token.isEmpty || port == null || port <= 0 || port > 65535) {
      setState(() => _errorText = 'ホスト・ポート・トークンを正しく入力してください');
      return;
    }
    _pair(QrPayload(host: host, port: port, token: token));
  }

  Future<void> _openScanner() async {
    final raw = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const _ScannerPage()),
    );
    if (raw == null || !mounted) return;

    final payload = QrPayload.tryParse(raw);
    if (payload == null) {
      setState(() => _errorText = '読み取ったQRコードはAvaPhoneのものではありません');
      return;
    }
    await _pair(payload);
  }

  @override
  Widget build(BuildContext context) {
    final app = ref.watch(appControllerProvider);
    final connecting = app.phase == ConnectionPhase.connecting ||
        app.phase == ConnectionPhase.reconnecting;

    // ペアリング失敗はAppState.lastErrorに届く
    ref.listen(appControllerProvider.select((s) => s.lastError), (prev, next) {
      if (next != null && next.seq != prev?.seq) {
        setState(() => _errorText = '${next.code}: ${next.message}');
      }
    });

    return Scaffold(
      appBar: AppBar(title: const Text('AvaPhone - ペアリング')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text('PC中継アプリを起動し、表示されたQRコードを読み取ってください。',
                style: TextStyle(fontSize: 16)),
            const SizedBox(height: 16),
            if (_canScan)
              FilledButton.icon(
                onPressed: connecting ? null : _openScanner,
                icon: const Icon(Icons.qr_code_scanner),
                label: const Text('QRコードをスキャン'),
              ),
            if (connecting) ...[
              const SizedBox(height: 16),
              const Center(child: CircularProgressIndicator()),
              const SizedBox(height: 8),
              const Center(child: Text('接続中…')),
            ],
            if (_errorText != null) ...[
              const SizedBox(height: 12),
              Text(_errorText!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const Divider(height: 40),
            Text('手動入力', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            TextField(
              controller: _jsonController,
              decoration: const InputDecoration(
                labelText: 'QRコードのJSONを貼り付け',
                hintText: '{"protocol":1,"host":"192.168.1.10","port":27810,"token":"..."}',
                border: OutlineInputBorder(),
              ),
              maxLines: 2,
            ),
            const SizedBox(height: 8),
            OutlinedButton(
              onPressed: connecting ? null : _pairFromJson,
              child: const Text('JSONで接続'),
            ),
            const SizedBox(height: 16),
            Row(children: [
              Expanded(
                flex: 3,
                child: TextField(
                  controller: _hostController,
                  decoration: const InputDecoration(
                      labelText: 'ホスト', hintText: '192.168.1.10', border: OutlineInputBorder()),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                flex: 2,
                child: TextField(
                  controller: _portController,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'ポート', border: OutlineInputBorder()),
                ),
              ),
            ]),
            const SizedBox(height: 8),
            TextField(
              controller: _tokenController,
              decoration: const InputDecoration(labelText: 'トークン', border: OutlineInputBorder()),
            ),
            const SizedBox(height: 8),
            OutlinedButton(
              onPressed: connecting ? null : _pairFromFields,
              child: const Text('接続'),
            ),
          ],
        ),
      ),
    );
  }
}

/// QRスキャナ(mobile_scanner はモバイルのみ対応)。
class _ScannerPage extends StatefulWidget {
  const _ScannerPage();

  @override
  State<_ScannerPage> createState() => _ScannerPageState();
}

class _ScannerPageState extends State<_ScannerPage> {
  bool _done = false;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('QRコードを読み取り')),
      body: MobileScanner(
        onDetect: (capture) {
          if (_done) return;
          final value = capture.barcodes.firstOrNull?.rawValue;
          if (value == null) return;
          _done = true;
          Navigator.of(context).pop(value);
        },
      ),
    );
  }
}
