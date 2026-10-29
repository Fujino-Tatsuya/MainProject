using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 간파 창 '이번 열림 길이' 해석기(할 일 139-6) — 간파 시도 예측 입력의 창 길이(<c>WindowExpectedDuration</c>)를 <b>로컬로 아는 값</b>에서 먼저 찾는다.
/// <list type="bullet">
/// <item>값 = 게임초(스케일 시간 — 창 타이머가 흐르는 시간). <see cref="Unbounded"/>(+∞) = 시간으로 닫히지 않음(성공으로만 닫힘),
///       <see cref="Unknown"/>(-1) = 모름 → 관측 중앙값(<see cref="InterruptWindowHistory.TryGetTypicalOpenDuration"/>) 폴백.</item>
/// <item>대상 종류마다 어댑터 하나 — 이미 공개된 값·클라에 있는 SO/프리팹 데이터·이미 오는 복제값/RPC 만 읽는다.
///       새 전송 없음, 대상(보스·몬스터) 코드 무수정이 원칙이다. 앞에 등록된 어댑터부터 묻고, 맡은 첫 어댑터의 값을 쓴다.</item>
/// <item>서버(<see cref="InterruptLagMonitor.EstimateWindow"/>)·오너 클라(<see cref="InterruptWindowReplicaTracker.Estimate"/>) 공용.
///       예측(연출 강·약)에만 쓰고 판정에는 쓰지 않는다.</item>
/// </list>
/// 창 종류별 출처 표: Docs/tech/dash-interrupt.md §5 '창 길이 출처'.
/// </summary>
public static class InterruptWindowLengthResolver
{
    /// <summary>모름 — 관측 중앙값으로 폴백한다.</summary>
    public const float Unknown = -1f;

    /// <summary>무기한 — 시간으로 닫히지 않는다. 예측기의 남은 창 = +∞ − 경과 = +∞ 라 '창 닫힘으로 약'이 나오지 않는다.</summary>
    public const float Unbounded = float.PositiveInfinity;

    /// <summary>
    /// 어댑터 — 이 창 소유자를 맡으면 true 와 길이(게임초·<see cref="Unbounded"/>·<see cref="Unknown"/>), 안 맡으면 false(다음 어댑터).
    /// <paramref name="isServer"/> = 서버 예측인가(서버만 아는 값이 있는 대상 — 예: 랩 허수아비 게이트).
    /// </summary>
    public delegate bool Adapter(GameObject owner, bool isServer, out float length);

    // 기본 어댑터 — 허수아비(단계 1)·중간보스·로비 스파링 봇(단계 2). 맡는 대상이 겹치지 않는다(허수아비엔 MonsterBase 가 없다).
    // 할 일 139-6 단계 3~4 에서 23호 어댑터가 여기 붙는다.
    private static readonly List<Adapter> Adapters = new List<Adapter> { TryTrainingDummy, TryMidBossCounterWindow };

    /// <summary>어댑터를 뒤에 붙인다(이미 있으면 그대로).</summary>
    public static void Register(Adapter adapter)
    {
        if (adapter != null && !Adapters.Contains(adapter))
            Adapters.Add(adapter);
    }

    public static void Unregister(Adapter adapter) => Adapters.Remove(adapter);

    /// <summary>창 소유자의 이번 열림 길이(게임초). 맡는 어댑터가 없거나 값이 이상하면(NaN·음수) <see cref="Unknown"/>.</summary>
    public static float Resolve(GameObject owner, bool isServer)
    {
        if (owner == null)
            return Unknown;

        for (int i = 0; i < Adapters.Count; i++)
        {
            if (Adapters[i](owner, isServer, out float length))
                return Sanitize(length);
        }

        return Unknown;
    }

    /// <summary>예측 입력의 창 길이 — 로컬로 아는 길이(무기한 포함) 우선, 모르면 관측 추정.</summary>
    public static float PreferLocal(float localLength, float observedLength) =>
        localLength >= 0f ? localLength : observedLength;

    /// <summary>
    /// 허수아비(<see cref="TrainingDummyInterrupt"/>) 창 길이. 게이트 없음(본게임 맵씬·튜토리얼) = 무기한 — 모든 피어가 안다.
    /// 랩 게이트(<c>DebugDummyInterruptWindowMode</c>)가 있으면 서버(솔로 호스트 포함)는 게이트 값 그대로,
    /// 원격 클라는 모름 — Play 중 모드·길이 변경(호스트 패널)이 복제되지 않아 클라의 게이트 값은 틀릴 수 있다(관측 중앙값 폴백).
    /// </summary>
    public static float ForTrainingDummy(bool isServer, ITrainingDummyWindowGate gate)
    {
        if (gate == null)
            return Unbounded;
        return isServer ? gate.OpenWindowLength : Unknown;
    }

    private static bool TryTrainingDummy(GameObject owner, bool isServer, out float length)
    {
        if (!owner.TryGetComponent(out TrainingDummyInterrupt dummy))
        {
            length = Unknown;
            return false;
        }

        length = ForTrainingDummy(isServer, dummy.ServerWindowGate);
        return true;
    }

    /// <summary>
    /// 중간보스(SpinnerBot·GauntletBot·WallBot)·로비 스파링 봇(<see cref="TrainingSparringBot"/>) 창 길이 = <see cref="MonsterCounterWindow.WindowDuration"/>
    /// (저작값 ÷ 몬스터 SO 공격속도). 네 봇 모두 창을 이 길이로 열고 같은 길이의 단계 타이머로 닫는다.
    /// 저작값·SO 는 모든 피어가 같은 에셋이라 서버·원격 클라가 같은 값이다(공격속도는 버프·디버프 없는 SO 값 — <see cref="MonsterBase.AttackAnimSpeed"/>).
    /// 성공·피격·상태 이탈·Gauntlet 타격 순간(OnAttackHit) 같은 조기 닫힘은 복제 bool 로만 안다.
    /// 🔴 타입으로 한정한다 — 23호 프리팹에도 <see cref="MonsterCounterWindow"/> 가 붙어 있지만 23호 창은 자기 계통이라 이 길이가 아니다.
    /// </summary>
    private static bool TryMidBossCounterWindow(GameObject owner, bool isServer, out float length)
    {
        length = Unknown;
        if (!owner.TryGetComponent(out MonsterBase monster)
            || !(monster is SpinnerBot || monster is GauntletBot || monster is WallBot || monster is TrainingSparringBot))
            return false;

        // 카운터 없음(컴포넌트 없음·길이 0)이면 창이 열리지 않는다 — 모름.
        if (owner.TryGetComponent(out MonsterCounterWindow counter) && counter.WindowDuration > 0f)
            length = counter.WindowDuration;
        return true;
    }

    private static float Sanitize(float length) =>
        float.IsNaN(length) || length < 0f ? Unknown : length;
}
