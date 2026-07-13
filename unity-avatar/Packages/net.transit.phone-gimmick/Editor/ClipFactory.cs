using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// AnimationClip アセットの生成を担当する。
    ///
    /// - 全クリップは Generated 配下へ決定的な名前で出力する(冪等)。
    ///   既存クリップがあれば同一アセット(同一GUID)へカーブを書き直す。
    /// - Write Defaults OFF 前提のため、各クリップは「そのレイヤーが触る全プロパティ」を
    ///   必ず含む完全クリップとして生成する。
    /// </summary>
    public class ClipFactory
    {
        /// <summary>定数カーブの長さ(1フレーム相当)。</summary>
        private const float OneFrame = 1f / 60f;

        private readonly Transform _avatarRoot;
        private readonly string _animationFolder;

        public ClipFactory(GameObject avatarRoot, string animationFolder)
        {
            if (avatarRoot == null)
            {
                throw new ArgumentNullException(nameof(avatarRoot));
            }
            _avatarRoot = avatarRoot.transform;
            _animationFolder = animationFolder;
        }

        // ------------------------------------------------------------------
        // m_IsActive 切替クリップ
        // ------------------------------------------------------------------

        /// <summary>全ターゲットを同一状態(全ON/全OFF)に切り替える完全クリップ。</summary>
        public AnimationClip CreateToggleClip(string clipName, GameObject[] targets, bool uniformState)
        {
            bool[] states = new bool[targets.Length];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = uniformState;
            }
            return CreateSelectionClip(clipName, targets, states);
        }

        /// <summary>activeIndex のみON、他は全OFFの完全クリップ。activeIndex=-1 で全OFF。</summary>
        public AnimationClip CreateExclusiveToggleClip(string clipName, GameObject[] targets, int activeIndex)
        {
            bool[] states = new bool[targets.Length];
            if (activeIndex >= 0 && activeIndex < targets.Length)
            {
                states[activeIndex] = true;
            }
            return CreateSelectionClip(clipName, targets, states);
        }

        /// <summary>ターゲットごとにON/OFFを指定する完全クリップ。</summary>
        public AnimationClip CreateSelectionClip(string clipName, GameObject[] targets, bool[] states)
        {
            if (targets.Length != states.Length)
            {
                throw new ArgumentException("targets と states の要素数が一致しません。", nameof(states));
            }

            AnimationClip clip = GetOrCreateClip(clipName);
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                {
                    throw new InvalidOperationException(clipName + ": 対象オブジェクトが null です(index=" + i + ")。");
                }
                string path = AnimationUtility.CalculateTransformPath(targets[i].transform, _avatarRoot);
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
                AnimationUtility.SetEditorCurve(clip, binding,
                    AnimationCurve.Constant(0f, OneFrame, states[i] ? 1f : 0f));
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        // ------------------------------------------------------------------
        // スケール演出クリップ(NotifyFX)
        // ------------------------------------------------------------------

        /// <summary>スケールポップ演出(0.5秒: 1 → 1.3 → 1)。</summary>
        public AnimationClip CreateScalePopClip(string clipName, GameObject target)
        {
            AnimationClip clip = GetOrCreateClip(clipName);
            string path = AnimationUtility.CalculateTransformPath(target.transform, _avatarRoot);
            string[] axes = new string[] { "x", "y", "z" };
            foreach (string axis in axes)
            {
                AnimationCurve curve = new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.25f, 1.3f),
                    new Keyframe(0.5f, 1f));
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    path, typeof(Transform), "m_LocalScale." + axis);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>スケールを一定値で保持する完全クリップ(アイドル用)。</summary>
        public AnimationClip CreateScaleHoldClip(string clipName, GameObject target, float scale)
        {
            AnimationClip clip = GetOrCreateClip(clipName);
            string path = AnimationUtility.CalculateTransformPath(target.transform, _avatarRoot);
            string[] axes = new string[] { "x", "y", "z" };
            foreach (string axis in axes)
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    path, typeof(Transform), "m_LocalScale." + axis);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, OneFrame, scale));
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>カーブを一切含まない空クリップ(Effects レイヤーの Init 用)。</summary>
        public AnimationClip CreateEmptyClip(string clipName)
        {
            AnimationClip clip = GetOrCreateClip(clipName);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        // ------------------------------------------------------------------
        // Constraint Weight クリップ(Pose)
        // ------------------------------------------------------------------

        /// <summary>
        /// VRCParentConstraint の5ソースWeightをまとめて設定する完全クリップ。
        /// bindings は <see cref="FindConstraintSourceWeightBindings"/> で取得した実バインディングを渡す。
        /// </summary>
        public AnimationClip CreatePoseClip(string clipName, EditorCurveBinding[] bindings, float[] weights)
        {
            if (bindings.Length != weights.Length)
            {
                throw new ArgumentException("bindings と weights の要素数が一致しません。", nameof(weights));
            }

            AnimationClip clip = GetOrCreateClip(clipName);
            for (int i = 0; i < bindings.Length; i++)
            {
                AnimationUtility.SetEditorCurve(clip, bindings[i],
                    AnimationCurve.Constant(0f, OneFrame, weights[i]));
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>
        /// VRCParentConstraint のソースWeightに対応するアニメーションバインディングを、
        /// AnimationUtility.GetAnimatableBindings() の実バインディングから探索する。
        ///
        /// SDK内部のプロパティパス表現(例: "Sources.source0.Weight")をハードコードせず、
        /// 「source{n}」+「Weight」に相当するものを検索するため、SDKの内部表現変更に頑健。
        /// 見つからない場合は候補一覧を添えて明示的にエラーを投げる。
        /// </summary>
        public static EditorCurveBinding[] FindConstraintSourceWeightBindings(
            GameObject avatarRoot, VRCParentConstraint constraint, int sourceCount)
        {
            if (constraint == null)
            {
                throw new ArgumentNullException(nameof(constraint), "VRCParentConstraint が生成されていません。");
            }

            EditorCurveBinding[] result = new EditorCurveBinding[sourceCount];
            bool[] found = new bool[sourceCount];
            List<string> candidates = new List<string>();

            EditorCurveBinding[] all = AnimationUtility.GetAnimatableBindings(constraint.gameObject, avatarRoot);
            Regex sourceRegex = new Regex(@"source\s*(\d+)", RegexOptions.IgnoreCase);

            foreach (EditorCurveBinding binding in all)
            {
                if (binding.type == null || !typeof(VRCParentConstraint).IsAssignableFrom(binding.type))
                {
                    continue;
                }
                candidates.Add(binding.propertyName);

                // 末尾が Weight でないもの(座標オフセット等)は除外。
                // GlobalWeight は "source{n}" を含まないため正規表現で除外される。
                if (!binding.propertyName.EndsWith("Weight", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Match match = sourceRegex.Match(binding.propertyName);
                if (!match.Success)
                {
                    continue;
                }
                int index;
                if (!int.TryParse(match.Groups[1].Value, out index))
                {
                    continue;
                }
                if (index < 0 || index >= sourceCount || found[index])
                {
                    continue;
                }
                found[index] = true;
                result[index] = binding;
            }

            List<string> missing = new List<string>();
            for (int i = 0; i < sourceCount; i++)
            {
                if (!found[i])
                {
                    missing.Add("source" + i);
                }
            }
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "VRCParentConstraint のソースWeightアニメーションバインディングが見つかりません: " +
                    string.Join(", ", missing) + "\n" +
                    "SDKの内部表現が想定(Sources.source{n}.Weight 相当)と異なる可能性があります。\n" +
                    "検出された VRCParentConstraint のバインディング一覧:\n  " +
                    (candidates.Count > 0 ? string.Join("\n  ", candidates) : "(なし)"));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // アセット入出力
        // ------------------------------------------------------------------

        /// <summary>
        /// 決定的パス「AnimationsFolder/クリップ名.anim」でクリップを取得または新規作成する。
        /// 既存クリップは GUID を維持したままカーブを全消去して再利用する。
        /// </summary>
        private AnimationClip GetOrCreateClip(string clipName)
        {
            string path = _animationFolder + "/" + clipName + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                clip.name = clipName;
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                // 冪等化: floatカーブとオブジェクト参照カーブを全消去してから書き直す
                clip.ClearCurves();
                EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (EditorCurveBinding binding in objectBindings)
                {
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                }
            }
            return clip;
        }
    }
}
