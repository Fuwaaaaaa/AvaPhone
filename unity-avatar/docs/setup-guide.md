# Phone Gimmick セットアップガイド

Avatar Smartphone Link の Unity(アバター)側セットアップ手順。
対象: Unity 2022.3.22f1 + VRChat Creator Companion(VCC)+ `com.vrchat.avatars` >= 3.7.0。

パラメータ仕様の正は `docs/protocol.md`。Unity側の単一情報源は
`unity-avatar/Packages/net.transit.phone-gimmick/Editor/ParameterDefinitions.cs`。

---

## 1. 前提条件

| 項目 | 要件 |
|---|---|
| Unity | 2022.3.22f1(VCC管理のバージョン) |
| VRChat SDK | com.vrchat.avatars 3.7.0 以上(VRC Constraints ネイティブ対応版) |
| アバター | Humanoid。Hips / RightHand / LeftHand / Head ボーンがマッピング済み |
| 同期パラメータ空き | 52bit 以上(上限256bit) |
| Expressions Menu ルート | 1枠以上の空き(満杯だとエラーで中断・既存メニューは無変更) |

## 2. パッケージ導入

1. VCC → `Settings` → `Packages` → `Add Local Package` で
   `unity-avatar/Packages/net.transit.phone-gimmick` を追加
2. 対象プロジェクトの `Manage Project` で「Phone Gimmick」を追加
3. Unityプロジェクトを開き、コンパイルエラーがないことを確認
   (エラーが出た場合は「4. 初回コンパイル時の確認チェックリスト」を参照)

VCCを使わない場合はプロジェクトの `Packages/` 直下へフォルダーごとコピーでも動作します。

## 3. 生成手順

1. アバターをシーンに配置
2. `Tools/Phone Gimmick/Setup` を開く
3. アバターを指定(「シーンから自動検出」も可)
4. 生成モードを選択
   - **非破壊(既定)**: FX / Parameters / Menu を `Assets/PhoneGimmick/Generated/<アバター名>/` に複製して編集。オリジナル無傷。2回目以降は複製先を in-place 更新
   - **上書き**: Descriptor に割り当て済みのアセットを直接編集
5. 検証表示(Humanoidボーン / 同期コスト / WD混在警告)を確認して「生成 / 更新」
6. 生成後、`PhoneAnchor_Stow / RHand / LHand / Ear / Selfie` の位置・回転を体格に合わせて手動調整
   (アンカーはHumanoidボーン直下にあり、Constraintのソースなので移動すればポーズに反映されます)

再実行は冪等です(Phone_レイヤー全削除→再生成、Phone/パラメータ上書きマージ、
シーンオブジェクトはマーカー検出→削除→再生成)。

### 生成物一覧

| 種別 | 内容 |
|---|---|
| シーン | `PhoneGimmick/Body`(VRCParentConstraint、ソース順序固定: 0=Stow 1=RHand 2=LHand 3=Ear 4=Selfie)、Model/Screen/ページ8枚/バッテリー11段/通話・メディア・通知オーバーレイ各4枚/接続ランプ/NotifyFX |
| FX | `Phone_Visibility / Phone_Page / Phone_Connection / Phone_Battery / Phone_Call / Phone_Media / Phone_Notification / Phone_Effects / Phone_Pose` の9レイヤー(全ステートWD OFF・完全クリップ) |
| Parameters | `Phone/` 10パラメータ・全Synced・計52bit(protocol.md準拠) |
| Menu | ルートに「Phone」1枠(表示/ロック/ページ/ポーズ/通知テスト/デバッグ) |
| アセット | `Assets/PhoneGimmick/Generated/<アバター名>/` にFXコントローラー1個+AnimationClip 47個+Parameters+Menu4個+マテリアル |

補足: 仕様表のPhone_Visibilityの切替対象は Model/Screen/ConnectionLamp ですが、
本体非表示中に通知演出だけが宙に浮いて見えるのを防ぐため、NotifyFX の m_IsActive も
切替対象に含めています(Phone_EffectsはNotifyFXのスケールのみを触るため競合しません)。

## 4. 初回コンパイル時の確認チェックリスト

この拡張はUnity実機なしで実装されているため、初回コンパイル時に以下を確認してください。
いずれもエラー箇所と修正方針をセットで記載します。

