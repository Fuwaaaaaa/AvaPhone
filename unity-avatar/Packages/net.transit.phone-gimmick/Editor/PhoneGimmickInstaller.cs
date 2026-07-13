using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using TransIt.PhoneGimmick;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// 生成処理全体で共有するコンテキスト。
    /// 生成先アセット・シーンオブジェクト参照・設定を1か所に集約する。
    /// </summary>
    public class GenerationContext
    {
        public VRCAvatarDescriptor Descriptor;
        public GameObject AvatarRoot;

        /// <summary>true=既存アセットを直接編集(上書き)、false=Generated配下へ複製してから編集(非破壊・既定)。</summary>
        public bool OverwriteMode;

        // ---- 出力先フォルダー ----
        public string AvatarFolder;      // Assets/PhoneGimmick/Generated/<アバター名>
        public string AnimationsFolder;  // <AvatarFolder>/Animations
        public string MaterialsFolder;   // <AvatarFolder>/Materials

        // ---- 生成/編集対象アセット ----
        public AnimatorController FxController;
        public int FxLayerIndex = -1;
        public VRCExpressionParameters Parameters;
        public VRCExpressionsMenu RootMenu;
        public VRCExpressionsMenu MainMenu;

        /// <summary>検証段階で構築したマージ済みパラメータ一覧。</summary>
        public VRCExpressionParameters.Parameter[] MergedParameters;

        // ---- シーンオブジェクト参照(PhonePrefabBuilderが設定) ----
        public GameObject PhoneRoot;
        public GameObject Body;
        public GameObject Model;
        public GameObject Screen;
        public GameObject ConnectionLamp;
        public GameObject LampOn;
        public GameObject LampOff;
        public GameObject NotifyFx;
        public GameObject[] Pages;          // 8
        public GameObject[] Batteries;      // 11
        public GameObject[] CallOverlays;   // 4 (Call_01..04)
        public GameObject[] MediaOverlays;  // 4
        public GameObject[] NotifyOverlays; // 4
        public Transform[] Anchors = new Transform[ParameterDefinitions.ConstraintSourceCount];
        public VRCParentConstraint BodyConstraint;

        public GenerationContext(VRCAvatarDescriptor descriptor, bool overwriteMode)
        {
            Descriptor = descriptor;
            AvatarRoot = descriptor != null ? descriptor.gameObject : null;
            OverwriteMode = overwriteMode;
        }
    }

    /// <summary>
    /// オーケストレータ。検証 → シーン生成 → パラメータ → FXレイヤー → メニュー → アバター適用 の順に実行する。
    /// Undo対応は行わないが、例外発生時にどの工程まで完了したかをログで報告する。
    /// </summary>
    public static class PhoneGimmickInstaller
    {
        public const string GeneratedRootFolder = "Assets/PhoneGimmick/Generated";
        public const string LogPrefix = "[PhoneGimmick] ";

        /// <summary>ギミック一式を生成してアバターへ適用する。</summary>
        public static void Install(VRCAvatarDescriptor descriptor, bool overwriteMode)
        {
            GenerationContext ctx = new GenerationContext(descriptor, overwriteMode);
            List<string> completed = new List<string>();
            string stage = "検証";
            try
            {
                Validate(ctx);
                completed.Add(stage);

                stage = "出力フォルダー作成";
                PrepareFolders(ctx);
                completed.Add(stage);

                // 複製(非破壊)モードでは、既存 FX / Parameters / Menu をここで Generated 配下へ複製する。
                // 既に Generated 配下を指している場合は in-place 更新になる。
                stage = "生成先アセットの解決(複製/新規作成)";
                AvatarIntegrator.ResolveTargets(ctx);
                completed.Add(stage);

                stage = "シーンオブジェクト生成(PhonePrefabBuilder)";
                new PhonePrefabBuilder().Build(ctx);
                completed.Add(stage);

                stage = "Expression Parameters 生成(ParameterBuilder)";
                new ParameterBuilder().Build(ctx);
                completed.Add(stage);

                stage = "FXレイヤー生成(FxLayerBuilder)";
                BuildClipsAndLayers(ctx);
                completed.Add(stage);

                stage = "Expressions Menu 生成(MenuBuilder)";
                new MenuBuilder().Build(ctx);
                completed.Add(stage);

                stage = "アバターへの適用(AvatarIntegrator)";
                AvatarIntegrator.Apply(ctx);
                completed.Add(stage);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(LogPrefix + "生成が完了しました。\n" +
                          "  出力先: " + ctx.AvatarFolder + "\n" +
                          "  FX: " + AssetDatabase.GetAssetPath(ctx.FxController) + "\n" +
                          "  Parameters: " + AssetDatabase.GetAssetPath(ctx.Parameters) + "\n" +
                          "  Menu(ルート): " + AssetDatabase.GetAssetPath(ctx.RootMenu));
            }
            catch (Exception)
            {
                Debug.LogError(LogPrefix + "生成に失敗しました。失敗した工程: " + stage + "\n" +
                               "完了済みの工程: " + (completed.Count > 0 ? string.Join(" → ", completed) : "(なし)") + "\n" +
                               "途中まで生成されたアセット/オブジェクトが残っている可能性があります。" +
                               "原因を解消してから再実行すると、冪等処理により途中状態は上書き再生成されます。");
                throw;
            }
        }

        /// <summary>
        /// ギミックをアバターから削除する。
        /// シーンオブジェクト、FXの Phone_ レイヤー、Phone/ パラメータ、メニューの Phone 枠を除去する。
        /// Generated 配下のアセットファイル自体は削除しない(手動で削除可能)。
        /// </summary>
        public static void Uninstall(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor), "アバター(VRCAvatarDescriptor)が指定されていません。");
            }

            GameObject avatarRoot = descriptor.gameObject;

            // 1. シーンオブジェクト削除(マーカー + アンカー)
            PhonePrefabBuilder.RemoveGeneratedObjects(avatarRoot);

            // 2. FXコントローラーから Phone_ レイヤーと Phone/ パラメータを削除
            int fxIndex = AvatarIntegrator.TryFindFxLayerIndex(descriptor);
            if (fxIndex >= 0)
            {
                AnimatorController fx = descriptor.baseAnimationLayers[fxIndex].animatorController as AnimatorController;
                if (fx != null)
                {
                    FxLayerBuilder builder = new FxLayerBuilder(fx);
                    builder.RemovePhoneLayers();
                    builder.RemovePhoneParameters();
                    EditorUtility.SetDirty(fx);
                }
            }

            // 3. Expression Parameters から Phone/ を削除
            VRCExpressionParameters parameters = descriptor.expressionParameters;
            if (parameters != null && parameters.parameters != null)
            {
                List<VRCExpressionParameters.Parameter> kept = new List<VRCExpressionParameters.Parameter>();
                foreach (VRCExpressionParameters.Parameter p in parameters.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name))
                    {
                        continue;
                    }
                    if (!p.name.StartsWith(ParameterDefinitions.ParameterPrefix, StringComparison.Ordinal))
                    {
                        kept.Add(p);
                    }
                }
                parameters.parameters = kept.ToArray();
                EditorUtility.SetDirty(parameters);
            }

            // 4. ルートメニューから Phone 枠を削除
            VRCExpressionsMenu rootMenu = descriptor.expressionsMenu;
            if (rootMenu != null && rootMenu.controls != null)
            {
                rootMenu.controls.RemoveAll(c =>
                    c != null &&
                    (c.name == MenuBuilder.RootControlName ||
                     (c.subMenu != null && c.subMenu.name == MenuBuilder.MainMenuAssetName)));
                EditorUtility.SetDirty(rootMenu);
            }

            EditorUtility.SetDirty(descriptor);
            EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);
            AssetDatabase.SaveAssets();

            Debug.Log(LogPrefix + "削除が完了しました。Generated 配下のアセットファイルは残っています: " + GeneratedRootFolder);
        }

        /// <summary>生成前の検証。失敗時は例外を投げる。</summary>
        public static void Validate(GenerationContext ctx)
        {
            if (ctx.Descriptor == null)
            {
                throw new InvalidOperationException("アバター(VRCAvatarDescriptor)が指定されていません。");
            }

            Animator animator = ctx.Descriptor.GetComponent<Animator>();
            if (animator == null)
            {
                throw new InvalidOperationException("アバターに Animator コンポーネントがありません。");
            }
            if (animator.avatar == null || !animator.isHuman)
            {
                throw new InvalidOperationException("アバターが Humanoid ではありません。Rig 設定を Humanoid にしてください。");
            }

            // アンカー生成に必要なボーンの存在確認
            HumanBodyBones[] requiredBones = new HumanBodyBones[]
            {
                HumanBodyBones.Hips, HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.Head
            };
            foreach (HumanBodyBones bone in requiredBones)
            {
                if (animator.GetBoneTransform(bone) == null)
                {
                    throw new InvalidOperationException("Humanoidボーン " + bone + " が見つかりません。Avatar設定を確認してください。");
                }
            }

            if (ctx.Descriptor.baseAnimationLayers == null || ctx.Descriptor.baseAnimationLayers.Length == 0)
            {
                throw new InvalidOperationException(
                    "VRCAvatarDescriptor の baseAnimationLayers が初期化されていません。" +
                    "一度 Inspector で Avatar Descriptor を選択してから再実行してください。");
            }

            // FXがAnimatorOverrideControllerの場合は非対応
            int fxIndex = AvatarIntegrator.TryFindFxLayerIndex(ctx.Descriptor);
            if (fxIndex < 0)
            {
                throw new InvalidOperationException("baseAnimationLayers に FX レイヤーが見つかりません。");
            }
            VRCAvatarDescriptor.CustomAnimLayer fxLayer = ctx.Descriptor.baseAnimationLayers[fxIndex];
            if (!fxLayer.isDefault && fxLayer.animatorController != null &&
                !(fxLayer.animatorController is AnimatorController))
            {
                throw new InvalidOperationException(
                    "FX に AnimatorOverrideController が設定されています。通常の AnimatorController に変更してから再実行してください。");
            }

            // パラメータコストの事前検証(256bit超過チェック)
            VRCExpressionParameters.Parameter[] existing =
                ctx.Descriptor.expressionParameters != null ? ctx.Descriptor.expressionParameters.parameters : null;
            ctx.MergedParameters = ParameterBuilder.BuildMergedList(existing);
            int cost = ParameterBuilder.CalculateCost(ctx.MergedParameters);
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                throw new InvalidOperationException(
                    "同期パラメータの合計が上限を超えます: " + cost + " / " + VRCExpressionParameters.MAX_PARAMETER_COST + " bit。" +
                    "既存の Expression Parameters を削減してから再実行してください(本ギミックは " +
                    ParameterDefinitions.ExpectedTotalCost + " bit 使用します)。");
            }
        }

        /// <summary>出力フォルダーを作成し、コンテキストへパスを設定する。</summary>
        private static void PrepareFolders(GenerationContext ctx)
        {
            string avatarName = SanitizeName(ctx.AvatarRoot.name);
            ctx.AvatarFolder = GeneratedRootFolder + "/" + avatarName;
            ctx.AnimationsFolder = ctx.AvatarFolder + "/Animations";
            ctx.MaterialsFolder = ctx.AvatarFolder + "/Materials";
            EnsureAssetFolder(ctx.AnimationsFolder);
            EnsureAssetFolder(ctx.MaterialsFolder);
        }

        /// <summary>クリップ生成 → FXレイヤー構築。</summary>
        private static void BuildClipsAndLayers(GenerationContext ctx)
        {
            ClipFactory factory = new ClipFactory(ctx.AvatarRoot, ctx.AnimationsFolder);

            // --- Constraintソース Weight の実バインディングを探索(ハードコード禁止) ---
            EditorCurveBinding[] weightBindings = ClipFactory.FindConstraintSourceWeightBindings(
                ctx.AvatarRoot, ctx.BodyConstraint, ParameterDefinitions.ConstraintSourceCount);

            // --- Visibility(Model/Screen/ConnectionLamp + NotifyFX のm_IsActive切替) ---
            // 注: NotifyFX は仕様表に含まれないが、本体非表示中に通知演出だけが宙に浮いて
            //     見えるのを防ぐため表示切替対象に含める。
            GameObject[] visibilityTargets = new GameObject[]
            {
                ctx.Model, ctx.Screen, ctx.ConnectionLamp, ctx.NotifyFx
            };
            AnimationClip clipHidden = factory.CreateToggleClip("Phone_Visibility_Hidden", visibilityTargets, false);
            AnimationClip clipVisible = factory.CreateToggleClip("Phone_Visibility_Visible", visibilityTargets, true);

            // --- Page(8ページ排他表示) ---
            AnimationClip[] pageClips = new AnimationClip[ctx.Pages.Length];
            for (int i = 0; i < ctx.Pages.Length; i++)
            {
                pageClips[i] = factory.CreateExclusiveToggleClip(
                    "Phone_Page_" + i.ToString("00"), ctx.Pages, i);
            }

            // --- Connection(Lamp_On/Lamp_Off切替) ---
            AnimationClip clipConnOff = factory.CreateSelectionClip("Phone_Connection_Off",
                new GameObject[] { ctx.LampOn, ctx.LampOff }, new bool[] { false, true });
            AnimationClip clipConnOn = factory.CreateSelectionClip("Phone_Connection_On",
                new GameObject[] { ctx.LampOn, ctx.LampOff }, new bool[] { true, false });

            // --- Battery(11段階排他表示) ---
            AnimationClip[] batteryClips = new AnimationClip[ctx.Batteries.Length];
            for (int i = 0; i < ctx.Batteries.Length; i++)
            {
                batteryClips[i] = factory.CreateExclusiveToggleClip(
                    "Phone_Battery_" + i.ToString("00"), ctx.Batteries, i);
            }

            // --- Call/Media/Notify(オーバーレイ。state 0 = 全消灯) ---
            AnimationClip[] callClips = CreateOverlayClips(factory, "Phone_Call_", ctx.CallOverlays);
            AnimationClip[] mediaClips = CreateOverlayClips(factory, "Phone_Media_", ctx.MediaOverlays);
            AnimationClip[] notifyClips = CreateOverlayClips(factory, "Phone_Notify_", ctx.NotifyOverlays);

            // --- Effects(NotifyFXスケールポップ) ---
            AnimationClip clipPop = factory.CreateScalePopClip("Phone_Fx_NotifyPop", ctx.NotifyFx);
            AnimationClip clipFxIdle = factory.CreateScaleHoldClip("Phone_Fx_Idle", ctx.NotifyFx, 1f);
            AnimationClip clipEmpty = factory.CreateEmptyClip("Phone_Fx_Empty");

            // --- Pose(ConstraintソースWeightの完全セット x6) ---
            AnimationClip[] poseClips = new AnimationClip[ParameterDefinitions.PoseSourceWeights.Length];
            for (int i = 0; i < poseClips.Length; i++)
            {
                poseClips[i] = factory.CreatePoseClip(
                    "Phone_Pose_" + i.ToString("00"), weightBindings, ParameterDefinitions.PoseSourceWeights[i]);
            }

            // --- FXレイヤー構築(Phone_ 接頭辞レイヤーを全削除 → 再生成で冪等) ---
            FxLayerBuilder fx = new FxLayerBuilder(ctx.FxController);
            fx.RemovePhoneLayers();
            fx.EnsureParameters();

            fx.BoolLayer("Phone_Visibility", ParameterDefinitions.ParamVisible,
                "Hidden", clipHidden, "Visible", clipVisible);

            fx.IntSelectorLayer("Phone_Page", ParameterDefinitions.ParamPage,
                pageClips, 0f, 0, BuildStateNames("Page", ParameterDefinitions.PageNames.Length));

            fx.BoolLayer("Phone_Connection", ParameterDefinitions.ParamConnected,
                "Disconnected", clipConnOff, "Connected", clipConnOn);

            fx.IntSelectorLayer("Phone_Battery", ParameterDefinitions.ParamBattery,
                batteryClips, 0f, 10, BuildStateNames("Bat", batteryClips.Length));

            fx.IntSelectorLayer("Phone_Call", ParameterDefinitions.ParamCallState,
                callClips, 0f, 0, BuildStateNames("Call", callClips.Length));

            fx.IntSelectorLayer("Phone_Media", ParameterDefinitions.ParamMediaState,
                mediaClips, 0f, 0, BuildStateNames("Media", mediaClips.Length));

            fx.IntSelectorLayer("Phone_Notification", ParameterDefinitions.ParamNotifyType,
                notifyClips, 0f, 0, BuildStateNames("Notify", notifyClips.Length));

            fx.EffectsLayer("Phone_Effects", ParameterDefinitions.ParamEventToggle,
                clipPop, clipFxIdle, clipEmpty);

            // Poseのみ遷移Duration 0.2秒(固定時間)
            fx.IntSelectorLayer("Phone_Pose", ParameterDefinitions.ParamPose,
                poseClips, 0.2f, 0, BuildStateNames("Pose", poseClips.Length));

            EditorUtility.SetDirty(ctx.FxController);
        }

        /// <summary>オーバーレイ用クリップ群(state 0 = 全消灯、state n = Overlay_0n のみ点灯)を生成する。</summary>
        private static AnimationClip[] CreateOverlayClips(ClipFactory factory, string namePrefix, GameObject[] overlays)
        {
            // overlays は 4個(_01.._04)。ステートは 0..4 の5個。
            AnimationClip[] clips = new AnimationClip[overlays.Length + 1];
            for (int state = 0; state <= overlays.Length; state++)
            {
                // state 0 → activeIndex -1(全OFF)、state n → overlays[n-1] のみON
                clips[state] = factory.CreateExclusiveToggleClip(
                    namePrefix + state.ToString("00"), overlays, state - 1);
            }
            return clips;
        }

        private static string[] BuildStateNames(string prefix, int count)
        {
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = prefix + i;
            }
            return names;
        }

        /// <summary>"Assets/..." 形式のフォルダーを親から順に作成する。</summary>
        public static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        /// <summary>アバター名をフォルダー名として安全な文字列に変換する。</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Avatar";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '/' || chars[i] == '\\')
                {
                    chars[i] = '_';
                }
            }
            string result = new string(chars).Trim();
            return string.IsNullOrEmpty(result) ? "Avatar" : result;
        }
    }
}
