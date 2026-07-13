using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// VRCExpressionParameters を構築する。
    ///
    /// マージ規則: 既存の非 Phone/ パラメータは順序を保って維持し、
    /// Phone/ 接頭辞のパラメータは ParameterDefinitions の定義で上書きする(冪等)。
    /// 適用前に CalcTotalCost() で 256bit 超過を検証する。
    /// </summary>
    public class ParameterBuilder
    {
        /// <summary>マージ済みリストをターゲットアセットへ適用する。</summary>
        public void Build(GenerationContext ctx)
        {
            if (ctx.Parameters == null)
            {
                throw new InvalidOperationException("Expression Parameters アセットが解決されていません。");
            }

            VRCExpressionParameters.Parameter[] merged = ctx.MergedParameters;
            if (merged == null)
            {
                merged = BuildMergedList(ctx.Parameters.parameters);
            }

            ctx.Parameters.parameters = merged;

            int cost = ctx.Parameters.CalcTotalCost();
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                throw new InvalidOperationException(
                    "同期パラメータの合計が上限を超えました: " + cost + " / " +
                    VRCExpressionParameters.MAX_PARAMETER_COST + " bit");
            }

            EditorUtility.SetDirty(ctx.Parameters);
        }

        /// <summary>
        /// 既存パラメータと ParameterDefinitions をマージした配列を作る。
        /// 既存の Phone/ 接頭辞エントリは捨て、定義順で末尾に追加し直す。
        /// </summary>
        public static VRCExpressionParameters.Parameter[] BuildMergedList(
            VRCExpressionParameters.Parameter[] existing)
        {
            List<VRCExpressionParameters.Parameter> merged = new List<VRCExpressionParameters.Parameter>();

            if (existing != null)
            {
                foreach (VRCExpressionParameters.Parameter p in existing)
                {
                    if (p == null || string.IsNullOrEmpty(p.name))
                    {
                        continue;
                    }
                    if (!p.name.StartsWith(ParameterDefinitions.ParameterPrefix, StringComparison.Ordinal))
                    {
                        merged.Add(p);
                    }
                }
            }

            foreach (ParameterDefinitions.PhoneParameter def in ParameterDefinitions.All)
            {
                merged.Add(ToVrcParameter(def));
            }

            return merged.ToArray();
        }

        /// <summary>定義から VRCExpressionParameters.Parameter を生成する。全て Synced。</summary>
        public static VRCExpressionParameters.Parameter ToVrcParameter(ParameterDefinitions.PhoneParameter def)
        {
            VRCExpressionParameters.Parameter parameter = new VRCExpressionParameters.Parameter();
            parameter.name = def.Name;
            parameter.valueType = def.Type;
            parameter.defaultValue = def.DefaultValue;
            parameter.saved = def.Saved;
            parameter.networkSynced = true; // 全パラメータ Synced(計52bit)
            return parameter;
        }

        /// <summary>
        /// 与えたパラメータ配列の同期コストを、一時インスタンス経由で SDK の CalcTotalCost() により算出する。
        /// アセットには一切触れない(事前検証用)。
        /// </summary>
        public static int CalculateCost(VRCExpressionParameters.Parameter[] parameters)
        {
            VRCExpressionParameters temp = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            try
            {
                temp.parameters = parameters;
                return temp.CalcTotalCost();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        /// <summary>既存アセットに対する生成後コストの見積り(ウィンドウ表示用)。</summary>
        public static int EstimateMergedCost(VRCExpressionParameters existing)
        {
            VRCExpressionParameters.Parameter[] merged =
                BuildMergedList(existing != null ? existing.parameters : null);
            return CalculateCost(merged);
        }
    }
}
