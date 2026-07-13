using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// VRCAvatarDescriptor との統合を担当する。
    ///
    /// - ResolveTargets: 生成/編集対象の FX・Parameters・ルートメニューを決定する。
    ///   既定(非破壊)では既存アセットを Generated 配下へ複製してから編集する。
    ///   既に Generated 配下の決定的パスを指している場合は in-place 更新。
    ///   上書きモードでは既存アセットを直接編集する。
    /// - Apply: 生成したアセットを Descriptor へ差し替える(最終工程)。
    /// </summary>
    public static class AvatarIntegrator
    {
        public const string FxControllerFileName = "PhoneGimmick_FX.controller";
        public const string ParametersFileName = "PhoneGimmick_Parameters.asset";
        public const string RootMenuFileName = "PhoneGimmick_Menu_Root.asset";

        // ------------------------------------------------------------------
        // FXレイヤー検索
        // ------------------------------------------------------------------

        /// <summary>
        /// baseAnimationLayers から FX レイヤーのインデックスを検索する(インデックス直指定禁止)。
        /// 見つからなければ -1。
        /// </summary>
        public static int TryFindFxLayerIndex(VRCAvatarDescriptor descriptor)
        {
            VRCAvatarDescriptor.CustomAnimLayer[] layers = descriptor.baseAnimationLayers;
            if (layers == null)
            {
                return -1;
            }
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    return i;
                }
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // ターゲット解決(複製 / in-place / 新規)
        // ------------------------------------------------------------------

        public static void ResolveTargets(GenerationContext ctx)
        {
            ResolveFxController(ctx);
            ResolveParameters(ctx);
            ResolveRootMenu(ctx);
        }

        private static void ResolveFxController(GenerationContext ctx)
        {
            int fxIndex = TryFindFxLayerIndex(ctx.Descriptor);
            if (fxIndex < 0)
            {
                throw new InvalidOperationException("baseAnimationLayers に FX レイヤーが見つかりません。");
            }
            ctx.FxLayerIndex = fxIndex;

            VRCAvatarDescriptor.CustomAnimLayer fxLayer = ctx.Descriptor.baseAnimationLayers[fxIndex];

            // isDefault の場合は未カスタムとみなし、割当済みコントローラーは無視する
            AnimatorController source = null;
            if (!fxLayer.isDefault && fxLayer.animatorController != null)
            {
                source = fxLayer.animatorController as AnimatorController;
                if (source == null)
                {
                    throw new InvalidOperationException(
                        "FX に AnimatorOverrideController が設定されています。通常の AnimatorController に変更してください。");
                }
            }

            string dstPath = ctx.AvatarFolder + "/" + FxControllerFileName;

            if (source == null)
            {
                // FX未設定 → Generated 配下に新規作成(既に生成済みならそれを再利用)
                AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(dstPath);
                ctx.FxController = existing != null
                    ? existing
                    : AnimatorController.CreateAnimatorControllerAtPath(dstPath);
            }
            else if (ctx.OverwriteMode || IsSameAssetPath(source, dstPath))
            {
                // 上書きモード、または既に Generated 配下の決定的パス → in-place 更新
                ctx.FxController = source;
            }
            else
            {
                // 非破壊: 複製してから編集(オリジナル無傷)
                ctx.FxController = DuplicateAsset<AnimatorController>(source, dstPath);
            }
        }

        private static void ResolveParameters(GenerationContext ctx)
        {
            VRCExpressionParameters source = ctx.Descriptor.expressionParameters;
            string dstPath = ctx.AvatarFolder + "/" + ParametersFileName;

            if (source == null)
            {
                VRCExpressionParameters existing = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(dstPath);
                if (existing != null)
                {
                    ctx.Parameters = existing;
                }
                else
                {
                    VRCExpressionParameters created = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                    created.name = System.IO.Path.GetFileNameWithoutExtension(ParametersFileName);
                    created.parameters = new VRCExpressionParameters.Parameter[0];
                    AssetDatabase.CreateAsset(created, dstPath);
                    ctx.Parameters = created;
                }
            }
            else if (ctx.OverwriteMode || IsSameAssetPath(source, dstPath))
            {
                ctx.Parameters = source;
            }
            else
            {
                ctx.Parameters = DuplicateAsset<VRCExpressionParameters>(source, dstPath);
            }
        }

        private static void ResolveRootMenu(GenerationContext ctx)
        {
            VRCExpressionsMenu source = ctx.Descriptor.expressionsMenu;
            string dstPath = ctx.AvatarFolder + "/" + RootMenuFileName;

            if (source == null)
            {
                VRCExpressionsMenu existing = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(dstPath);
                if (existing != null)
                {
                    ctx.RootMenu = existing;
                }
                else
                {
                    VRCExpressionsMenu created = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    created.name = System.IO.Path.GetFileNameWithoutExtension(RootMenuFileName);
                    AssetDatabase.CreateAsset(created, dstPath);
                    ctx.RootMenu = created;
                }
            }
            else if (ctx.OverwriteMode || IsSameAssetPath(source, dstPath))
            {
                ctx.RootMenu = source;
            }
            else
            {
                ctx.RootMenu = DuplicateAsset<VRCExpressionsMenu>(source, dstPath);
            }
        }

        // ------------------------------------------------------------------
        // Descriptor への適用(最終工程)
        // ------------------------------------------------------------------

        public static void Apply(GenerationContext ctx)
        {
            VRCAvatarDescriptor descriptor = ctx.Descriptor;

            // Playable Layers をカスタム化し、FX スロットへ生成コントローラーを割り当てる
            descriptor.customizeAnimationLayers = true;

            VRCAvatarDescriptor.CustomAnimLayer[] layers = descriptor.baseAnimationLayers;
            VRCAvatarDescriptor.CustomAnimLayer fxLayer = layers[ctx.FxLayerIndex];
            fxLayer.isDefault = false;
            fxLayer.isEnabled = true;
            fxLayer.animatorController = ctx.FxController;
            layers[ctx.FxLayerIndex] = fxLayer; // CustomAnimLayer は struct のため書き戻す
            descriptor.baseAnimationLayers = layers;

            // Expressions を有効化して差し替え
            descriptor.customExpressions = true;
            descriptor.expressionsMenu = ctx.RootMenu;
            descriptor.expressionParameters = ctx.Parameters;

            EditorUtility.SetDirty(descriptor);
            EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);
        }

        // ------------------------------------------------------------------
        // アセット複製ヘルパー
        // ------------------------------------------------------------------

        private static bool IsSameAssetPath(UnityEngine.Object asset, string path)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(assetPath) &&
                   string.Equals(assetPath, path, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>アセットを決定的パスへ複製し、複製後のアセットを返す(オリジナル無傷)。</summary>
        private static T DuplicateAsset<T>(T source, string dstPath) where T : UnityEngine.Object
        {
            string srcPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(srcPath))
            {
                throw new InvalidOperationException(
                    source.name + " はアセットとして保存されていないため複製できません。" +
                    "アセット化してから再実行するか、上書きモードを使用してください。");
            }

            // 前回の複製が残っていれば削除してから複製する
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dstPath) != null)
            {
                AssetDatabase.DeleteAsset(dstPath);
            }

            if (!AssetDatabase.CopyAsset(srcPath, dstPath))
            {
                throw new InvalidOperationException("アセットの複製に失敗しました: " + srcPath + " → " + dstPath);
            }

            T duplicated = AssetDatabase.LoadAssetAtPath<T>(dstPath);
            if (duplicated == null)
            {
                throw new InvalidOperationException("複製したアセットの読み込みに失敗しました: " + dstPath);
            }
            return duplicated;
        }
    }
}
