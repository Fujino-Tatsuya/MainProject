using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 오너 마스크 암전(할 일 132)에서 <b>남길 렌더러</b>를 고르는 렌더링 레이어 비트 4.
///
/// 비트 배치(조사 2026-10-28): 0 = 조명 / 1 = <see cref="DecalReceivers"/> / 2·3 = <see cref="PlayerSilhouetteLayers"/> /
/// <b>4 = 오너 암전 남김</b> / 8 = TagManager "Ground Layer"(바닥 프리팹 257 = 0+8). 비트 0 은 조명이라 쓰지 않는다.
///
/// ⚠️ 규약은 <see cref="PlayerSilhouetteLayers"/> 와 같다 — 비트를 더할 때 대입으로 덮지 않는다(bit 0 을 잃으면 조명을 못 받는다).
/// 🔴 SVN 잔상(<c>DashAfterimage.cs</c>)은 이 타입을 모른다(SVN 이 git 보다 먼저 들어간다) — 리터럴 <c>1u &lt;&lt; 4</c> 로 같은 비트를 더한다.
///    이 값을 바꾸면 그 파일도 같이 바꿔야 한다.
/// ⚠️ 렌더링 레이어가 Everything(0xFFFFFFFF)인 렌더러(50.Art VFX 프리팹 일부)는 이 비트도 이미 갖고 있어 암전에 늘 남는다.
/// </summary>
public static class InterruptOwnerMaskLayers
{
    public const int LayerIndex = 4;
    public const uint Mask = 1u << LayerIndex;

    public static bool Has(uint renderingLayerMask) => (renderingLayerMask & Mask) != 0;

    /// <summary>비트를 더한다(다른 비트는 그대로).</summary>
    public static uint Add(uint renderingLayerMask) => renderingLayerMask | Mask;

    /// <summary>
    /// 원복 — 태깅 전에 비트가 없었으면(<paramref name="hadBit"/> = false) 우리 비트만 지운다. 다른 비트는 지금 값 그대로 둔다
    /// (암전 중에 실루엣 등이 비트 2·3 을 바꿨어도 되돌리지 않는다).
    /// </summary>
    public static uint Restore(uint renderingLayerMask, bool hadBit) => hadBit ? renderingLayerMask : renderingLayerMask & ~Mask;
}

/// <summary>
/// 암전 동안만 비트 4 를 붙였다가 원복하는 태거(할 일 132). 루트마다 태깅 순간 한 번 렌더러를 모아 캐시한다(매 프레임 탐색 없음).
/// 태깅 뒤 그 루트 아래 새로 생긴 렌더러는 포함되지 않는다.
/// </summary>
public sealed class InterruptOwnerMaskTagger
{
    private struct Tagged
    {
        public Renderer Renderer;
        public bool HadBit;
    }

    private readonly List<Tagged> _tagged = new List<Tagged>();
    private readonly HashSet<Renderer> _taggedSet = new HashSet<Renderer>();
    private readonly List<Renderer> _buffer = new List<Renderer>();

    /// <summary>지금 비트를 붙여 둔 렌더러 수.</summary>
    public int Count => _tagged.Count;

    /// <summary><paramref name="root"/> 아래 렌더러(비활성 포함)에 비트를 더한다. 이미 붙인 것은 건너뛴다. 새로 붙인 수.</summary>
    public int Tag(GameObject root)
    {
        if (root == null)
            return 0;

        root.GetComponentsInChildren(true, _buffer);
        int added = 0;
        for (int i = 0; i < _buffer.Count; i++)
        {
            Renderer r = _buffer[i];
            if (r == null || !_taggedSet.Add(r))
                continue;

            uint mask = r.renderingLayerMask;
            _tagged.Add(new Tagged { Renderer = r, HadBit = InterruptOwnerMaskLayers.Has(mask) });
            r.renderingLayerMask = InterruptOwnerMaskLayers.Add(mask);
            added++;
        }

        _buffer.Clear();
        return added;
    }

    /// <summary>붙인 비트를 전부 원복한다(원래 없던 것만 지운다). 파괴된 렌더러는 건너뛴다.</summary>
    public void RestoreAll()
    {
        for (int i = 0; i < _tagged.Count; i++)
        {
            Renderer r = _tagged[i].Renderer;
            if (r != null)
                r.renderingLayerMask = InterruptOwnerMaskLayers.Restore(r.renderingLayerMask, _tagged[i].HadBit);
        }

        _tagged.Clear();
        _taggedSet.Clear();
    }
}