- [ ] **asmdefの参照解決**
  `PhoneGimmick.Editor.asmdef` は `VRC.SDKBase` / `VRC.SDK3A` / `VRC.SDKBase.Editor` / `VRC.SDK3A.Editor` を、
  `PhoneGimmick.Runtime.asmdef` は `VRC.SDKBase` を参照しています(ndmf / Modular Avatar の実例で確認済みの名前)。
  Inspector で asmdef を開き、references が Missing になっていないか確認。
  Missing の場合は SDK パッケージ内の実際の asmdef 名に合わせて修正してください。
  なお `VRCParentConstraint`(VRC.SDK3.Dynamics.Constraint.dll)と `VRCConstraintSource`(VRC.Dynamics.dll)は
  Auto Referenced な事前コンパイルDLLのため、`overrideReferences: false` により自動参照される想定です。
  「型が見つからない」エラーが出た場合は asmdef の Override References を有効にし、
  該当DLL(VRC.Dynamics.dll / VRC.SDK3.Dynamics.Constraint.dll)を Assembly References に追加してください。

- [ ] **VRCConstraintSource のコンストラクタ**(`PhonePrefabBuilder.SetupConstraint`)
  `new VRCConstraintSource(Transform, float)` を使用(公式Constraints APIドキュメントの例に準拠)。
  シグネチャ不一致でエラーになる場合は
  `var s = new VRCConstraintSource(); s.SourceTransform = ...; s.Weight = ...;` 形式か、
  オフセット引数付きコンストラクタ `(Transform, float, Vector3, Vector3)` に変更してください。

- [ ] **CustomAnimLayer のフィールド**(`AvatarIntegrator.Apply`)
  `descriptor.customizeAnimationLayers` / `layer.isDefault` / `layer.isEnabled` は
  デコンパイル知識ベースで、Web上の一次ソースでは `baseAnimationLayers` / `type` /
  `animatorController` までしか確認できていません。
  コンパイルエラー時は VRCAvatarDescriptor の定義(F12)で実フィールド名を確認してください。

- [ ] **VRCExpressionsMenu.Control.Label 型**(`MenuBuilder`)
  `control.labels = new VRCExpressionsMenu.Control.Label[0]` を使用しています。
  labels の型が異なる場合(string[]等)はその型の空配列に、フィールド自体が無い場合は行削除で対応。
  `VRCExpressionsMenu.MAX_CONTROLS`(=8想定)が無い場合はリテラル 8 に置換。

- [ ] **VRCExpressionParameters まわり**(`ParameterBuilder`)
  `Parameter.networkSynced` / `CalcTotalCost()` / `MAX_PARAMETER_COST` はWeb確認済み。
  SDKが古いと `networkSynced` が存在しないため、その場合は SDK を 3.7.0 以上へ更新してください。

- [ ] **ConstraintソースWeightのバインディング探索**(実行時・初回生成時)
  Poseクリップは `AnimationUtility.GetAnimatableBindings()` から
  「`source{n}` + `Weight`」に合致するバインディング(`Sources.source0.Weight` 相当)を探索します。
  見つからない場合は検出候補一覧付きの例外が出ます。その一覧から実際のプロパティ名を確認し、
  `ClipFactory.FindConstraintSourceWeightBindings` の正規表現を調整してください。

- [ ] **サブアセット内包の確認**(初回生成後)
  Project ビューで生成FXコントローラーを選択し、Phone_ 各レイヤーのステートマシン/ステート/遷移が
  コントローラー内に保存されているか(シーン再読込後もレイヤーが空にならないか)を確認。
  問題がある場合は `FxLayerBuilder.EnsureSubAsset` が全オブジェクトに効いているか確認してください。

- [ ] **Quadの向きと画面レイアウト**
  UnityのQuadは -Z 方向が表面です。生成直後に画面がアバターの意図しない向きを向いている場合は
  `PhoneAnchor_*` の回転で調整してください(スクリプト修正は不要)。

## 5. 動作検証手順(M1〜M8)

Av3Emulator(Lyuma)または Gesture Manager をシーンに置いて Play モードで検証します。
パラメータ操作は Av3Emulator の「Floats/Ints/Bools」欄、または Gesture Manager の
Radial Menu(生成された Phone メニュー)から行います。OSC経由の値も同じパラメータに入るため、
ここで動けば中継アプリ経由でも動きます。

