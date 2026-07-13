# AvaPhone - スマートフォンアプリ

VRChatアバターのスマホギミックを操作するコンパニオンアプリ(Flutter / iOS・Android)。
プロトコルの正は [docs/protocol.md](../docs/protocol.md)。

## 使い方(開発)

```powershell
flutter pub get
flutter test          # 単体+widgetテスト(実機・エミュレータ不要)
flutter run           # 接続したデバイスで実行
```

- **Windowsデスクトップターゲット**は開発用デバッグ環境(QRスキャン以外の全機能が動く。
  手動入力でペアリング可能)。ビルドにはWindowsの開発者モード(symlink許可)と
  Visual Studio C++ツールチェーンが必要
- **Androidエミュレータ**からPC上の中継アプリへは `10.0.2.2:27810` で接続(手動入力)
- **Android実機**は同一LAN+Windowsファイアウォールで 27810/TCP の受信許可が必要
- **iOS** はビルドにmacOSが必要。初回接続時にローカルネットワーク許可プロンプトが出る
  (初回接続は失敗しうるが自動再接続が吸収する)。検証は実機推奨

## 構成

```
lib/
├── core/            プロトコル定数・パラメータ定義(docs/protocol.mdと一致)
├── domain/          メッセージ型(sealed class)・QRペイロード・保存資格情報
├── data/            WebSocket接続抽象・ConnectionManager(状態機械)・
│                    永続化(flutter_secure_storage / shared_preferences)・バッテリー
├── state/           AppController(Riverpod Notifier。確定状態+保留操作=optimistic UI)
└── ui/screens/      ペアリング(QR+手動入力)/ メイン操作 / 設定
```

### 設計の要点

- **状態の正本はサーバー(=VRChatエコー)**: 操作は `parameter.set` 送信と同時に保留登録して
  仮表示し、`parameter.ack(applied)` で確定、ackが3秒来なければ仮表示を破棄して確定値へ戻す
- **フォアグラウンド前提**: pause時に明示切断、resume時に即再接続(iOSのバックグラウンド
  WebSocket維持は不可のため)
- **自動再接続**: バックオフ 1, 2, 5, 10, 以降10秒。認証失敗(AUTH_FAILED/PAIRING_REQUIRED)
  時はリトライせず未ペアリングへ
- **バッテリー連携**: 30秒周期+充電状態変化時に0-10段階へ変換して送信(前回と同値はスキップ、
  設定でOFF可)

## テスト

- `test/unit/` — バックオフ系列・メッセージcodec・バッテリー変換・接続状態機械
  (fake_asyncでハートビート2秒/切断判定6秒/バックオフを時間制御して検証)
- `test/widget/` — 偽WebSocketでアプリ全体を起動し、ペアリング画面分岐・操作UI無効化・
  optimistic UIの仮表示→確定/ロールバック・EventToggle反転・バッテリー自動送信を検証
