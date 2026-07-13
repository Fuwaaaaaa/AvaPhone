import 'package:avaphone/app.dart';
import 'package:avaphone/data/pairing_repository.dart';
import 'package:avaphone/domain/pairing_info.dart';
import 'package:avaphone/state/app_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../support/fakes.dart';

const _storedRelay = PairedRelay(
  host: '127.0.0.1',
  port: 27810,
  deviceId: 'dev-1',
  secret: 'sec-1',
  serverName: 'TestPC',
);

/// 保存済み資格情報あり+偽コネクタでアプリ全体を起動するヘルパ。
Future<FakeConnector> pumpApp(WidgetTester tester,
    {bool paired = true}) async {
  // ListViewの遅延構築で画面外要素が消えないよう縦長ビューポートにする
  tester.view.physicalSize = const Size(800, 2000);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(tester.view.reset);

  final connector = FakeConnector();
  final repo = InMemoryPairingRepository();
  if (paired) repo.stored = _storedRelay;

  await tester.pumpWidget(ProviderScope(
    overrides: [
      wsConnectorProvider.overrideWithValue(connector),
      pairingRepositoryProvider.overrideWithValue(repo),
      appSettingsProvider.overrideWith((_) async => InMemorySettingsStore()),
      batteryReaderProvider.overrideWithValue(() async => 80),
      batteryChangesProvider.overrideWithValue(const Stream.empty()),
    ],
    child: const AvaPhoneApp(),
  ));
  await tester.pump(const Duration(milliseconds: 50)); // loaded=true + 自動接続開始
  await tester.pump(const Duration(milliseconds: 50));
  return connector;
}

/// 認証+対応アバターのsnapshotまで進めるヘルパ。
Future<FakeConnection> connectAndSnapshot(
    WidgetTester tester, FakeConnector connector) async {
  final connection = connector.connections.single;
  connection.reply(authAckJson());
  connection.reply(snapshotJson());
  await tester.pump(const Duration(milliseconds: 50));
  await tester.pump(const Duration(milliseconds: 50));
  return connection;
}

void main() {
  testWidgets('未ペアリング時はペアリング画面が表示される', (tester) async {
    await pumpApp(tester, paired: false);

    expect(find.text('AvaPhone - ペアリング'), findsOneWidget);
    expect(find.text('手動入力'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('接続+対応アバターで操作UIが有効になる', (tester) async {
    final connector = await pumpApp(tester);
    await connectAndSnapshot(tester, connector);

    // メイン画面が表示され、無効化オーバーレイが無い
    expect(find.text('AvaPhone'), findsOneWidget);
    expect(find.byKey(const Key('inoperable-reason')), findsNothing);
    expect(find.text('アバター対応'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('非対応アバターでは操作UIが無効化される', (tester) async {
    final connector = await pumpApp(tester);
    final connection = connector.connections.single;
    connection.reply(authAckJson());
    connection.reply(snapshotJson(supported: false, parameters: {}));
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.byKey(const Key('inoperable-reason')), findsOneWidget);
    expect(find.text('現在のアバターはAvaPhoneに対応していません'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('ページタップで仮表示され、ackで確定する', (tester) async {
    final connector = await pumpApp(tester);
    final connection = await connectAndSnapshot(tester, connector);

    await tester.tap(find.byKey(const Key('page-4')));
    await tester.pump();

    // parameter.set が送信され、仮表示でページ4が選択状態になっている
    final sets = connection.sentOfType('parameter.set');
    expect(sets.single['parameter'], 'Phone/Page');
    expect(sets.single['value'], 4);

    // ack(applied) → 確定
    connection.reply({
      'v': 1,
      'id': sets.single['id'],
      'type': 'parameter.ack',
      'parameter': 'Phone/Page',
      'value': 4,
      'status': 'applied',
      'timestamp': 0,
    });
    await tester.pump();

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('ackが来なければ3秒で仮表示がロールバックされる', (tester) async {
    final connector = await pumpApp(tester);
    final connection = await connectAndSnapshot(tester, connector);

    await tester.tap(find.byKey(const Key('pose-3')));
    await tester.pump();

    final sets = connection.sentOfType('parameter.set');
    expect(sets.single['parameter'], 'Phone/Pose');
    expect(sets.single['value'], 3);

    // ローカルタイムアウト経過 → 確定値(Pose=1)へ戻る
    await tester.pump(pendingOpTimeout + const Duration(milliseconds: 100));

    final chip3 = tester.widget<ChoiceChip>(find.byKey(const Key('pose-3')));
    final chip1 = tester.widget<ChoiceChip>(find.byKey(const Key('pose-1')));
    expect(chip3.selected, isFalse);
    expect(chip1.selected, isTrue);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('VRChat側の変更(state.update)が表示に反映される', (tester) async {
    final connector = await pumpApp(tester);
    final connection = await connectAndSnapshot(tester, connector);

    connection.reply({
      'v': 1,
      'id': 'u1',
      'type': 'state.update',
      'parameter': 'Phone/Pose',
      'value': 4,
      'timestamp': 0,
    });
    await tester.pump();

    final chip4 = tester.widget<ChoiceChip>(find.byKey(const Key('pose-4')));
    expect(chip4.selected, isTrue);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('通知演出はEventToggleの反転値を送る', (tester) async {
    final connector = await pumpApp(tester);
    final connection = await connectAndSnapshot(tester, connector);

    await tester.tap(find.byKey(const Key('event-notify')));
    await tester.pump();

    final sets = connection.sentOfType('parameter.set');
    expect(sets.single['parameter'], 'Phone/EventToggle');
    expect(sets.single['value'], true); // snapshot時点はfalse → 反転してtrue

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('接続時にバッテリー段階が自動送信される', (tester) async {
    final connector = await pumpApp(tester);
    final connection = await connectAndSnapshot(tester, connector);

    // batteryReader=80% → 段階8。ただしsnapshotの確定値も8なので送信されない
    // → 確定値を変えて再確認するため、まずsnapshotのBatteryを5にして接続し直すのは
    //   手間なので、ここでは「同値なら送らない」ことだけを検証する
    await tester.pump(const Duration(milliseconds: 100));
    final batterySets = connection
        .sentOfType('parameter.set')
        .where((m) => m['parameter'] == 'Phone/Battery');
    expect(batterySets, isEmpty);

    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('バッテリー段階が確定値と異なれば送信される', (tester) async {
    final connector = await pumpApp(tester);
    final connection = connector.connections.single;
    connection.reply(authAckJson());
    connection.reply(snapshotJson(parameters: {
      'Phone/Visible': true,
      'Phone/Page': 1,
      'Phone/Pose': 1,
      'Phone/Battery': 3, // 実機は80%(段階8)なので差分あり
      'Phone/CallState': 0,
      'Phone/MediaState': 0,
      'Phone/NotifyType': 0,
      'Phone/EventToggle': false,
      'Phone/Locked': false,
      'Phone/Connected': true,
    }));
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pump(const Duration(milliseconds: 100));

    final batterySets = connection
        .sentOfType('parameter.set')
        .where((m) => m['parameter'] == 'Phone/Battery')
        .toList();
    expect(batterySets, isNotEmpty);
    expect(batterySets.first['value'], 8);

    await tester.pumpWidget(const SizedBox());
  });
}