| # | 検証項目 | 手順 | 期待結果 |
|---|---|---|---|
| M1 | 表示切替 | `Phone/Visible` を false→true→false | 本体・画面・ランプ・通知FXが一括で表示/非表示。初期状態は非表示 |
| M2 | ページ切替 | `Phone/Page` を 0→7 まで順に変更 | 該当ページのみ表示(0=ロック濃紺 … 7=接続エラー赤)。他ページは消灯 |
| M3 | 接続ランプ | `Phone/Connected` を true/false | true=緑ランプ、false=灰ランプ。排他表示 |
| M4 | バッテリー | `Phone/Battery` を 0/5/10 | 段階に応じた幅のゲージのみ表示。初期値10 |
| M5 | オーバーレイ | `Phone/CallState`・`Phone/MediaState`・`Phone/NotifyType` を 0〜4 | 0で全消灯、1〜4で該当オーバーレイのみ点灯。3系統が独立動作 |
| M6 | 通知演出 | `Phone/EventToggle` を true→false→true と反転 | 反転のたびにNotifyFXが0.5秒スケールポップ(1→1.3→1)。**Playモード開始直後には発火しない**(Init直行の確認) |
| M7 | ポーズ | `Phone/Pose` を 0→1→2→3→4→5 | 腰→右手→左手→耳→自撮り位置→両手中点へ0.2秒でブレンド移動。Pose5は左右手の中点 |
| M8 | 既定値・保存 | Playを再起動(またはアバターリセット) | Visible=false, Locked=true, Page=0, Pose=0, Battery=10, 他=0 に戻る。Expression Parameters上でSavedがVisible/Locked/Page/PoseのみON、コスト表示が52bit(+既存分)であること |

追加確認(アップロード前):
- VRChat SDK Build パネルのバリデーションで Constraint / パラメータ関連の警告が出ないこと
- 既存ギミック(表情等)が非破壊モードで無傷であること(オリジナルのFXアセットのdiffが無い)

## 6. 既知のリスクと対処

| リスク | 内容 | 対処 |
|---|---|---|
| WD混在 | 生成レイヤーはWD OFF。既存FXがWD ONで統一されている場合、他ギミックの挙動に影響する可能性 | 生成ウィンドウの警告を確認。問題が出る場合は既存レイヤー側の設計に合わせて全ステートのWDを統一する |
| ConstraintのSDK内部表現変更 | ソースWeightのアニメーションパスはSDK内部表現に依存。生成時に実バインディングを探索するため通常は追従するが、大幅変更時は例外で停止する | 例外メッセージの候補一覧を確認し `FindConstraintSourceWeightBindings` の探索条件を更新 |
| メニュー満杯 | ルート8枠使用済みだと生成が中断される(既存メニューは無変更) | ルートメニューを1枠空けて再実行 |
| 256bit超過 | 既存パラメータ+52bitが上限を超えると生成前検証で中断 | 既存の同期パラメータを削減 |
| 複製後の乖離 | 非破壊モードは初回に複製を作るため、以後オリジナルのFX/Menuを編集しても反映されない | 運用開始後は複製(Generated配下)を正として編集するか、上書きモードへ移行 |
| アンカー初期位置 | 体格非依存の固定オフセットのため、そのままでは位置が合わない | 生成後に `PhoneAnchor_*` を手動調整(再生成しても削除→再生成されるため、調整値は再生成前に記録するかプレハブ化を推奨) |
| 再生成でアンカー調整が消える | 冪等化のためアンカーも削除→再生成される | アンカー調整は最後に行うか、調整値をメモしておく。恒久対応はTODO(位置保持オプション) |
| Av3Emulatorの互換性 | VRC Constraints対応は Av3Emulator 3.x 以降。古い版だとPose検証(M7)が動かない | Av3Emulator を最新化するか、実VRChatでM7を確認 |
| IEditorOnly | `PhoneGimmickRoot` はビルド時にSDKが除去するため実機には残らない(想定どおり) | 対処不要(マーカーが実機に必要になった場合は IEditorOnly を外す) |

## 7. OSC連携の確認(参考)

中継アプリからの操作対象は `/avatar/parameters/Phone/*` です(protocol.md 参照)。
アバター対応判定は `Phone/Visible` と `Phone/Page` の存在で行われるため、
アップロード後にVRChatのOSC Debugパネルで両パラメータが見えることを確認してください。
初回はVRChat側のOSC設定(Options→OSC→Enabled)と、アバター切替によるOSC設定ファイルの
再生成(`%LOCALAPPDATA%Low/VRChat/VRChat/OSC/` 配下の該当アバターjson削除→再読込)が必要な場合があります。
