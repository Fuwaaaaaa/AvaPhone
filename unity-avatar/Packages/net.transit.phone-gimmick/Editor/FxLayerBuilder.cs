using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// FX AnimatorController に Phone_ 接頭辞のレイヤー群を構築する。
    ///
    /// 実装ルール:
    /// - 全ステート Write Defaults OFF
    /// - AnyState 遷移は必ず canTransitionToSelf = false
    /// - Int切替遷移: Equals条件 / hasExitTime=false / duration 0(Poseのみ0.2秒固定)
    /// - StateMachine 等のサブアセットは AssetDatabase.AddObjectToAsset でコントローラーへ内包
    /// - 冪等性: Phone_ 接頭辞レイヤーを全削除してから再生成する
    /// </summary>
    public class FxLayerBuilder
    {
        public const string LayerPrefix = "Phone_";

        private readonly AnimatorController _controller;

        public FxLayerBuilder(AnimatorController controller)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller), "FX AnimatorController が null です。");
            }
            _controller = controller;
        }

        // ------------------------------------------------------------------
        // 冪等化(削除)
        // ------------------------------------------------------------------

        /// <summary>Phone_ 接頭辞のレイヤーを全削除する(サブアセットも破棄)。</summary>
        public void RemovePhoneLayers()
        {
            AnimatorControllerLayer[] layers = _controller.layers;
            List<AnimatorControllerLayer> kept = new List<AnimatorControllerLayer>();
            foreach (AnimatorControllerLayer layer in layers)
            {
                if (layer.name != null && layer.name.StartsWith(LayerPrefix, StringComparison.Ordinal))
                {
                    DestroyStateMachineRecursive(layer.stateMachine);
                }
                else
                {
                    kept.Add(layer);
                }
            }
            _controller.layers = kept.ToArray();
            EditorUtility.SetDirty(_controller);
        }

        /// <summary>Phone/ 接頭辞のAnimatorパラメータを全削除する(アンインストール用)。</summary>
        public void RemovePhoneParameters()
        {
            AnimatorControllerParameter[] parameters = _controller.parameters;
            List<AnimatorControllerParameter> kept = new List<AnimatorControllerParameter>();
            foreach (AnimatorControllerParameter p in parameters)
            {
                if (p.name == null || !p.name.StartsWith(ParameterDefinitions.ParameterPrefix, StringComparison.Ordinal))
                {
                    kept.Add(p);
                }
            }
            _controller.parameters = kept.ToArray();
            EditorUtility.SetDirty(_controller);
        }

        /// <summary>ステートマシンとその配下のサブアセットを再帰的に破棄する。</summary>
        private static void DestroyStateMachineRecursive(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                return;
            }

            // 子ステートマシン(states/stateMachines プロパティはコピー配列を返すため反復中の破棄も安全)
            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                DestroyStateMachineRecursive(child.stateMachine);
            }

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                AnimatorState state = child.state;
                if (state == null)
                {
                    continue;
                }
                foreach (AnimatorStateTransition t in state.transitions)
                {
                    UnityEngine.Object.DestroyImmediate(t, true);
                }
                foreach (StateMachineBehaviour b in state.behaviours)
                {
                    UnityEngine.Object.DestroyImmediate(b, true);
                }
                UnityEngine.Object.DestroyImmediate(state, true);
            }

            foreach (AnimatorStateTransition t in stateMachine.anyStateTransitions)
            {
                UnityEngine.Object.DestroyImmediate(t, true);
            }
            foreach (AnimatorTransition t in stateMachine.entryTransitions)
            {
                UnityEngine.Object.DestroyImmediate(t, true);
            }
            foreach (StateMachineBehaviour b in stateMachine.behaviours)
            {
                UnityEngine.Object.DestroyImmediate(b, true);
            }

            UnityEngine.Object.DestroyImmediate(stateMachine, true);
        }

        // ------------------------------------------------------------------
        // Animatorパラメータ
        // ------------------------------------------------------------------

        /// <summary>
        /// ParameterDefinitions の全パラメータを AnimatorController に登録する。
        /// 既に同名・同型のパラメータがあれば維持、型違いは置き換える。
        /// </summary>
        public void EnsureParameters()
        {
            foreach (ParameterDefinitions.PhoneParameter def in ParameterDefinitions.All)
            {
                AnimatorControllerParameterType type =
                    def.Type == VRCExpressionParameters.ValueType.Bool
                        ? AnimatorControllerParameterType.Bool
                        : AnimatorControllerParameterType.Int;

                AnimatorControllerParameter existing = null;
                foreach (AnimatorControllerParameter p in _controller.parameters)
                {
                    if (string.Equals(p.name, def.Name, StringComparison.Ordinal))
                    {
                        existing = p;
                        break;
                    }
                }

                if (existing != null)
                {
                    if (existing.type == type)
                    {
                        continue; // 既存をそのまま利用
                    }
                    _controller.RemoveParameter(existing);
                }

                AnimatorControllerParameter parameter = new AnimatorControllerParameter();
                parameter.name = def.Name;
                parameter.type = type;
                parameter.defaultBool = def.DefaultValue != 0f;
                parameter.defaultInt = (int)def.DefaultValue;
                parameter.defaultFloat = def.DefaultValue;
                _controller.AddParameter(parameter);
            }
            EditorUtility.SetDirty(_controller);
        }

        // ------------------------------------------------------------------
        // レイヤー構築
        // ------------------------------------------------------------------

        /// <summary>
        /// Bool直接遷移の2ステートレイヤー(Visibility / Connection 用)。
        /// offステートが既定。off→on [If]、on→off [IfNot]、duration 0。
        /// </summary>
        public void BoolLayer(string layerName, string parameterName,
            string offStateName, AnimationClip offClip,
            string onStateName, AnimationClip onClip)
        {
            AnimatorStateMachine sm = AddLayer(layerName);

            AnimatorState offState = AddState(sm, offStateName, offClip, new Vector3(300f, 60f, 0f));
            AnimatorState onState = AddState(sm, onStateName, onClip, new Vector3(300f, 180f, 0f));
            sm.defaultState = offState;

            AnimatorStateTransition toOn = offState.AddTransition(onState);
            ConfigureTransition(toOn, 0f, false, 0f);
            toOn.AddCondition(AnimatorConditionMode.If, 0f, parameterName);

            AnimatorStateTransition toOff = onState.AddTransition(offState);
            ConfigureTransition(toOff, 0f, false, 0f);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            EditorUtility.SetDirty(_controller);
        }

        /// <summary>
        /// AnyState + Equals 方式のInt切替レイヤー(Page/Battery/Call/Media/Notify/Pose 共通)。
        /// </summary>
        /// <param name="layerName">レイヤー名(Phone_ 接頭辞込み)</param>
        /// <param name="parameterName">Int型Animatorパラメータ名</param>
        /// <param name="clips">値 0..N-1 に対応するクリップ</param>
        /// <param name="duration">遷移時間(秒、固定時間)。通常0、Poseのみ0.2</param>
        /// <param name="defaultIndex">既定ステートのインデックス(パラメータ初期値に合わせる)</param>
        /// <param name="stateNames">ステート名(null なら自動命名)</param>
        public void IntSelectorLayer(string layerName, string parameterName,
            AnimationClip[] clips, float duration, int defaultIndex, string[] stateNames)
        {
            AnimatorStateMachine sm = AddLayer(layerName);

            for (int i = 0; i < clips.Length; i++)
            {
                string stateName = stateNames != null && i < stateNames.Length
                    ? stateNames[i]
                    : layerName + "_" + i;
                AnimatorState state = AddState(sm, stateName, clips[i], new Vector3(300f, 60f + 70f * i, 0f));
                if (i == defaultIndex)
                {
                    sm.defaultState = state;
                }

                AnimatorStateTransition transition = sm.AddAnyStateTransition(state);
                transition.canTransitionToSelf = false; // 必須: 自己遷移禁止
                ConfigureTransition(transition, duration, false, 0f);
                transition.AddCondition(AnimatorConditionMode.Equals, i, parameterName);
            }

            EditorUtility.SetDirty(_controller);
        }

        /// <summary>
        /// EventToggle(Bool)反転リトリガーレイヤー(Phone_Effects)。
        ///
        /// Init(空、既定) → A_Idle [true] / B_Idle [false]  … ロード時の誤発火防止(Playを経由しない)
        /// B_Idle →[true]→ A_Play →(ExitTime 1.0)→ A_Idle
        /// A_Idle →[false]→ B_Play →(ExitTime 1.0)→ B_Idle
        /// A_Play/B_Play は同一の通知演出クリップ(NotifyFXスケールポップ0.5秒)。
        /// </summary>
        public void EffectsLayer(string layerName, string parameterName,
            AnimationClip popClip, AnimationClip idleClip, AnimationClip emptyClip)
        {
            AnimatorStateMachine sm = AddLayer(layerName);

            AnimatorState init = AddState(sm, "Init", emptyClip, new Vector3(300f, 40f, 0f));
            AnimatorState aPlay = AddState(sm, "A_Play", popClip, new Vector3(560f, 160f, 0f));
            AnimatorState aIdle = AddState(sm, "A_Idle", idleClip, new Vector3(560f, 280f, 0f));
            AnimatorState bPlay = AddState(sm, "B_Play", popClip, new Vector3(40f, 280f, 0f));
            AnimatorState bIdle = AddState(sm, "B_Idle", idleClip, new Vector3(40f, 160f, 0f));
            sm.defaultState = init;

            AnimatorStateTransition t;

            // Init からは Idle へ即遷移(Play を経由しない)
            t = init.AddTransition(aIdle);
            ConfigureTransition(t, 0f, false, 0f);
            t.AddCondition(AnimatorConditionMode.If, 0f, parameterName);

            t = init.AddTransition(bIdle);
            ConfigureTransition(t, 0f, false, 0f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            // false エッジ: A_Idle → B_Play → B_Idle
            t = aIdle.AddTransition(bPlay);
            ConfigureTransition(t, 0f, false, 0f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, parameterName);

            t = bPlay.AddTransition(bIdle);
            ConfigureTransition(t, 0f, true, 1f); // ExitTime 1.0(クリップ再生完了後)

            // true エッジ: B_Idle → A_Play → A_Idle
            t = bIdle.AddTransition(aPlay);
            ConfigureTransition(t, 0f, false, 0f);
            t.AddCondition(AnimatorConditionMode.If, 0f, parameterName);

            t = aPlay.AddTransition(aIdle);
            ConfigureTransition(t, 0f, true, 1f); // ExitTime 1.0

            EditorUtility.SetDirty(_controller);
        }

        // ------------------------------------------------------------------
        // 内部ヘルパー
        // ------------------------------------------------------------------

        /// <summary>新しいレイヤーを追加し、そのステートマシンを返す。</summary>
        private AnimatorStateMachine AddLayer(string layerName)
        {
            AnimatorStateMachine sm = new AnimatorStateMachine();
            sm.name = layerName;
            sm.hideFlags = HideFlags.HideInHierarchy;
            // サブアセットとしてコントローラーへ内包する
            AssetDatabase.AddObjectToAsset(sm, _controller);

            AnimatorControllerLayer layer = new AnimatorControllerLayer();
            layer.name = layerName;
            layer.defaultWeight = 1f;
            layer.stateMachine = sm;
            _controller.AddLayer(layer);

            return sm;
        }

        /// <summary>Write Defaults OFF のステートを追加する。</summary>
        private AnimatorState AddState(AnimatorStateMachine sm, string stateName, Motion motion, Vector3 position)
        {
            AnimatorState state = sm.AddState(stateName, position);
            state.motion = motion;
            state.writeDefaultValues = false; // Write Defaults OFF
            EnsureSubAsset(state);
            return state;
        }

        /// <summary>遷移の共通設定。ConfigureTransition(遷移, 固定時間duration, ExitTime使用, exitTime値)。</summary>
        private AnimatorStateTransition ConfigureTransition(
            AnimatorStateTransition transition, float duration, bool hasExitTime, float exitTime)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true; // duration は秒指定
            transition.duration = duration;
            transition.offset = 0f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            EnsureSubAsset(transition);
            return transition;
        }

        /// <summary>
        /// オブジェクトがまだアセットに内包されていなければコントローラーのサブアセットにする。
        /// (AddState / AddTransition は永続化済みステートマシンに対して自動でサブアセット化するが、
        ///  Unityバージョン差異への保険として明示的に確認する)
        /// </summary>
        private void EnsureSubAsset(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }
            if (!EditorUtility.IsPersistent(obj))
            {
                obj.hideFlags |= HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(obj, _controller);
            }
        }
    }
}
