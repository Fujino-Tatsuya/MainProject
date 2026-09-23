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
    /// 바닥 전용 각도 페이드. 실제 기울기 35° 에서 흐려지기 시작해 55° 에서 사라진다 —
    /// 바닥(0°)은 그대로, 벽(90°)은 안 칠해진다.
    ///
    /// 🔴 <b>이 값은 "도"가 아니다.</b> URP 는 x = ((1 - cos θ)/2)² 를 start/180 · end/180 과 비교한다
    /// (<c>DecalEntityManager.cs</c> 의 angleFade 계산). 실제 각도 θ 의 설정값은
    /// <c>180 · ((1 - cos θ)/2)²</c> 이고, <b>수직벽(90°)이 45 에 해당한다.</b>
    /// 그래서 end 를 45 이상으로 두면(35/55, 30/60, 0/90 …) 벽이 항상 50% 남는다 — 2026-09-23 에
    /// 값을 여러 번 바꿔도 "무시된다"고 보였던 이유. 35°→1.47, 55°→8.18.
    /// ⚠️ 셰이더 그래프의 Angle Fade 도 켜져 있어야 한다(<c>SG_DecalFloorOnly</c>·<c>SG_ColoredDecal</c>).
    ///    URP 기본 <c>Decal.shadergraph</c> 는 꺼져 있어 이 값을 통째로 무시한다.
    ///
    /// 🔴 왜 필요한가(2026-09-23): 아레나는 <b>루트째</b> 수신자로 표시돼 벽도 비트 1 을 갖는다.
    /// 각도 페이드가 꺼져 있으면(180/180) 투영 상자 안에 들어온 벽면까지 예고가 타고 올라간다.
    /// 수신자에서 벽을 빼는 것보다 프로젝터가 거르는 쪽이 존·아레나 저작과 무관해서 이쪽을 쓴다.
    /// </summary>
    public const float FloorAngleFadeStart = 1.47f;
    public const float FloorAngleFadeEnd = 8.18f;

    /// <summary>
    /// 예고·장판 프로젝터의 공통 계약 — 수신자 마스크(캐릭터 제외) + 바닥 전용 각도 페이드(벽 제외).
    /// 프로젝터를 만드는 곳마다 이 한 줄만 부른다.
    /// </summary>
    public static void ConfigureFloorProjector(UnityEngine.Rendering.Universal.DecalProjector decal)
    {
        if (decal == null) return;
        decal.renderingLayerMask = Mask;
        decal.startAngleFade = FloorAngleFadeStart;
        decal.endAngleFade = FloorAngleFadeEnd;
    }

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
        int tagged = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            // 벽·기둥은 수신자에서 뺀다. 각도 페이드가 수직면은 지우지만, 벽 메시에 붙은
            // 위를 향한 면(루버 판자·기둥 밑동·벽 상단)은 바닥과 각도로 구분되지 않는다(2026-09-23 실측).
            // 🔴 단, 게임플레이 오브젝트(Unit — 송전탑 등)는 모양과 무관하게 남긴다. 빼면 표식이
            //    프롭 위로 이어지지 않고 프롭이 표식을 가린다(MapContentSpawner 주석). 벽 판정은 건축물에만.
            if (IsWallLike(r.bounds) && r.GetComponentInParent<Unit>(true) == null) continue;

            // 🔴 OR 다. 대입(=)으로 바꾸면 그 렌더러가 bit 0 을 잃어 조명이 빠진다.
            r.renderingLayerMask |= Mask;
            tagged++;
        }

        return tagged;
    }

    /// <summary>벽으로 보는 최소 높이(m). 낮은 상자·단차는 계속 칠해진다.</summary>
    public const float WallMinHeight = 1.2f;

    /// <summary>
    /// 월드 경계 상자로 벽을 가른다 — <b>높고, 높이가 수평 두 변 중 짧은 쪽보다 큰 것</b>.
    /// 긴 벽(30×3×0.5)·기둥(0.8×4×0.8)은 벽, 바닥·철망(평평)·경사로(6×1.5×3)·낮은 프롭은 바닥 쪽이다.
    /// ⚠️ ㄱ자로 합쳐진 벽 메시처럼 수평 두 변이 모두 긴 렌더러는 벽으로 못 가른다.
    /// </summary>
    public static bool IsWallLike(Bounds b)
    {
        Vector3 s = b.size;
        return s.y > WallMinHeight && s.y > Mathf.Min(s.x, s.z);
    }
}
