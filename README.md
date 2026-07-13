# AvaPhone(アバフォン)

**日本語** | [English](README.en.md)

実物のスマートフォンから、VRChatアバターに搭載したスマートフォンギミックを操作するシステムです。
手元のスマホで画面を切り替えると、アバターのスマホも同じ画面に切り替わります。バッテリー残量も同期します。

> 旧仮称: Avatar Smartphone Link(基本仕様書 v0.1 時点の名称)

```
実スマートフォン(Flutter製アプリ)
      │  WebSocket / JSON / 同一LAN
      ▼
PC中継アプリ VrcPhoneRelay(C# / .NET 8)
      │  OSC / OSCQuery(PC内ループバック)
      ▼
PC版VRChat ── Avatar Parameters ──▶ アバターFX Animator ──▶ スマホギミック
```

## できること

| 機能 | 状態 |
|---|---|
| スマホからアバター上のスマホ本体を表示/収納 | ✅ 実装済み |
| 画面切り替え(ロック/ホーム/通知/通話/カメラ/メディア/設定/接続エラーの8画面) | ✅ 実装済み |
| 保持ポーズ切り替え(腰収納/右手/左手/耳あて/自撮り/両手の6種、0.2秒ブレンド) | ✅ 実装済み |
| 実スマホのバッテリー残量をアバターに11段階表示 | ✅ 実装済み(アプリ側は開発中) |
| 通知演出・着信/通話/メディア再生の状態演出 | ✅ 実装済み |
| VRChat側(Expression Menu)での変更をスマホへ逆反映 | ✅ 実装済み |
| 他プレイヤーへの状態同期(Synced Parameters・52bit) | ✅ 実装済み |
| 切断時の安全動作(6秒で検出、通話等の一時状態をリセット) | ✅ 実装済み |
| スマートフォンアプリ(iOS / Android) | 🚧 開発中 |

### できないこと(設計上の非対応)

