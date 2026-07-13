# VrcPhoneRelay - AvaPhone PC中継アプリ

スマートフォンアプリとPC版VRChatをつなぐ常駐アプリ。
WebSocketサーバー(スマホ側)とOSC/OSCQueryクライアント(VRChat側)を中継する。

プロトコルの正は [docs/protocol.md](../docs/protocol.md)。

## 使い方

```powershell
# 開発実行
dotnet run --project src/VrcPhoneRelay.Server

# テスト(VRChat不要 — FakeVrchatでE2E)
dotnet test

# 配布用ビルド(単一exe)
dotnet publish src/VrcPhoneRelay.Server -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

起動するとコンソール対話UIが立ち上がる(未ペアリング時はQRコードを自動表示):

| コマンド | 動作 |
|---|---|
| `pair` | ペアリングモード開始+QRコード表示(トークン有効期限5分・使い捨て) |
| `status` | VRChat検出状態・アバター・全パラメータ値を表示 |
| `devices` | ペアリング済み端末一覧 |
| `unpair <deviceId>` | ペアリング解除 |
| `quit` | 終了 |

## 設定

`appsettings.json` の `Relay` セクション、環境変数 `VRCPHONERELAY_Relay__*`、
またはコマンドライン `--Relay:WsPort=27810` で指定できる。

| キー | 既定値 | 内容 |
|---|---|---|
| `WsPort` | 27810 | WebSocket待受ポート |
| `BindAddress` | 0.0.0.0 | 待受アドレス(スマホ実機から接続するため全IF) |
| `OscMode` | Auto | `Auto` / `OscQuery` / `Fixed` |
| `FixedSendHost` / `FixedSendPort` | 127.0.0.1 / 9000 | 固定ポート時のVRChat OSC入力 |
| `FixedReceivePort` | 9001 | 固定ポート時のVRChat OSC出力受信 |
| `ServiceName` | VrcPhoneRelay | OSCQueryで広告する名前 |

`Auto`(既定)はOSCQuery(mDNS)でVRChatを自動検出しつつ、9001が空いていれば
レガシー固定ポートも併用する。VPN等でmDNSが使えない環境では `Fixed` を指定する。

## ファイアウォール

スマホ実機からの接続には WebSocket ポート(既定 27810/TCP)の受信許可が必要:

```powershell
netsh advfirewall firewall add rule name="VrcPhoneRelay WS" dir=in action=allow protocol=TCP localport=27810 profile=private
```

OSCQuery(mDNS)には UDP 5353 の許可が必要な場合がある。OSC通信自体はPC内ループバックのみ。

## VRChat無しでの動作確認

```powershell
# ターミナル1: VRChat偽装ツール(受信9000→エコー9001)
dotnet run --project tools/FakeVrchat

# ターミナル2: 中継アプリを固定ポートモードで起動
dotnet run --project src/VrcPhoneRelay.Server --Relay:OscMode=Fixed

# FakeVrchat側で対応アバターへの変更を模擬
avatar avtr_test-1234
```

※ 固定ポートモードのアバター対応判定はVRChatのOSC設定ファイル
(`%USERPROFILE%\AppData\LocalLow\VRChat\VRChat\OSC\usr_*\Avatars\avtr_*.json`)を参照する。
FakeVrchatだけで対応アバターを完全に模擬する場合は統合テスト
(`tests/VrcPhoneRelay.Integration.Tests`)の `RelayServerFixture` を参照。

## 構成

```
src/VrcPhoneRelay.Core    パラメータ定義・検証・状態ストア・ペアリング(依存なし・全ユニットテスト対象)
src/VrcPhoneRelay.Osc     OSCコーデック(自前実装)・OscBridge・OSCQueryロケータ・アバター監視
src/VrcPhoneRelay.Server  Kestrel WebSocketサーバー・メッセージルーター・コンソールUI
tools/FakeVrchat          VRChatのOSC挙動を偽装する開発ツール
tests/                    ユニット+E2E統合テスト
```

ユーザーデータ: `%APPDATA%\VrcPhoneRelay\devices.json`(ペアリング済み端末。secretはSHA-256ハッシュ保存)
