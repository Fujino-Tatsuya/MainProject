using UnityEngine;

/// <summary>
/// 플레이어 실루엣(벽 뒤 윤곽선)이 <b>누구를 그릴지</b> 고르는 데 쓰는 렌더링 레이어 비트.
///
/// 🔴 <b>왜 레이어(<c>GameObject.layer</c>)가 아닌가</b>(2026-09-14 실측): 실제로 스폰되는
/// <c>Paladin</c> 프리팹은 <b>루트만 layer 6(Player)</b> 이고 정작 렌더러 4개
/// (<c>tripo_part_0</c>·검·방패·시체표식)는 <b>전부 layer 0</b> 이다. 레이어는 자식에게
/// 상속되지 않으므로 레이어 필터로 거르면 <b>아무것도 그려지지 않는다.</b>
///
/// ⚠️ <b>규약은 <see cref="DecalReceivers"/> 와 같다 — 비트를 추가만 하고 절대 지우지 않는다.</b>
/// URP 에서 Rendering Layer 는 Light Layer 와 같은 비트다. 이 프로젝트의 모든 라이트가
/// <b>bit 0 만</b> 비추므로, 대입(<c>=</c>)으로 덮어써서 bit 0 을 잃으면 캐릭터가 조명을 못 받아
/// 새까맣게 된다. 이 사고는 이미 한 번 났고 <see cref="DecalReceivers"/> 주석에 남아 있다.
///
/// 비트 배치: 0 = 조명 / 1 = <c>DecalReceiver</c> / <b>2 = 내 캐릭터 / 3 = 다른 플레이어</b>.
/// </summary>
public static class PlayerSilhouetteLayers
{
    /// <summary>보는 사람 자신의 캐릭터(초록).</summary>
    public const int LocalLayerIndex = 2;

    /// <summary>같이 하는 다른 플레이어(파랑).</summary>
    public const int RemoteLayerIndex = 3;

    public const uint LocalMask = 1u << LocalLayerIndex;
    public const uint RemoteMask = 1u << RemoteLayerIndex;

    private const uint BothMask = LocalMask | RemoteMask;

    /// <summary>
    /// <paramref name="root"/> 아래 모든 렌더러를 실루엣 대상으로 표시한다(비활성 포함).
    ///
    /// 🔴 <paramref name="isLocal"/> 은 <b>보는 사람 기준</b>이다. 같은 캐릭터라도 피어마다 값이
    /// 다르므로 <b>복제하면 안 된다</b> — 각 클라가 자기 화면에서 판단해야 맞다.
    ///
    /// 🔴 <b>오클루전 컬링도 함께 끈다.</b> 플레이어 렌더러는 전부 <c>m_DynamicOccludee: 1</c> 이라
    /// 벽 뒤에 들어가면 <b>렌더러 자체가 컬링되어</b> 실루엣도 같이 사라진다 — 정확히 우리가
    /// 그리고 싶은 순간에 대상이 없어지는 셈이다.
    /// </summary>
    /// <returns>표시한 렌더러 수. 0 이면 배선이 잘못된 것이므로 호출처가 진단을 남긴다.</returns>
    public static int Tag(GameObject root, bool isLocal)
    {
        if (root == null) return 0;

        uint add = isLocal ? LocalMask : RemoteMask;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            // 소유권이 바뀌거나 리스폰하면 반대쪽 비트가 남아 있을 수 있다 — 우리 비트 2개만 정리하고
            // 나머지(조명·데칼)는 그대로 둔다.
            r.renderingLayerMask = (r.renderingLayerMask & ~BothMask) | add;
            r.allowOcclusionWhenDynamic = false;
        }

        return renderers.Length;
    }

    /// <summary>실루엣 대상에서 뺀다. 우리 비트만 지운다 — 조명·데칼 비트는 건드리지 않는다.</summary>
    public static void Untag(GameObject root)
    {
        if (root == null) return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            r.renderingLayerMask &= ~BothMask;
        }
    }
}
