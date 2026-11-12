using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — 처치자 이동 방식(할 일 272). 프로필 칸 <see cref="BossVictoryProfile.killerMove"/> 가 고른다(캐릭터 분기 대신 데이터).
/// </summary>
public enum BossVictoryKillerMove
{
    BackDash,       // 백대쉬(기존 — 팔라딘) → 처치 연출 스킬
    TeleportDash,   // 어쌔신 — 타임라인 '어쌔신 순간이동·Q 이동' 클립마다 순간이동 → Q 이동(272 후속), 마지막 뒤 정지 프레임
    RetreatCharge,  // 제자리. 보스와 가까우면 후퇴 스킬(E) 표시 + 백대쉬 → Q 차징, 멀면 바로 Q 차징(거너)
}

/// <summary>
/// 보스 처치 연출 처치자 이동(할 일 272) 순수 계산 — 테스트 <c>BossVictoryKillerMoveTests</c>.
/// 런타임(<see cref="BossVictoryGlitchPresentation"/>)과 타임라인 창 Scene 뷰 핸들이 같은 식을 쓴다.
/// </summary>
public static class BossVictoryKillerMoves
{
    /// <summary>어쌔신 정지 프레임 기본(Combo_Attack_03_04 60fps 71프레임 = 1.1833초) — 타임라인 '처치 연출 스킬' 클립 끝 자르기로 저장한다.</summary>
    public const int DefaultFreezeFrame = 71;

    /// <summary>순간이동 Q 기본 애니 상태 = Combo_Attack_03_04 클립을 든 상태(AssassinAnimatorController).</summary>
    public const string DefaultTeleportMoveState = "Assassin_Q_DashStrike_Transformed";

    /// <summary>
    /// 처치자 단계 시각 — 이동 방식별로 '백대쉬 시작' 단계(= 백대쉬 / 후퇴)와 '처치 연출 스킬' 단계 시각을 고른다.
    /// 백대쉬 = 마커 그대로. 순간이동 = 백대쉬 없음 · 처치 연출 단계 = 어쌔신 트랙 첫 클립 시작(없으면 무한대).
    /// 후퇴 차징 — 가까우면 백대쉬 시작 → 'Q 차징 시작' 마커, 멀면 백대쉬 없음(무한대)·백대쉬 시작 시각에 바로 Q 차징.
    /// </summary>
    public static BossVictoryKillerSequence.Times Times(BossVictoryTimeline tl, BossVictoryKillerMove move, bool tooClose)
    {
        BossVictoryKillerSequence.Times t = tl.KillerTimes();
        switch (move)
        {
            case BossVictoryKillerMove.TeleportDash:
                t.BackDashStart = float.PositiveInfinity;
                t.FinisherStart = BossVictoryAssassinDash.FirstStart(tl.assassinDash);
                return t;
            case BossVictoryKillerMove.RetreatCharge when tooClose:
                t.FinisherStart = tl.ChargeStartTime;
                return t;
            case BossVictoryKillerMove.RetreatCharge:
                t.FinisherStart = tl.backDashStartTime;
                t.BackDashStart = float.PositiveInfinity;
                return t;
            default:
                return t;
        }
    }

    /// <summary>후퇴 차징 — 처치 순간 보스와 수평 거리가 <paramref name="minDistance"/> 보다 짧은가(같으면 멂).</summary>
    public static bool IsTooClose(Vector3 killer, Vector3 boss, float minDistance)
    {
        Vector3 d = killer - boss;
        d.y = 0f;
        return d.sqrMagnitude < minDistance * minDistance;
    }

