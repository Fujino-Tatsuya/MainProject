using UnityEngine;

/// <summary>
/// "밟을 수 있는 바닥"을 찾는 공용 판정. 같은 실수가 네 번 반복돼서 한 곳으로 모았다.
///
/// 반복된 함정 4가지 — 여기서 전부 처리한다:
/// <list type="number">
/// <item><b>레이어</b>: 생성맵 바닥·벽은 <c>Ground</c>가 아니라 <b>Default</b>다(존 프리팹 전수 확인).
///   <c>LayerMask.GetMask("Ground")</c>만 쓰면 구조적으로 빗나간다.</item>
/// <item><b>원점</b>: 대상 지점이 바닥면과 같은 높이거나 미세하게 아래면, 표면에서 시작한 광선이
///   MeshCollider 윗면을 놓친다(뒷면은 안 맞는다). 항상 위에서 아래로 훑는다.</item>
/// <item><b>유닛 콜라이더</b>: Default 레이어를 포함하면 <b>보스 공격 히트박스</b>도 후보가 된다
///   (No.23은 Rage·DashAttack·Floor 등 Default 레이어 콜라이더가 7개다. 그래서 폭탄이 보스 몸통
///   높이 y≈1.8에 "착지"했다). 유닛(<see cref="Unit"/>) 계층 콜라이더는 바닥이 아니므로 제외한다.</item>
/// <item><b>조용한 실패</b>: 못 찾았을 때 아무 로그도 없으면 "폭탄이 공중에 뜬다" 같은 증상만 남고
///   원인을 못 짚는다. 호출부가 <paramref name="report"/>를 그대로 로그에 실어 보낸다.</item>
/// </list>
/// </summary>
public static class GroundProbe
{
    public const float ProbeUp = 2f;
    public const float ProbeDistance = 200f;

    /// <summary>
    /// 바닥면 위로 띄우는 표준 간격. 장판·폭탄처럼 바닥에 눕는 것들이 표면과 **정확히 같은 높이**면
    /// z-fighting이 난다. 반대로 절대 Y를 상수로 고정하면 안 된다 — 생성맵 보스룸 슬래브 윗면은
    /// 0.50이고 BossScene은 0이라, 상수로 박으면 한쪽에서 바닥 아래로 묻힌다.
    /// 항상 "찾은 바닥 + 이 간격"으로 쓴다.
    ///
    /// 🔴 **이 값으로 "바닥 단차"를 넘으려 하지 말 것**(2026-09-03 팀장 확정).
    ///    보스룸 아레나에는 보행면(0.50) 위에 <c>Env_Floor_bosscharger</c> 바닥판(0.56, MeshCollider
    ///    포함)이 얹혀 있어서, 이 값이 그 6cm 단차보다 낮으면 장판이 판에 묻힌다. 이 프로브는
    ///    마스크(Default·Ground)에 그 판도 넣으므로 판 위를 찍으면 반대로 위로 나온다 —
    ///    그래서 <b>같은 장판이 위로도 아래로도 보인다.</b>
    ///
    ///    그래서 0.12 로 올려 봤고 <b>되돌렸다.</b> 탑다운에서 띄운 만큼 장판이 바닥 기준으로
    ///    밀려 보이는데(시차), 그건 "예고가 판정에 대해 거짓말하지 않는다"는 규약을 깨서
    ///    받아들일 수 없다는 판단이다. <b>단차·가려짐은 높이로 풀 문제가 아니다</b> —
    ///    깊이 비교를 없애거나(ZTest Always + 스텐실) 표면에 투영해야(URP 데칼) 시차 0 이 된다.
    ///    그 작업이 들어가면 이 값은 <b>0 이 된다</b>(CONTEXT.md 별건 항목).
    /// </summary>
    public const float SurfaceOffset = 0.05f;

    /// <summary>찾은 바닥 위에 눕힐 Y. <see cref="SurfaceOffset"/>만큼 띄운다.</summary>
    public static float SurfaceY(in RaycastHit ground) => ground.point.y + SurfaceOffset;

    static readonly RaycastHit[] Buffer = new RaycastHit[16];

    /// <summary>
    /// <paramref name="point"/> 위쪽에서 아래로 훑어 가장 가까운 바닥을 찾는다.
    /// </summary>
    /// <param name="extraMask">호출부가 저작한 마스크. Default+Ground는 항상 함께 포함한다.</param>
    /// <param name="report">진단용 한 줄(성공: 맞힌 콜라이더/높이, 실패: 제외 내역). 로그에 그대로 싣는다.</param>
    public static bool TryFindGround(Vector3 point, int extraMask, out RaycastHit ground, out string report)
    {
        int mask = extraMask | LayerMask.GetMask("Default", "Ground");
        Vector3 origin = point + Vector3.up * ProbeUp;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, Buffer, ProbeDistance, mask,
                                            QueryTriggerInteraction.Ignore);

        ground = default;
        bool found = false;
        int skippedUnits = 0;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Buffer[i];

            // 유닛(보스/플레이어) 콜라이더는 바닥이 아니다 — 공격 히트박스가 Default 레이어에 있다.
            if (hit.collider.GetComponentInParent<Unit>() != null)
            {
                skippedUnits++;
                continue;
            }

            // 아래로 쏘므로 y가 가장 높은 것이 가장 가까운 바닥이다.
            if (!found || hit.point.y > ground.point.y)
            {
                ground = hit;
                found = true;
            }
        }

        report = found
            ? $"바닥={ground.collider.name}(layer {ground.collider.gameObject.layer}) y={ground.point.y:F2}" +
              (skippedUnits > 0 ? $", 유닛 콜라이더 {skippedUnits}개 제외" : "")
            : $"바닥 없음 — 후보 {count}개 중 유닛 {skippedUnits}개 제외, 마스크 {mask}. " +
              "그 지점에 바닥 콜라이더가 있는지 확인하세요(생성맵 바닥은 Default).";

        return found;
    }
}
