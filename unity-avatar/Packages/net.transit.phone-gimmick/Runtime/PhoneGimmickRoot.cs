using UnityEngine;
using VRC.SDKBase;

namespace TransIt.PhoneGimmick
{
    /// <summary>
    /// スマホギミックのルートオブジェクトを識別するためのマーカーコンポーネント。
    ///
    /// - エディタ拡張(PhoneGimmickInstaller)が既存ギミックの検出・削除・再生成に使用する。
    /// - <see cref="IEditorOnly"/> を実装しているため、アバターのアップロード時に
    ///   VRChat SDK がこのコンポーネントを自動的に除去する(実行時には残らない)。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Phone Gimmick/Phone Gimmick Root (マーカー)")]
    public class PhoneGimmickRoot : MonoBehaviour, IEditorOnly
    {
        // マーカー専用。ロジックは持たない。
    }
}