    /// <summary>보스 수평 회전(y 축만) — 기울어진 보스여도 오프셋은 바닥 평면 기준.</summary>
    public static Quaternion Yaw(Quaternion rotation) => Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);

    /// <summary>보스 기준 점 = 보스 위치 + 보스 수평 회전 × 로컬 오프셋.</summary>
    public static Vector3 BossPoint(Vector3 bossPosition, Quaternion bossRotation, Vector3 localOffset) =>
        bossPosition + Yaw(bossRotation) * localOffset;

    /// <summary><see cref="BossPoint"/> 의 역 — 월드 점 → 보스 로컬 오프셋(Scene 뷰 핸들로 끌 때).</summary>
    public static Vector3 BossLocalOffset(Vector3 bossPosition, Quaternion bossRotation, Vector3 worldPoint) =>
        Quaternion.Inverse(Yaw(bossRotation)) * (worldPoint - bossPosition);

    /// <summary>이동 진행(0~1, 곡선 적용) — 시각이 구간 앞이면 0, 뒤면 곡선 끝 값. 곡선이 없으면 선형.</summary>
    public static float MoveProgress(float time, float start, float stop, AnimationCurve ease)
    {
        float u = stop > start ? Mathf.Clamp01((time - start) / (stop - start)) : (time >= start ? 1f : 0f);
        return Ease(ease, u);
    }

    /// <summary>정지 프레임 → 클립 시간(초).</summary>
    public static float FreezeSeconds(int frame, float frameRate) => Mathf.Max(0, frame) / BossVictoryAnimTrim.FrameRate(frameRate);

    /// <summary>
    /// 순간이동 Q 이동 중 처치자 애니 배율 — Custom0 → End 클립 구간(<paramref name="clipSpan"/> 초)이 노드 길이(<paramref name="moveSeconds"/>)에 딱 맞게.
    /// 상태 속도(<paramref name="stateSpeed"/>, Combo_Attack_03_04 상태 1.5)를 나눠 Animator.speed 배율로. 길이·구간이 0 이하면 1.
    /// </summary>
    public static float MoveAnimScale(float clipSpan, float moveSeconds, float stateSpeed)
    {
        if (clipSpan <= 0f || moveSeconds <= 0f) return 1f;
        return clipSpan / moveSeconds / (stateSpeed > 0f ? stateSpeed : 1f);
    }

    /// <summary>거너 처치 연출 차징 구체 크기 = 시작 크기 → 목표 크기(성장 곡선, 진행 <paramref name="t"/> 0~1). t = 1(발사 순간) = 목표 크기.</summary>
    public static float ChargeScale(float from, float to, AnimationCurve growth, float t) =>
        Mathf.LerpUnclamped(from, to, Ease(growth, Mathf.Clamp01(t)));

    static float Ease(AnimationCurve curve, float u) =>
        curve != null && curve.length > 0 ? curve.Evaluate(u) : u;
}

/// <summary>
/// 어쌔신 순간이동·Q 이동 트랙(272 후속) 순수 계산 — 클립 = 한 번(시작에 순간이동 → 길이 동안 Q 이동). 여러 개는 시작 순(같으면 목록 순) #1 #2 …,
/// 겹치면 앞 클립은 다음 클립 시작에서 끝(<see cref="SegmentEnd"/>). 테스트 <c>BossVictoryKillerMoveTests</c>.
/// </summary>
public static class BossVictoryAssassinDash
{
    /// <summary>기본 새 클립 길이(초) = Combo_Attack_03_04 Custom0 → End 클립 구간(0.15 → 0.425).</summary>
    public const float DefaultLength = 0.275f;
    public static readonly Vector3 DefaultFrom = new Vector3(-3f, 0f, 2.5f);
    public static readonly Vector3 DefaultTo = new Vector3(3f, 0f, 2.5f);

    /// <summary>실행 순서(목록 번호) — 시작 순, 같으면 목록 순.</summary>
    public static void Order(List<BossVictoryClip> clips, List<int> order)
    {
        order.Clear();
        if (clips == null) return;
        for (int i = 0; i < clips.Count; i++) order.Add(i);
        order.Sort((a, b) => clips[a].start != clips[b].start ? clips[a].start.CompareTo(clips[b].start) : a.CompareTo(b));
    }

    /// <summary>목록 번호 <paramref name="index"/> 클립의 실행 번호(1부터, 창 '#n' 표시).</summary>
    public static int Number(List<BossVictoryClip> clips, int index)
    {
        var order = new List<int>(clips.Count);
        Order(clips, order);
        return order.IndexOf(index) + 1;
    }

    /// <summary>첫 클립 시작(없으면 무한대).</summary>
    public static float FirstStart(List<BossVictoryClip> clips)
    {
        float first = float.PositiveInfinity;
        if (clips == null) return first;
        for (int i = 0; i < clips.Count; i++) first = Mathf.Min(first, clips[i].start);
        return first;
    }

    /// <summary>순서 <paramref name="k"/> 번째 이동이 끝나는 시각 = 자기 끝과 다음 클립 시작 중 이른 쪽.</summary>
    public static float SegmentEnd(List<BossVictoryClip> clips, List<int> order, int k)
    {
        float end = clips[order[k]].End;
        return k + 1 < order.Count ? Mathf.Min(end, clips[order[k + 1]].start) : end;
    }

    /// <summary>시각 <paramref name="t"/> 에 시작한 마지막 이동의 순서(아직 없으면 -1).</summary>
    public static int Current(List<BossVictoryClip> clips, List<int> order, float t)
    {
        int k = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (clips[order[i]].start > t) break;
            k = i;
        }
        return k;
    }

    /// <summary>모든 이동이 끝났는가(클립 없으면 true).</summary>
    public static bool AllDone(List<BossVictoryClip> clips, List<int> order, float t) =>
        order.Count == 0 || t >= SegmentEnd(clips, order, order.Count - 1);

    /// <summary>이동 진행(곡선 적용) — 클립 전체 길이 기준, 겹쳐 잘린 클립은 잘린 시각의 진행에서 멈춘다.</summary>
    public static float Progress(List<BossVictoryClip> clips, List<int> order, int k, float t, AnimationCurve ease)
    {
        BossVictoryClip c = clips[order[k]];
        return BossVictoryKillerMoves.MoveProgress(Mathf.Min(t, SegmentEnd(clips, order, k)), c.start, c.End, ease);
    }
}
