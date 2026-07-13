using System;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// スマホギミックのアバターパラメータ定義。
    ///
    /// docs/protocol.md「1. アバターパラメータ定義」の単一情報源(Unity側)。
    /// 他のクラス(ParameterBuilder / FxLayerBuilder / MenuBuilder)は必ずこのクラスを参照し、
    /// パラメータ名・型・初期値・Saved をハードコードしないこと。
    /// </summary>
    public static class ParameterDefinitions
    {
        // ---- パラメータ名(OSCアドレスは /avatar/parameters/<名前>) ----
        public const string ParamVisible = "Phone/Visible";
        public const string ParamConnected = "Phone/Connected";
        public const string ParamLocked = "Phone/Locked";
        public const string ParamPage = "Phone/Page";
        public const string ParamPose = "Phone/Pose";
        public const string ParamBattery = "Phone/Battery";
        public const string ParamCallState = "Phone/CallState";
        public const string ParamMediaState = "Phone/MediaState";
        public const string ParamNotifyType = "Phone/NotifyType";
        public const string ParamEventToggle = "Phone/EventToggle";

        /// <summary>このギミックが管理するパラメータの接頭辞。マージ時の上書き判定に使用する。</summary>
        public const string ParameterPrefix = "Phone/";

        /// <summary>全パラメータの合計同期コスト(Bool=1bit x4 + Int=8bit x6 = 52bit)。</summary>
        public const int ExpectedTotalCost = 52;

        /// <summary>1パラメータ分の定義。</summary>
        public sealed class PhoneParameter
        {
            public readonly string Name;
            public readonly VRCExpressionParameters.ValueType Type;
            public readonly float DefaultValue;
            public readonly bool Saved;
            /// <summary>Int型の最小値(Bool型では未使用)。</summary>
            public readonly int MinValue;
            /// <summary>Int型の最大値(Bool型では未使用)。</summary>
            public readonly int MaxValue;

            public PhoneParameter(
                string name,
                VRCExpressionParameters.ValueType type,
                float defaultValue,
                bool saved,
                int minValue,
                int maxValue)
            {
                Name = name;
                Type = type;
                DefaultValue = defaultValue;
                Saved = saved;
                MinValue = minValue;
                MaxValue = maxValue;
            }
        }

        /// <summary>
        /// 全パラメータ定義(docs/protocol.md の表と一致させること)。
        /// 全て Synced(networkSynced = true)。
        /// </summary>
        public static readonly PhoneParameter[] All = new PhoneParameter[]
        {
            //                 名前              型                                       初期値  Saved  最小 最大
            new PhoneParameter(ParamVisible,     VRCExpressionParameters.ValueType.Bool,  0f,    true,  0, 1),
            new PhoneParameter(ParamConnected,   VRCExpressionParameters.ValueType.Bool,  0f,    false, 0, 1),
            new PhoneParameter(ParamLocked,      VRCExpressionParameters.ValueType.Bool,  1f,    true,  0, 1),
            new PhoneParameter(ParamPage,        VRCExpressionParameters.ValueType.Int,   0f,    true,  0, 7),
            new PhoneParameter(ParamPose,        VRCExpressionParameters.ValueType.Int,   0f,    true,  0, 5),
            new PhoneParameter(ParamBattery,     VRCExpressionParameters.ValueType.Int,   10f,   false, 0, 10),
            new PhoneParameter(ParamCallState,   VRCExpressionParameters.ValueType.Int,   0f,    false, 0, 4),
            new PhoneParameter(ParamMediaState,  VRCExpressionParameters.ValueType.Int,   0f,    false, 0, 4),
            new PhoneParameter(ParamNotifyType,  VRCExpressionParameters.ValueType.Int,   0f,    false, 0, 4),
            new PhoneParameter(ParamEventToggle, VRCExpressionParameters.ValueType.Bool,  0f,    false, 0, 1),
        };

        // ---- 値の意味(docs/protocol.md「値の意味」と一致) ----

        /// <summary>Page 0-7 のラベル。</summary>
        public static readonly string[] PageNames = new string[]
        {
            "ロック", "ホーム", "通知", "通話", "カメラ", "メディア", "設定", "接続エラー"
        };

        /// <summary>Pose 0-5 のラベル。</summary>
        public static readonly string[] PoseNames = new string[]
        {
            "収納(腰)", "右手持ち", "左手持ち", "通話(耳あて)", "自撮り", "両手操作"
        };

        /// <summary>
        /// Pose ごとの VRCParentConstraint ソースWeight。
        /// ソース順序は固定: 0=Stow(腰), 1=RHand(右手), 2=LHand(左手), 3=Ear(耳), 4=Selfie(自撮り)。
        /// Pose5(両手操作)は右手・左手の中点ブレンド。
        /// </summary>
        public static readonly float[][] PoseSourceWeights = new float[][]
        {
            new float[] { 1f, 0f,   0f,   0f, 0f }, // Pose0: 収納(腰)
            new float[] { 0f, 1f,   0f,   0f, 0f }, // Pose1: 右手持ち
            new float[] { 0f, 0f,   1f,   0f, 0f }, // Pose2: 左手持ち
            new float[] { 0f, 0f,   0f,   1f, 0f }, // Pose3: 通話(耳あて)
            new float[] { 0f, 0f,   0f,   0f, 1f }, // Pose4: 自撮り
            new float[] { 0f, 0.5f, 0.5f, 0f, 0f }, // Pose5: 両手操作(中点)
        };

        /// <summary>Constraintソース数(=アンカー数)。</summary>
        public const int ConstraintSourceCount = 5;

        /// <summary>定義を名前で検索する。見つからなければ null。</summary>
        public static PhoneParameter Find(string name)
        {
            foreach (PhoneParameter p in All)
            {
                if (string.Equals(p.Name, name, StringComparison.Ordinal))
                {
                    return p;
                }
            }
            return null;
        }
    }
}
