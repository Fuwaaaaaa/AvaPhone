# Avatar Smartphone Link 通信プロトコル定義 v1

スマートフォンアプリ ⇔ PC中継アプリ間のWebSocketプロトコル、および
中継アプリ ⇔ VRChat間のOSCパラメータの**単一情報源**。

以下の3実装はこの文書に従うこと。差異が生じた場合はこの文書を修正してから実装を追従させる。

| 実装 | 定義ファイル |
|---|---|
| PC中継アプリ (C#) | `relay-app/src/VrcPhoneRelay.Core/Parameters/PhoneParameters.cs` |
| スマホアプリ (Dart) | `mobile-app/lib/core/constants/phone_parameters.dart` |
| Unityエディタ拡張 (C#) | `unity-avatar/Packages/net.transit.phone-gimmick/Editor/ParameterDefinitions.cs` |

---

## 1. アバターパラメータ定義

OSCアドレスは `/avatar/parameters/<パラメータ名>`。

| パラメータ | 型 | 値域 | 初期値 | Synced | Saved | 切断時 | 内容 |
|---|---|---|---|---|---|---|---|
| `Phone/Visible` | Bool | - | false | ON | ON | 維持 | 本体表示 |
| `Phone/Connected` | Bool | - | false | ON | OFF | **false** | 実スマホ接続状態(中継アプリのみが書く) |
| `Phone/Locked` | Bool | - | true | ON | ON | 維持 | ロック状態 |
| `Phone/Page` | Int | 0-7 | 0 | ON | ON | 維持 | 表示画面 |
| `Phone/Pose` | Int | 0-5 | 0 | ON | ON | 維持 | 保持ポーズ |
| `Phone/Battery` | Int | 0-10 | 10 | ON | OFF | 維持 | バッテリー段階 |
| `Phone/CallState` | Int | 0-4 | 0 | ON | OFF | **0リセット** | 通話状態 |
| `Phone/MediaState` | Int | 0-4 | 0 | ON | OFF | **0リセット** | メディア状態 |
| `Phone/NotifyType` | Int | 0-4 | 0 | ON | OFF | **0リセット** | 通知演出種別 |
| `Phone/EventToggle` | Bool | - | false | ON | OFF | 維持 | イベント発火用反転値(レート制限対象) |

- 範囲外のInt値は**クランプ**して適用し、ack にはクランプ後の値を返す。
- 未定義パラメータ・型不一致は**拒否**(`INVALID_PARAMETER` / `INVALID_VALUE`)。
- `Phone/EventToggle` の送信は1秒に1回まで(超過は `RATE_LIMITED`)。
- アバター対応判定: `Phone/Visible` と `Phone/Page` の両方が存在すること。

### 値の意味

- **Page**: 0=ロック 1=ホーム 2=通知 3=通話 4=カメラ 5=メディア 6=設定 7=接続エラー
- **Pose**: 0=収納(腰) 1=右手持ち 2=左手持ち 3=通話(耳あて) 4=自撮り 5=両手操作
- **CallState**: 0=通話なし 1=着信中 2=発信中 3=通話中 4=通話終了演出
- **MediaState**: 0=停止 1=再生 2=一時停止 3=次トラック演出 4=前トラック演出
- **Battery**: 0=0-4% 1=5-14% 2=15-24% … 9=85-94% 10=95-100%(`clamp(round(percent/10), 0, 10)` 相当。0-4%のみ段階0)
- **NotifyType**: 0=演出なし 1-4=通知演出種別(MVPでは1のみ使用、2-4は予約)

---

## 2. WebSocketトランスポート

| 項目 | 値 |
|---|---|
| 方向 | スマホ → PC(クライアント → サーバー) |
| URL | `ws://<host>:<port>/ws`(既定ポート 27810) |
| データ形式 | UTF-8 テキストフレーム、1フレーム=1 JSONメッセージ |
| プロトコルバージョン | `"v": 1`(整数。サーバーは不一致メッセージを `error` で拒否) |
| ハートビート | クライアントが2秒間隔で `ping` 送信、サーバーは `pong` 応答 |
| 切断判定 | 双方とも「最後に何かを受信してから6秒」で切断とみなす |
| 再接続 | クライアント側バックオフ 1, 2, 5, 10, 以降10秒間隔 |

## 3. メッセージ共通形式(エンベロープ)

```json
{
  "v": 1,
  "id": "一意なメッセージ識別子(UUID推奨)",
  "type": "メッセージ種別",
  "timestamp": 1783900000000
}
```

- `timestamp` は送信側のUNIXエポックミリ秒。
- 応答メッセージ(`parameter.ack`、`auth.ack`、要求起因の `error`)の `id` は**要求の `id` を引き継ぐ**。
- サーバー起点のメッセージ(`state.snapshot`、通知的 `error`)の `id` はサーバーが新規採番する。

## 4. メッセージ種別

### 4.1 auth(クライアント→サーバー、接続後最初の1通・必須)

接続後、他のいかなるメッセージより先に送る。10秒以内に `auth` が来ない接続はサーバーが切断する。

初回(ペアリング。QRコードの `token` を使用):

```json
{ "v": 1, "id": "...", "type": "auth", "token": "<pairingToken>",
  "deviceName": "Pixel 9", "timestamp": 0 }
```

再接続(ペアリング時に発行された資格情報を使用):

```json
{ "v": 1, "id": "...", "type": "auth",
  "deviceId": "<deviceId>", "secret": "<deviceSecret>", "timestamp": 0 }
```

### 4.2 auth.ack(サーバー→クライアント)

```json
{ "v": 1, "id": "<authのid>", "type": "auth.ack",
  "deviceId": "<deviceId>", "secret": "<deviceSecret>",
  "serverName": "<PC名>", "timestamp": 0 }
```

- 初回ペアリング成功時: 新規発行の `deviceId` + `secret` を含む。クライアントは安全に保存する。
- 再接続成功時: `deviceId` のみ返す(`secret` は省略)。
- 失敗時は `error`(`AUTH_FAILED` または `PAIRING_REQUIRED`)を返して切断する。
- `auth.ack` 直後、サーバーは必ず `state.snapshot` を1通送る。

### 4.3 ping / pong

```json
{ "v": 1, "id": "...", "type": "ping", "timestamp": 0 }
{ "v": 1, "id": "<pingのid>", "type": "pong", "timestamp": 0 }
```

### 4.4 parameter.set(クライアント→サーバー)

```json
{ "v": 1, "id": "cmd-001", "type": "parameter.set",
  "parameter": "Phone/Page", "value": 4, "timestamp": 0 }
```

`value` はJSONの boolean(Bool型パラメータ)または number(Int型パラメータ)。

### 4.5 parameter.ack(サーバー→クライアント)

```json
{ "v": 1, "id": "cmd-001", "type": "parameter.ack",
  "parameter": "Phone/Page", "value": 4, "status": "applied", "timestamp": 0 }
```

- `status`: `applied`(VRChatから確定値を確認、`value` は確定値)/ `timeout`(1.5秒以内に確認できず。`value` は送信した値)。
- `timeout` 後にVRChatから値が届いた場合は `state.update` として送る。

### 4.6 state.snapshot(サーバー→クライアント)

認証直後・アバター変更時・VRChat再検出時に全状態を送る。

```json
{ "v": 1, "id": "...", "type": "state.snapshot",
  "avatarId": "avtr_xxxx", "supported": true,
  "vrchat": "connected",
  "parameters": { "Phone/Visible": true, "...": "全10パラメータ" },
  "timestamp": 0 }
```

- `vrchat`: `connected` / `not_found` / `osc_disabled`。
- `supported=false` または `vrchat!=connected` の間、クライアントは操作UIを無効化する。
- 非対応アバターでは `parameters` は空オブジェクト。

### 4.7 state.update(サーバー→クライアント)

VRChat側での変更(Expression Menu等)や切断時リセットなど、個別パラメータの確定値変更を通知。

```json
{ "v": 1, "id": "...", "type": "state.update",
  "parameter": "Phone/Page", "value": 2, "timestamp": 0 }
```

### 4.8 error(サーバー→クライアント)

```json
{ "v": 1, "id": "cmd-001", "type": "error",
  "code": "UNSUPPORTED_AVATAR", "message": "人間可読の説明", "timestamp": 0 }
```

| code | 意味 | 接続 |
|---|---|---|
| `OSC_DISABLED` | VRChatは起動しているがOSCが無効 | 維持 |
| `VRCHAT_NOT_FOUND` | VRChatを検出できない | 維持 |
| `UNSUPPORTED_AVATAR` | 現在のアバターに対応パラメータがない | 維持 |
| `INVALID_PARAMETER` | 未定義パラメータ | 維持 |
| `INVALID_VALUE` | 型不一致・変換不能な値 | 維持 |
| `PAIRING_REQUIRED` | 未知の端末(ペアリングモードでQRを再読取せよ) | 切断 |
| `AUTH_FAILED` | トークン/secret不正・期限切れ | 切断 |
| `RATE_LIMITED` | 送信頻度超過 | 維持 |
| `TIMEOUT` | VRChat側応答なし(parameter.ackのstatus=timeoutと併用しない。汎用) | 維持 |

## 5. QRコードペイロード

```json
{ "protocol": 1, "host": "192.168.1.10", "port": 27810, "token": "<base64url 128bit>" }
```

- `token` 有効期限5分・使い捨て。ペアリングモード中のみ有効。

## 6. シーケンス概要

```
[初回ペアリング]
スマホ                中継アプリ                VRChat
  │ ←── QR表示(ペアリングモード) │
  │ ws接続                     │
  │ auth(token) ──────────────→│
  │ ←────────── auth.ack(deviceId+secret)
  │ ←────────── state.snapshot │
  │ ping ⇄ pong (2秒間隔)      │
  │ parameter.set(Page=4) ────→│ 検証→OSC送信 ──→ /avatar/parameters/Phone/Page 4
  │                            │ ←── OSCエコー ── Phone/Page=4
  │ ←── parameter.ack(applied) │
  │                            │ ←── OSCエコー ── Phone/Page=2 (Expression Menu操作)
  │ ←── state.update(Page=2)   │
```
