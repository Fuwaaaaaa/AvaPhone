using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using TransIt.PhoneGimmick;

namespace TransIt.PhoneGimmick.Editor
{
    /// <summary>
    /// スマホギミックのシーンオブジェクト(階層・アンカー・VRCParentConstraint)を生成する。
    ///
    /// 冪等性: PhoneGimmickRoot マーカーコンポーネントと PhoneAnchor_* 命名で既存物を検出し、
    /// 削除してから再生成する。
    /// </summary>
    public class PhonePrefabBuilder
    {
        // ---- オブジェクト名(検出にも使用するため変更しないこと) ----
        public const string RootName = "PhoneGimmick";
        public const string BodyName = "Body";
        public const string ModelName = "Model";
        public const string ScreenName = "Screen";
        public const string ConnectionLampName = "ConnectionLamp";
        public const string LampOnName = "Lamp_On";
        public const string LampOffName = "Lamp_Off";
        public const string NotifyFxName = "NotifyFX";

        /// <summary>アンカー名。Constraintソースの固定順序(0=Stow, 1=RHand, 2=LHand, 3=Ear, 4=Selfie)。</summary>
        public static readonly string[] AnchorNames = new string[]
        {
            "PhoneAnchor_Stow", "PhoneAnchor_RHand", "PhoneAnchor_LHand", "PhoneAnchor_Ear", "PhoneAnchor_Selfie"
        };

        /// <summary>アンカーの親となるHumanoidボーン(AnchorNamesと同順)。</summary>
        private static readonly HumanBodyBones[] AnchorBones = new HumanBodyBones[]
        {
            HumanBodyBones.Hips, HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.Head, HumanBodyBones.Head
        };

        /// <summary>
        /// アンカーの初期位置オフセット(アバタールート空間、メートル)。
        /// アバターの体格に依存するため、生成後にユーザーが手動調整する前提のプレースホルダー値。
        /// </summary>
        private static readonly Vector3[] AnchorOffsets = new Vector3[]
        {
            new Vector3(0.15f, 0.00f, 0.05f), // Stow: 腰の右側面
            new Vector3(0.00f, 0.03f, 0.05f), // RHand: 右手のひら付近
            new Vector3(0.00f, 0.03f, 0.05f), // LHand: 左手のひら付近
            new Vector3(0.08f, 0.02f, 0.03f), // Ear: 右耳付近
            new Vector3(0.00f, 0.05f, 0.40f), // Selfie: 頭部前方40cm
        };

        // ページ表示用オブジェクト名(Page_00_Lock … Page_07_Error)
        private static readonly string[] PageObjectSuffixes = new string[]
        {
            "Lock", "Home", "Notify", "Call", "Camera", "Media", "Settings", "Error"
        };

        // ---- マテリアル(生成後にフィールドへ保持) ----
        private Material _matBody;
        private Material _matScreen;
        private Material[] _matPages;
        private Material _matBattery;
        private Material _matCall;
        private Material _matMedia;
        private Material _matNotify;
        private Material _matLampOn;
        private Material _matLampOff;
        private Material _matNotifyFx;

        /// <summary>シーンオブジェクト一式を生成し、参照を ctx へ格納する。</summary>
        public void Build(GenerationContext ctx)
        {
            RemoveGeneratedObjects(ctx.AvatarRoot);
            CreateMaterials(ctx);
            CreateAnchors(ctx);
            CreateHierarchy(ctx);
            SetupConstraint(ctx);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ctx.AvatarRoot.scene);
        }

        // ------------------------------------------------------------------
        // 既存物の削除(冪等化)
        // ------------------------------------------------------------------

        /// <summary>アバター配下の生成済みオブジェクト(マーカー付きルート + アンカー)を削除する。</summary>
        public static void RemoveGeneratedObjects(GameObject avatarRoot)
        {
            // 1. PhoneGimmickRoot マーカーで検出
            List<GameObject> toDestroy = new List<GameObject>();
            PhoneGimmickRoot[] markers = avatarRoot.GetComponentsInChildren<PhoneGimmickRoot>(true);
            foreach (PhoneGimmickRoot marker in markers)
            {
                toDestroy.Add(marker.gameObject);
            }

            // 2. PhoneAnchor_* 命名で検出
            HashSet<string> anchorNames = new HashSet<string>(AnchorNames);
            Transform[] transforms = avatarRoot.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in transforms)
            {
                if (t != null && anchorNames.Contains(t.name))
                {
                    toDestroy.Add(t.gameObject);
                }
            }

            foreach (GameObject go in toDestroy)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ------------------------------------------------------------------
        // マテリアル生成
        // ------------------------------------------------------------------

