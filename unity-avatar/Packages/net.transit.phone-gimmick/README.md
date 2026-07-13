# Phone Gimmick (net.transit.phone-gimmick)

VRChatアバター用スマホギミックを自動生成するUnityエディタ拡張(VPMパッケージ)です。
モノレポ「AvaPhone」のUnity側実装で、実スマホ → PC中継アプリ → OSC → VRChat の
連携先となる Expression Parameters / FX Animator / Expressions Menu / スマホオブジェクト一式を生成します。

パラメータ定義の正は `docs/protocol.md`(リポジトリルート)です。
Unity側の単一情報源は `Editor/ParameterDefinitions.cs` にあります。

## 動作環境

- Unity 2022.3.22f1
- VRChat SDK `com.vrchat.avatars` 3.7.0 以上(VRC Constraints ネイティブ対応)
- Humanoid アバター(Hips / RightHand / LeftHand / Head ボーン必須)

## 導入方法

### 方法A: VCC(VRChat Creator Companion)にローカルパッケージとして追加

1. VCC を開き、`Settings` → `Packages` → `Add Local Package` を選択
2. このフォルダー(`unity-avatar/Packages/net.transit.phone-gimmick`、`package.json` があるフォルダー)を指定
3. 対象のアバタープロジェクトの `Manage Project` からパッケージ一覧に表示された
   「Phone Gimmick」を追加

### 方法B: プロジェクトの Packages フォルダーへ直接コピー

1. アバタープロジェクトの `Packages/` 直下に `net.transit.phone-gimmick` フォルダーごとコピー
2. Unity を起動(または再フォーカス)するとコンパイルされます

いずれの場合も `com.vrchat.avatars`(3.7.0以上)がプロジェクトに導入済みであることが前提です。

## 使い方

1. アバターをシーンに配置する
2. メニュー `Tools/Phone Gimmick/Setup` を開く
3. アバター(VRCAvatarDescriptor)を指定する
4. 生成モードを選ぶ
   - **非破壊(既定)**: 既存の FX / Parameters / Menu を
     `Assets/PhoneGimmick/Generated/<アバター名>/` に複製してから編集(オリジナル無傷)
   - **上書き**: 既存アセットを直接編集
5. 「生成 / 更新」を押す

生成は冪等です。再実行すると同じアセット・オブジェクトが上書き再生成されます。
削除は同ウィンドウの「削除」ボタンから行えます(Generatedのアセットファイル自体は残ります)。

## 生成されるもの

- シーン: `PhoneGimmick/Body`(VRCParentConstraint)+ 画面/ページ/バッテリー/オーバーレイ/ランプ/通知FX、
  Humanoidボーン直下のアンカー5個(`PhoneAnchor_Stow/RHand/LHand/Ear/Selfie`)
- FX: `Phone_` 接頭辞の9レイヤー(Write Defaults OFF)
- Expression Parameters: `Phone/` 接頭辞の10パラメータ(全Synced、計52bit)
- Expressions Menu: ルートに「Phone」サブメニュー1枠

アンカー位置・回転は体格依存のプレースホルダーです。生成後に各 `PhoneAnchor_*` を
手動調整してください。ページのマテリアル(`Mat_Page_**`)はテクスチャ差し替え可能です。

## 詳細ドキュメント

セットアップ手順・検証手順(M1〜M8)・初回コンパイル時チェックリスト・既知のリスクは
`unity-avatar/docs/setup-guide.md` を参照してください。