実スマホ画面のミラーリング、映像・写真・任意テキストの転送、通知本文の取得、通話音声連携は**行いません**。
VRChatのAvatar Parametersは数値・真偽値のみを扱えるため、番号に対応する事前作成済みの画面素材を切り替える方式です。
連絡先・メッセージ内容・位置情報などの個人情報は一切送信しません(詳細は[仕様書14章](docs/spec-v0.1.md#14-セキュリティプライバシー))。

## リポジトリ構成

| ディレクトリ | 内容 | 技術 |
|---|---|---|
| [`docs/`](docs/) | 基本仕様書 v0.1、[通信プロトコル定義(単一情報源)](docs/protocol.md)、[仕様修正記録](docs/spec-errata.md) | - |
| [`relay-app/`](relay-app/) | PC中継アプリ **VrcPhoneRelay**。WebSocketサーバー+OSC/OSCQueryクライアント | C# / .NET 8 / Kestrel |
| [`mobile-app/`](mobile-app/) | スマートフォンアプリ **AvaPhone**(開発中) | Flutter / Riverpod 3 |
| [`unity-avatar/`](unity-avatar/) | アバターギミック自動生成エディタ拡張 **AvaPhone Gimmick**(VPMパッケージ) | Unity 2022.3 / VRChat SDK 3.7+ |

## 動作要件

- **PC**: Windows(PC版VRChat + 中継アプリを実行)。VRChatのOSCを有効化(Action Menu → OSC → Enabled)
- **アバター**: Avatars 3.0。Expression Parametersの空き容量 52bit 以上
- **Unity**: 2022.3.22f1 + VRChat SDK(com.vrchat.avatars >= 3.7.0)+ VCC
- **スマートフォン**: iOS / Android(PCと同一LANに接続)

## セットアップ

### 1. アバターへのギミック導入(Unity)

1. VCC → Settings → Packages → **Add Local Package** で `unity-avatar/Packages/net.transit.phone-gimmick` を追加し、アバタープロジェクトへ導入
2. Unityメニュー **Tools → Phone Gimmick → Setup** を開き、アバターを指定して「生成 / 更新」
   - Expression Parameters(52bit)/ FX 9レイヤー / Expressions Menu「Phone」/ スマホプレースホルダー+ポーズ用アンカー5個が自動生成されます(既定は非破壊: 既存アセットは複製してから編集)
3. `PhoneAnchor_*` の位置を体格に合わせて調整し、アバターをアップロード

詳細手順・Av3Emulatorでの検証方法: [unity-avatar/docs/setup-guide.md](unity-avatar/docs/setup-guide.md)

### 2. PC中継アプリ

```powershell
# 配布ビルド(単一exe)
cd relay-app
dotnet publish src/VrcPhoneRelay.Server -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish

# 起動(または開発実行: dotnet run --project src/VrcPhoneRelay.Server)
artifacts\publish\VrcPhoneRelay.Server.exe
```

起動すると未ペアリング時はQRコードが自動表示されます。VRChatはOSCQuery(mDNS)で自動検出され、
mDNSが使えない環境では固定ポート(9000/9001)へ自動フォールバックします。

スマホ実機から接続するにはWebSocketポートの受信許可が必要です:

```powershell
netsh advfirewall firewall add rule name="AvaPhone Relay" dir=in action=allow protocol=TCP localport=27810 profile=private
```

設定・コンソールコマンド・VRChat無しでの動作確認(FakeVrchat): [relay-app/README.md](relay-app/README.md)

### 3. スマートフォンアプリ(開発中)

QRコード読み取りでペアリングし、以後は自動再接続します。iOS/Android対応(Flutter製)。

## 通信プロトコル概要

正式な定義は [docs/protocol.md](docs/protocol.md)(3実装すべてがこれに従います)。

- **トランスポート**: WebSocket(`ws://<PC>:27810/ws`)、UTF-8 JSON、ハートビート2秒/切断判定6秒
- **認証**: 初回はQRコードのワンタイムトークン(128bit・5分TTL・使い捨て)、再接続時は発行済み deviceId+secret(サーバー側はSHA-256ハッシュ保存)
- **状態の正本**: VRChatから最後に出力された値。スマホ操作は `parameter.set` → OSC送信 → VRChatエコー確認 → `parameter.ack(applied)`。1.5秒以内に確認できなければ `timeout`
- **アバターパラメータ**(接頭辞 `Phone/`、計52bit / 256bit):

| パラメータ | 型 | 値域 | 内容 |
|---|---|---|---|
| `Phone/Visible` | Bool | - | 本体表示 |
| `Phone/Connected` | Bool | - | 実スマホ接続状態(中継アプリのみが書く) |
| `Phone/Locked` | Bool | - | ロック状態 |
| `Phone/Page` | Int | 0-7 | 表示画面 |
| `Phone/Pose` | Int | 0-5 | 保持ポーズ |
| `Phone/Battery` | Int | 0-10 | バッテリー段階 |
| `Phone/CallState` | Int | 0-4 | 通話状態 |
| `Phone/MediaState` | Int | 0-4 | メディア状態 |
| `Phone/NotifyType` | Int | 0-4 | 通知演出種別 |
| `Phone/EventToggle` | Bool | - | 通知演出トリガー(反転式、1秒1回まで) |

## 開発

```powershell
# PC中継アプリ: ビルド+全テスト(VRChat不要 — FakeVrchatでE2E)
cd relay-app
dotnet test        # 110テスト(ユニット+E2E統合)

# VRChat偽装ツールを使った手動確認
dotnet run --project tools/FakeVrchat            # ターミナル1
dotnet run --project src/VrcPhoneRelay.Server --Relay:OscMode=Fixed  # ターミナル2
```

- E2Eテストはサーバー全体+FakeVrchat+実WebSocketクライアントで、ペアリング/ack/クランプ/レート制限/切断ポリシー/VRChat再検出までを実際に通します
- OSCコーデックは自前実装(i/f/s/T/F+バンドル展開)で、既知バイト列とのラウンドトリップテスト付き

## 開発状況

- [x] Stage 0: リポジトリ・プロトコル定義
- [x] Stage 1: PC中継アプリ(M0〜M6、110テスト)
- [x] Stage 2: Unityエディタ拡張(コード完成。Unityエディタでの実機検証はStage 4)
- [ ] Stage 3: スマートフォンアプリ(開発中)
- [ ] Stage 4: 統合試験(実VRChat+公開テストアバター+実スマホ、仕様書16章 T-001〜T-306)

## セキュリティ・プライバシー

- 接続は同一LAN内のみ。インターネット経由の接続は非対応(初期版の設計方針)
- ペアリング済み端末以外は操作不可。ペアリングモードは利用者の明示操作でのみ開始
- OSC通信はPC内ループバックに限定。未定義パラメータ・範囲外値はVRChatへ転送しない
- 認証トークン・secretはログに出力しない。端末情報はバッテリー残量・充電状態のみ送信

## ライセンス

未定(仕様書22章の未決定事項)。公開・配布方針が決まり次第追記します。
