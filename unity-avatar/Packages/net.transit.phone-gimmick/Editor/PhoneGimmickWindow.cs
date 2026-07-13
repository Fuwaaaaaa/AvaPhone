using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// スマホギミックのセットアップウィンドウ。
    /// メニュー: Tools/Phone Gimmick/Setup
    /// </summary>
    public class PhoneGimmickWindow : EditorWindow
    {
        private VRCAvatarDescriptor _descriptor;
        private int _modeIndex; // 0=非破壊(複製), 1=上書き
        private Vector2 _scroll;

        private static readonly string[] ModeLabels = new string[]
        {
            "非破壊(既存FX/Parameters/Menuを複製して編集)",
            "上書き(既存アセットを直接編集)"
        };

        [MenuItem("Tools/Phone Gimmick/Setup")]
        public static void Open()
        {
            PhoneGimmickWindow window = GetWindow<PhoneGimmickWindow>("Phone Gimmick");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Phone Gimmick セットアップ", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // ---- アバター指定 ----
            _descriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                "アバター", _descriptor, typeof(VRCAvatarDescriptor), true);

            if (_descriptor == null)
            {
                if (GUILayout.Button("シーンから自動検出"))
                {
                    _descriptor = FindObjectOfType<VRCAvatarDescriptor>();
                    if (_descriptor == null)
                    {
                        EditorUtility.DisplayDialog("Phone Gimmick",
                            "シーン内に VRCAvatarDescriptor が見つかりませんでした。", "OK");
                    }
                }
                EditorGUILayout.HelpBox("対象アバター(VRCAvatarDescriptor)を指定してください。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();

            // ---- モード選択 ----
            _modeIndex = EditorGUILayout.Popup("生成モード", _modeIndex, ModeLabels);
            if (_modeIndex == 0)
            {
                EditorGUILayout.HelpBox(
                    "非破壊モード: 既存の FX / Expression Parameters / Expressions Menu を\n" +
                    "Assets/PhoneGimmick/Generated/<アバター名>/ に複製してから編集し、\n" +
                    "Avatar Descriptor の参照を複製へ差し替えます。オリジナルは変更されません。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "上書きモード: Avatar Descriptor に割り当てられている既存アセットを直接編集します。\n" +
                    "事前にバックアップを取ることを推奨します。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            // ---- 検証情報 ----
            DrawValidationInfo();

            EditorGUILayout.Space();

            // ---- Write Defaults 警告 ----
            EditorGUILayout.HelpBox(
                "生成される9レイヤーは全ステート Write Defaults OFF です。\n" +
                "既存のFXレイヤーが WD ON で統一されている場合、WD混在によって\n" +
                "意図しない挙動(表情の固まり等)が起きることがあります。",
                MessageType.None);
            if (HasWriteDefaultsOnStates(_descriptor))
            {
                EditorGUILayout.HelpBox(
                    "既存FXに Write Defaults ON のステートが検出されました。WD混在に注意してください。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            // ---- 実行ボタン ----
            GUI.enabled = _descriptor != null;
            if (GUILayout.Button("生成 / 更新", GUILayout.Height(32f)))
            {
                if (EditorUtility.DisplayDialog("Phone Gimmick",
                        "アバター「" + _descriptor.gameObject.name + "」にスマホギミックを生成します。\n" +
                        "モード: " + ModeLabels[_modeIndex] + "\n\nよろしいですか?",
                        "生成する", "キャンセル"))
                {
                    RunInstall();
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("削除(シーンオブジェクト・Phone_レイヤー・Phone/パラメータ・メニュー枠)"))
            {
                if (EditorUtility.DisplayDialog("Phone Gimmick",
                        "アバター「" + _descriptor.gameObject.name + "」からスマホギミックを削除します。\n" +
                        "Generated 配下のアセットファイル自体は残ります。\n\nよろしいですか?",
                        "削除する", "キャンセル"))
                {
                    RunUninstall();
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------
        // 検証表示
        // ------------------------------------------------------------------

        private void DrawValidationInfo()
        {
            GUILayout.Label("検証", EditorStyles.boldLabel);

            // Humanoid / ボーン
            Animator animator = _descriptor.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.isHuman)
            {
                EditorGUILayout.HelpBox("Humanoid アバターではありません。Rig を Humanoid にしてください。", MessageType.Error);
            }
            else
            {
                string missing = "";
                HumanBodyBones[] bones = new HumanBodyBones[]
                {
                    HumanBodyBones.Hips, HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.Head
                };
                foreach (HumanBodyBones bone in bones)
                {
                    if (animator.GetBoneTransform(bone) == null)
                    {
                        missing += (missing.Length > 0 ? ", " : "") + bone;
                    }
                }
                if (missing.Length > 0)
                {
                    EditorGUILayout.HelpBox("必要な Humanoid ボーンが見つかりません: " + missing, MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField("Humanoidボーン", "OK(Hips / RightHand / LeftHand / Head)");
                }
            }

            // パラメータコスト
            try
            {
                int cost = ParameterBuilder.EstimateMergedCost(_descriptor.expressionParameters);
                string label = cost + " / " + VRCExpressionParameters.MAX_PARAMETER_COST + " bit" +
                               "(うち本ギミック " + ParameterDefinitions.ExpectedTotalCost + " bit)";
                if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
                {
                    EditorGUILayout.HelpBox("生成後の同期パラメータが上限を超えます: " + label, MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField("生成後の同期コスト", label);
                }
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox("パラメータコストの見積りに失敗: " + e.Message, MessageType.Warning);
            }

            // 出力先
            EditorGUILayout.LabelField("出力先",
                PhoneGimmickInstaller.GeneratedRootFolder + "/" +
                PhoneGimmickInstaller.SanitizeName(_descriptor.gameObject.name) + "/");
        }

        /// <summary>既存FX(Phone_ レイヤー以外)に Write Defaults ON のステートがあるか調べる。</summary>
        private static bool HasWriteDefaultsOnStates(VRCAvatarDescriptor descriptor)
        {
            int fxIndex = AvatarIntegrator.TryFindFxLayerIndex(descriptor);
            if (fxIndex < 0)
            {
                return false;
            }
            VRCAvatarDescriptor.CustomAnimLayer layer = descriptor.baseAnimationLayers[fxIndex];
            if (layer.isDefault)
            {
                return false;
            }
            AnimatorController controller = layer.animatorController as AnimatorController;
            if (controller == null)
            {
                return false;
            }
            foreach (AnimatorControllerLayer l in controller.layers)
            {
                if (l.name != null && l.name.StartsWith(FxLayerBuilder.LayerPrefix, StringComparison.Ordinal))
                {
                    continue; // 自前のレイヤーは除外
                }
                if (ScanWriteDefaults(l.stateMachine))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ScanWriteDefaults(AnimatorStateMachine sm)
        {
            if (sm == null)
            {
                return false;
            }
            foreach (ChildAnimatorState child in sm.states)
            {
                if (child.state != null && child.state.writeDefaultValues)
                {
                    return true;
                }
            }
            foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            {
                if (ScanWriteDefaults(child.stateMachine))
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 実行
        // ------------------------------------------------------------------

        private void RunInstall()
        {
            try
            {
                PhoneGimmickInstaller.Install(_descriptor, _modeIndex == 1);
                EditorUtility.DisplayDialog("Phone Gimmick",
                    "生成が完了しました。\n詳細は Console ログを確認してください。", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Phone Gimmick - エラー",
                    "生成に失敗しました:\n" + e.Message + "\n\n詳細は Console ログを確認してください。", "OK");
            }
        }

        private void RunUninstall()
        {
            try
            {
                PhoneGimmickInstaller.Uninstall(_descriptor);
                EditorUtility.DisplayDialog("Phone Gimmick", "削除が完了しました。", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Phone Gimmick - エラー",
                    "削除に失敗しました:\n" + e.Message, "OK");
            }
        }
    }
}
