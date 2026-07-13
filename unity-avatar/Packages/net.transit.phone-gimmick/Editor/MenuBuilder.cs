using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// Expressions Menu を構築する。
    ///
    /// ルートメニューへ「Phone」サブメニュー1枠を挿入する。
    /// ルートが満杯(8枠)の場合は既存メニューを変更せずエラーを投げる。
    /// メニューアセットは決定的な名前で Generated 配下に生成し、再実行時は同一アセットを書き直す(冪等)。
    /// </summary>
    public class MenuBuilder
    {
        /// <summary>ルートメニューに挿入するコントロール名。</summary>
        public const string RootControlName = "Phone";

        // アセット名(拡張子なし)。Uninstall の検出にも使用する。
        public const string MainMenuAssetName = "PhoneGimmick_Menu_Main";
        public const string PageMenuAssetName = "PhoneGimmick_Menu_Page";
        public const string PoseMenuAssetName = "PhoneGimmick_Menu_Pose";
        public const string DebugMenuAssetName = "PhoneGimmick_Menu_Debug";

        public void Build(GenerationContext ctx)
        {
            if (ctx.RootMenu == null)
            {
                throw new InvalidOperationException("ルートメニューアセットが解決されていません。");
            }

            // ---- ページ サブメニュー(Toggle 8個: Phone/Page = 0..7) ----
            VRCExpressionsMenu pageMenu = GetOrCreateMenu(ctx, PageMenuAssetName);
            pageMenu.controls.Clear();
            for (int i = 0; i < ParameterDefinitions.PageNames.Length; i++)
            {
                pageMenu.controls.Add(CreateToggle(
                    i + ": " + ParameterDefinitions.PageNames[i],
                    ParameterDefinitions.ParamPage, i));
            }
            EditorUtility.SetDirty(pageMenu);

            // ---- ポーズ サブメニュー(Toggle 6個: Phone/Pose = 0..5) ----
            VRCExpressionsMenu poseMenu = GetOrCreateMenu(ctx, PoseMenuAssetName);
            poseMenu.controls.Clear();
            for (int i = 0; i < ParameterDefinitions.PoseNames.Length; i++)
            {
                poseMenu.controls.Add(CreateToggle(
                    i + ": " + ParameterDefinitions.PoseNames[i],
                    ParameterDefinitions.ParamPose, i));
            }
            EditorUtility.SetDirty(poseMenu);

            // ---- デバッグ サブメニュー(Battery 0/5/10・CallState 0/1/3・MediaState 0/1) ----
            VRCExpressionsMenu debugMenu = GetOrCreateMenu(ctx, DebugMenuAssetName);
            debugMenu.controls.Clear();
            debugMenu.controls.Add(CreateToggle("Battery 0%", ParameterDefinitions.ParamBattery, 0));
            debugMenu.controls.Add(CreateToggle("Battery 50%", ParameterDefinitions.ParamBattery, 5));
            debugMenu.controls.Add(CreateToggle("Battery 100%", ParameterDefinitions.ParamBattery, 10));
            debugMenu.controls.Add(CreateToggle("通話: なし", ParameterDefinitions.ParamCallState, 0));
            debugMenu.controls.Add(CreateToggle("通話: 着信中", ParameterDefinitions.ParamCallState, 1));
            debugMenu.controls.Add(CreateToggle("通話: 通話中", ParameterDefinitions.ParamCallState, 3));
            debugMenu.controls.Add(CreateToggle("メディア: 停止", ParameterDefinitions.ParamMediaState, 0));
            debugMenu.controls.Add(CreateToggle("メディア: 再生", ParameterDefinitions.ParamMediaState, 1));
            EditorUtility.SetDirty(debugMenu);

            // ---- Phone メインメニュー ----
            VRCExpressionsMenu mainMenu = GetOrCreateMenu(ctx, MainMenuAssetName);
            mainMenu.controls.Clear();
            mainMenu.controls.Add(CreateToggle("表示", ParameterDefinitions.ParamVisible, 1));
            mainMenu.controls.Add(CreateToggle("ロック", ParameterDefinitions.ParamLocked, 1));
            mainMenu.controls.Add(CreateSubMenu("ページ", pageMenu));
            mainMenu.controls.Add(CreateSubMenu("ポーズ", poseMenu));
            mainMenu.controls.Add(CreateToggle("通知テスト", ParameterDefinitions.ParamEventToggle, 1));
            mainMenu.controls.Add(CreateSubMenu("デバッグ", debugMenu));
            EditorUtility.SetDirty(mainMenu);
            ctx.MainMenu = mainMenu;

            // ---- ルートメニューへ挿入 ----
            InsertRootControl(ctx.RootMenu, mainMenu);
            EditorUtility.SetDirty(ctx.RootMenu);
        }

        /// <summary>
        /// ルートメニューに「Phone」サブメニューを1枠挿入する。
        /// 以前の生成分(同名または同一サブメニュー参照)は除去してから追加する(冪等)。
        /// 満杯(8枠)の場合は既存メニューを変更せずに例外を投げる。
        /// </summary>
        private void InsertRootControl(VRCExpressionsMenu rootMenu, VRCExpressionsMenu mainMenu)
        {
            // 以前の生成分を除去(この除去は自分が追加した枠のみが対象)
            rootMenu.controls.RemoveAll(c =>
                c != null &&
                ((c.subMenu != null && c.subMenu == mainMenu) ||
                 string.Equals(c.name, RootControlName, StringComparison.Ordinal)));

            if (rootMenu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
            {
                throw new InvalidOperationException(
                    "ルートの Expressions Menu が満杯(" + VRCExpressionsMenu.MAX_CONTROLS + "枠)のため、" +
                    "「" + RootControlName + "」サブメニューを追加できません。" +
                    "既存メニューは変更していません。1枠空けてから再実行してください。");
            }

            rootMenu.controls.Add(CreateSubMenu(RootControlName, mainMenu));
        }

        // ------------------------------------------------------------------
        // コントロール生成ヘルパー
        // ------------------------------------------------------------------

        private static VRCExpressionsMenu.Control CreateToggle(string name, string parameterName, float value)
        {
            VRCExpressionsMenu.Control control = new VRCExpressionsMenu.Control();
            control.name = name;
            control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            control.parameter = new VRCExpressionsMenu.Control.Parameter { name = parameterName };
            control.value = value;
            control.subParameters = new VRCExpressionsMenu.Control.Parameter[0];
            control.labels = new VRCExpressionsMenu.Control.Label[0];
            return control;
        }

        private static VRCExpressionsMenu.Control CreateSubMenu(string name, VRCExpressionsMenu subMenu)
        {
            VRCExpressionsMenu.Control control = new VRCExpressionsMenu.Control();
            control.name = name;
            control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
            control.subMenu = subMenu;
            control.parameter = new VRCExpressionsMenu.Control.Parameter { name = "" };
            control.subParameters = new VRCExpressionsMenu.Control.Parameter[0];
            control.labels = new VRCExpressionsMenu.Control.Label[0];
            return control;
        }

        /// <summary>決定的パスでメニューアセットを取得または新規作成する(GUID維持)。</summary>
        private static VRCExpressionsMenu GetOrCreateMenu(GenerationContext ctx, string assetName)
        {
            string path = ctx.AvatarFolder + "/" + assetName + ".asset";
            VRCExpressionsMenu menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (menu == null)
            {
                menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                menu.name = assetName;
                AssetDatabase.CreateAsset(menu, path);
            }
            return menu;
        }
    }
}
