using UnityEngine;

/// <summary>
/// 바닥 표식·장판 데칼이 <b>칠해질 표면</b>을 표시한다(렌더링 레이어 비트 1 = <c>DecalReceiver</c>).
///
/// 🔴 <b>왜 캐릭터가 아니라 수신자를 표시하는가</b>(2026-09-04 실측):
/// URP 에서 Rendering Layer 는 <b>Light Layer 와 같은 비트</b>다. 이 프로젝트는
/// <c>m_SupportsLightLayers: 1</c> 이고 모든 라이트가 <b>bit 0 만</b> 비춘다
/// (<c>4.MapScene</c> 라이트 <c>m_RenderingLayers: 1</c>, 보스룸 라이트는 키가 없어 기본값).
/// 그래서 "캐릭터에 전용 레이어를 주고 데칼이 그 레이어를 피한다"는 흔한 방법을 쓰면
/// <b>캐릭터가 bit 0 을 잃어 조명을 못 받고 어두워진다.</b>
/// 방향을 뒤집어 <b>수신자에게 비트를 더한다</b> — 아무것도 bit 0 을 잃지 않으므로 조명은 무변경이고,
/// 캐릭터는 이 비트가 없어서 데칼 마스크에 자동으로 걸리지 않는다.
///
/// ⚠️ <b>규약: 이 시스템은 비트를 추가만 하고 절대 지우지 않는다.</b> 지우는 순간 위 사고가 난다.
/// </summary>
public static class DecalReceivers
{
    /// <summary>
    /// <c>DecalReceiver</c> 렌더링 레이어 인덱스. 이름은
    /// <c>UniversalRenderPipelineGlobalSettings</c> 의 <c>lightLayerName1</c> 에 있다
    /// (URP 기본 이름 "Light Layer 1" 을 이 용도로 명명했다 — 라벨만 바뀌고 비트는 그대로다).
    /// </summary>
    public const int LayerIndex = 1;

    /// <summary>데칼 프로젝터의 <c>renderingLayerMask</c> 에 넣을 값.</summary>
    public const uint Mask = 1u << LayerIndex;

    /// <summary>
    /// <paramref name="root"/> 아래 모든 렌더러를 데칼 수신자로 표시한다(비활성 포함).
    ///
    /// 호출처는 <b>런타임 스폰 경로</b>다 — 존 프리팹·씬을 저작하지 않으므로 팀원 작업과 머지 충돌이
    /// 없고, 존이 재스폰되면 자동으로 다시 표시된다.
    /// ⚠️ 데칼은 각 피어의 로컬 렌더링이라 <b>서버·클라 모두</b> 불려야 한다.
    /// </summary>
    /// <returns>표시한 렌더러 수. 0 이면 배선이 잘못된 것이므로 호출처가 진단을 남긴다.</returns>
    public static int Tag(GameObject root)
    {
        if (root == null) return 0;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            // 🔴 OR 다. 대입(=)으로 바꾸면 그 렌더러가 bit 0 을 잃어 조명이 빠진다.
            r.renderingLayerMask |= Mask;
        }

        return renderers.Length;
    }
}
