# AvaPhone(アバフォン)

実スマートフォンからVRChatアバター上のスマートフォンギミックを操作するシステム。
(正式名称: AvaPhone / 仕様書上の旧仮称: Avatar Smartphone Link)

```
スマホアプリ(Flutter) ⇔ [WebSocket/JSON/同一LAN] ⇔ PC中継アプリ(C#/.NET 8) ⇔ [OSC/OSCQuery] ⇔ PC版VRChat
```

スマホ画面のミラーリングではなく、状態値(表示/画面/ポーズ/バッテリー等)をアバターの
Expression Parameters へ送り、事前作成済みの画面素材とAnimatorで疑似的に表現する方式。

## リポジトリ構成

| ディレクトリ | 内容 |
|---|---|
| [`docs/`](docs/) | 基本仕様書 v0.1、[プロトコル定義(単一情報源)](docs/protocol.md)、[仕様修正記録](docs/spec-errata.md) |
| [`relay-app/`](relay-app/) | PC中継アプリ(C# / .NET 8)。WebSocketサーバー+OSC/OSCQueryクライアント |
| [`mobile-app/`](mobile-app/) | スマートフォンアプリ(Flutter、iOS/Android。Windowsデスクトップは開発用) |
| [`unity-avatar/`](unity-avatar/) | アバターギミック自動生成のUnityエディタ拡張(VPMパッケージソース) |

## 開発環境

- **relay-app**: .NET 8 SDK(`dotnet build` / `dotnet test`)
- **mobile-app**: Flutter SDK(stable)
- **unity-avatar**: Unity 2022.3.22f1 + VRChat SDK (com.vrchat.avatars >= 3.7.0)+ VCC

## クイックスタート(開発中)

```powershell
# PC中継アプリのテスト(VRChat不要 — FakeVrchatでE2E)
cd relay-app
dotnet test

# 中継アプリ起動
dotnet run --project src/VrcPhoneRelay.Server

# VRChat偽装ツール(開発用)
dotnet run --project tools/FakeVrchat
```

## ファイアウォール設定(Android/iOS実機接続時)

スマホ実機からPCへ接続するには、WebSocketポート(既定 27810/TCP)の受信許可が必要:

```powershell
netsh advfirewall firewall add rule name="VrcPhoneRelay WS" dir=in action=allow protocol=TCP localport=27810 profile=private
```

OSC通信(9000/9001)はPC内ループバックのみのため設定不要。

## ドキュメント

- [基本仕様書 v0.1](docs/spec-v0.1.md)
- [通信プロトコル定義 v1](docs/protocol.md) — WebSocketメッセージ・アバターパラメータの単一情報源
- [仕様書からの確定・修正事項](docs/spec-errata.md)