        private void CreateMaterials(GenerationContext ctx)
        {
            _matBody = GetOrCreateMaterial(ctx, "Mat_Body", new Color(0.10f, 0.10f, 0.12f));
            _matScreen = GetOrCreateMaterial(ctx, "Mat_Screen", new Color(0.02f, 0.02f, 0.04f));

            // 各ページに個別マテリアル(テクスチャ差し替え可能なようにアセット化する)
            Color[] pageColors = new Color[]
            {
                new Color(0.10f, 0.12f, 0.18f), // 00 ロック: 濃紺
                new Color(0.13f, 0.35f, 0.55f), // 01 ホーム: 青
                new Color(0.85f, 0.60f, 0.15f), // 02 通知: 琥珀
                new Color(0.15f, 0.60f, 0.30f), // 03 通話: 緑
                new Color(0.25f, 0.25f, 0.28f), // 04 カメラ: 灰
                new Color(0.55f, 0.20f, 0.55f), // 05 メディア: 紫
                new Color(0.35f, 0.38f, 0.40f), // 06 設定: スレート
                new Color(0.70f, 0.15f, 0.15f), // 07 接続エラー: 赤
            };
            _matPages = new Material[pageColors.Length];
            for (int i = 0; i < pageColors.Length; i++)
            {
                _matPages[i] = GetOrCreateMaterial(
                    ctx, "Mat_Page_" + i.ToString("00") + "_" + PageObjectSuffixes[i], pageColors[i]);
            }

            _matBattery = GetOrCreateMaterial(ctx, "Mat_Battery", new Color(0.20f, 0.80f, 0.30f));
            _matCall = GetOrCreateMaterial(ctx, "Mat_Call", new Color(0.90f, 0.25f, 0.25f));
            _matMedia = GetOrCreateMaterial(ctx, "Mat_Media", new Color(0.25f, 0.45f, 0.90f));
            _matNotify = GetOrCreateMaterial(ctx, "Mat_Notify", new Color(1.00f, 0.80f, 0.10f));
            _matLampOn = GetOrCreateMaterial(ctx, "Mat_Lamp_On", new Color(0.10f, 0.90f, 0.20f));
            _matLampOff = GetOrCreateMaterial(ctx, "Mat_Lamp_Off", new Color(0.25f, 0.25f, 0.25f));
            _matNotifyFx = GetOrCreateMaterial(ctx, "Mat_NotifyFX", new Color(1.00f, 0.55f, 0.10f));
        }

        /// <summary>
        /// マテリアルを取得または新規作成する。
        /// 既存アセットはユーザーの編集(テクスチャ差し替え等)を尊重してそのまま再利用する。
        /// </summary>
        private Material GetOrCreateMaterial(GenerationContext ctx, string name, Color color)
        {
            string path = ctx.MaterialsFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                return mat;
            }

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                throw new InvalidOperationException("Unlit/Color も Standard シェーダーも見つかりません。");
            }

            mat = new Material(shader);
            mat.name = name;
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------------
        // アンカー生成
        // ------------------------------------------------------------------

        private void CreateAnchors(GenerationContext ctx)
        {
            Animator animator = ctx.Descriptor.GetComponent<Animator>();
            Transform root = ctx.AvatarRoot.transform;

            for (int i = 0; i < AnchorNames.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(AnchorBones[i]);
                if (bone == null)
                {
                    throw new InvalidOperationException(
                        "Humanoidボーン " + AnchorBones[i] + " が見つかりません(" + AnchorNames[i] + " 用)。");
                }

                GameObject anchor = new GameObject(AnchorNames[i]);
                anchor.transform.SetParent(bone, false);
                // アバタールート空間のオフセットで初期配置(体格依存のため後で手動調整する前提)
                anchor.transform.position = bone.position + root.rotation * AnchorOffsets[i];

                // 画面(Quadの表面 = ローカル-Z)の向き:
                // Selfie は自分側(後方)、それ以外は前方に画面が向くように 180 度回す
                bool faceForward = i != 4;
                anchor.transform.rotation = faceForward
                    ? root.rotation * Quaternion.Euler(0f, 180f, 0f)
                    : root.rotation;

                ctx.Anchors[i] = anchor.transform;
            }
        }

        // ------------------------------------------------------------------
        // 階層生成
        // ------------------------------------------------------------------

        private void CreateHierarchy(GenerationContext ctx)
        {
            Transform avatarRoot = ctx.AvatarRoot.transform;

            // PhoneGimmick(ルート + マーカー)
            GameObject phoneRoot = new GameObject(RootName);
            phoneRoot.transform.SetParent(avatarRoot, false);
            phoneRoot.AddComponent<PhoneGimmickRoot>();
            ctx.PhoneRoot = phoneRoot;

            // Body(VRCParentConstraint のターゲット)
            GameObject body = new GameObject(BodyName);
            body.transform.SetParent(phoneRoot.transform, false);
            ctx.Body = body;

            // Model(プレースホルダー筐体: スケールしたCube)
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = ModelName;
            RemoveCollider(model);
            model.transform.SetParent(body.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = new Vector3(0.072f, 0.150f, 0.008f);
            SetupRenderer(model, _matBody);
            ctx.Model = model;

            // Screen(Quad。子はScreenのスケール空間で配置される: 幅1=画面幅)
            GameObject screen = CreateQuad(ScreenName, body.transform,
                new Vector3(0f, 0f, -0.0046f), new Vector3(0.064f, 0.136f, 1f), _matScreen, true);
            ctx.Screen = screen;

            // ページ(8枚、排他表示。初期状態は Page_00_Lock のみON = Phone/Page 初期値0)
            ctx.Pages = new GameObject[PageObjectSuffixes.Length];
            for (int i = 0; i < PageObjectSuffixes.Length; i++)
            {
                ctx.Pages[i] = CreateQuad(
                    "Page_" + i.ToString("00") + "_" + PageObjectSuffixes[i],
                    screen.transform,
                    new Vector3(0f, 0f, -0.001f),
                    Vector3.one,
                    _matPages[i],
                    i == 0);
            }

            // バッテリー(11段階の小Quad。段階に応じて幅を変える。初期は Battery_10 のみON)
            ctx.Batteries = new GameObject[11];
            for (int i = 0; i <= 10; i++)
            {
                float width = Mathf.Lerp(0.03f, 0.22f, i / 10f);
                ctx.Batteries[i] = CreateQuad(
                    "Battery_" + i.ToString("00"),
                    screen.transform,
                    new Vector3(0.33f, 0.44f, -0.002f),
                    new Vector3(width, 0.05f, 1f),
                    _matBattery,
                    i == 10);
            }

            // 通話/メディア/通知オーバーレイ(state 0 = 全消灯のため初期は全OFF)
            ctx.CallOverlays = CreateOverlayQuads(screen.transform, "Call",
                new Vector3(0f, 0.10f, -0.003f), new Vector3(0.85f, 0.30f, 1f), _matCall);
            ctx.MediaOverlays = CreateOverlayQuads(screen.transform, "Media",
                new Vector3(0f, -0.25f, -0.003f), new Vector3(0.85f, 0.20f, 1f), _matMedia);
            ctx.NotifyOverlays = CreateOverlayQuads(screen.transform, "Notify",
                new Vector3(0f, 0.38f, -0.004f), new Vector3(0.90f, 0.15f, 1f), _matNotify);

            // 接続ランプ(初期は Phone/Connected=false → Lamp_Off のみON)
            GameObject lamp = new GameObject(ConnectionLampName);
            lamp.transform.SetParent(body.transform, false);
            lamp.transform.localPosition = new Vector3(0.024f, 0.084f, -0.0046f);
            ctx.ConnectionLamp = lamp;
            ctx.LampOn = CreateQuad(LampOnName, lamp.transform,
                Vector3.zero, new Vector3(0.008f, 0.008f, 1f), _matLampOn, false);
            ctx.LampOff = CreateQuad(LampOffName, lamp.transform,
                Vector3.zero, new Vector3(0.008f, 0.008f, 1f), _matLampOff, true);

            // NotifyFX(EventToggle演出対象。スケールポップはこの親オブジェクトに掛かる)
            GameObject notifyFx = new GameObject(NotifyFxName);
            notifyFx.transform.SetParent(body.transform, false);
            notifyFx.transform.localPosition = new Vector3(0f, 0.10f, -0.012f);
            notifyFx.transform.localScale = Vector3.one;
            CreateQuad("Visual", notifyFx.transform,
                Vector3.zero, new Vector3(0.02f, 0.02f, 1f), _matNotifyFx, true);
            ctx.NotifyFx = notifyFx;
        }

        private GameObject[] CreateOverlayQuads(Transform parent, string baseName,
            Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject[] overlays = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                // Call_01 … Call_04(state 0 は「全消灯」でオブジェクトを持たない)
                overlays[i] = CreateQuad(
                    baseName + "_" + (i + 1).ToString("00"),
                    parent, localPos, localScale, mat, false);
            }
            return overlays;
        }

        // ------------------------------------------------------------------
        // Constraint
        // ------------------------------------------------------------------

        private void SetupConstraint(GenerationContext ctx)
        {
            Transform body = ctx.Body.transform;
            Transform stow = ctx.Anchors[0];

            // 初期位置は Stow アンカーに一致させる(オフセットゼロで追従させるため)
            body.SetPositionAndRotation(stow.position, stow.rotation);

            VRCParentConstraint constraint = ctx.Body.AddComponent<VRCParentConstraint>();

            // 固定順序でソース登録: 0=Stow, 1=RHand, 2=LHand, 3=Ear, 4=Selfie
            float[] initialWeights = ParameterDefinitions.PoseSourceWeights[0]; // Pose0 = 収納(腰)
            for (int i = 0; i < ctx.Anchors.Length; i++)
            {
                constraint.Sources.Add(new VRCConstraintSource(ctx.Anchors[i], initialWeights[i]));
            }

            constraint.GlobalWeight = 1f;
            constraint.Locked = true;
            constraint.IsActive = true;

            // スクリプトからプロパティを変更した後は必ず呼ぶ(SDK公式ドキュメントの指示)
            constraint.ApplyConfigurationChanges();

            ctx.BodyConstraint = constraint;
        }

        // ------------------------------------------------------------------
        // プリミティブ生成ヘルパー
        // ------------------------------------------------------------------

        private GameObject CreateQuad(string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Material mat, bool active)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            RemoveCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            SetupRenderer(go, mat);
            go.SetActive(active);
            return go;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void SetupRenderer(GameObject go, Material mat)
        {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
