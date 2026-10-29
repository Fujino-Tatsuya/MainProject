using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [각 피어] 간파 로컬 선착순 장부(할 일 143-2·143-3) — 오너 화면 연출을 1차로 막는다. 순수 로직(EditMode 테스트 대상).
/// 최종 판정은 서버 장부(<see cref="InterruptWindowClaims"/>)다 — 이 장부는 오너 시도 예측(암전·빛 모임·섬광)만 약으로 내린다.
///
/// <list type="bullet">
/// <item>창 = 대상의 카운터 창 복제값이 한 번 열린 것. <see cref="WindowOpened"/> 가 그 대상 기록을 지운다(새 창 = 초기화).
///       서버 장부와 같은 전이(같은 복제값의 열림)를 쓴다 — <see cref="InterruptWindowReplicaTracker.Record"/> 가 넘긴다.</item>
/// <item>닫힘 = 이 창에서 다른 플레이어의 강 시도(<see cref="SlowMotionPurpose.Attempt"/>, <see cref="ObserveAttempt"/>) 또는 성공
///       (<see cref="ObserveSuccess"/>)을 받았다. 약 시도(<see cref="SlowMotionPurpose.AttemptWeak"/>)는 닫지 않는다 — 그 사람이 실패하면
///       내가 첫 성공일 수 있다. 내 것도 닫지 않는다.</item>
/// <item>다시 열림(143-3) = 창을 닫은 강 시도가 전부 실패(<see cref="AttemptEnded"/> — 슬로우 Failed·ForcedReset, 발동 번호로 연결)했고
///       성공은 하나도 없다. 성공으로 닫힌 창은 새 창이 열릴 때까지 닫힘이다.</item>
/// <item>창이 닫힌 전이는 기록을 바꾸지 않는다 — 닫힌 창은 복제값만으로 이미 약 예측이다.</item>
/// </list>
/// 키 = 대상 NetworkObjectId(0 = 대상 없음) · 시전자·보는 사람 = clientId. 신호 = 슬로우 Started/Failed/ForcedReset(디렉터가 넘긴다).
/// 대상은 각 피어가 시전자 위치·방향과 예측 대상 캐시로 다시 찾는다 — 슬로우 메시지 모양은 그대로다.
/// 상대 신호가 닿기 전 동시 입력은 못 막는다 — 서버 판정 뒤 암전 걷힘이 처리한다. 문서: Docs/tech/interrupt-presentation.md §0.10.
/// </summary>
public sealed class InterruptLocalWindowClaims
{
    private sealed class Claim
    {
        public readonly Dictionary<uint, ulong> PendingAttempts = new Dictionary<uint, ulong>(); // 이 창을 닫은 남의 강 시도 발동 번호 → 시전자
        public bool Succeeded;                                                                  // 남의 성공을 받았다 — 새 창 전까지 닫힘
    }

    private readonly Dictionary<ulong, Claim> _claims = new Dictionary<ulong, Claim>();
    private readonly List<ulong> _reopened = new List<ulong>();

    /// <summary>[각 피어] 런타임 장부 하나 — 디렉터(신호)·창 복제 기록(초기화)·오너 시도 예측(읽기)이 같이 쓴다.</summary>
    public static InterruptLocalWindowClaims Local { get; } = new InterruptLocalWindowClaims();

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 기록이 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Local.Clear();

    /// <summary>새 창이 열렸다 — 그 대상의 닫힘 기록을 버린다.</summary>
    public void WindowOpened(ulong target) => _claims.Remove(target);

    /// <summary>
    /// 시도 슬로우 시작 하나를 본다 — <paramref name="caster"/> 가 <paramref name="viewer"/>(이 피어) 가 아니고 강 시도면
    /// 그 대상의 이 창을 <paramref name="triggerId"/> 로 닫는다(그 시도가 실패하면 다시 열린다). 닫았으면 true.
    /// </summary>
    public bool ObserveAttempt(ulong target, uint triggerId, ulong caster, SlowMotionPurpose purpose, ulong viewer)
    {
        if (target == 0 || triggerId == 0 || caster == viewer || purpose != SlowMotionPurpose.Attempt)
            return false;

        ClaimOf(target).PendingAttempts[triggerId] = caster;
        return true;
    }

    /// <summary>
    /// 성공 슬로우 시작 하나를 본다 — 남의 성공이면 그 대상(0 = 못 찾음)과, 그 시전자의 강 시도가 닫아 둔 창을
    /// 새 창 전까지 닫는다(성공은 실패 신호로 다시 열리지 않는다). 닫은 창이 있으면 true.
    /// </summary>
    public bool ObserveSuccess(ulong target, ulong caster, ulong viewer)
    {
        if (caster == viewer)
            return false;

        bool any = false;
        foreach (Claim claim in _claims.Values)
        {
            if (!claim.PendingAttempts.ContainsValue(caster))
                continue;

            claim.Succeeded = true;
            any = true;
        }

        if (target != 0)
        {
            ClaimOf(target).Succeeded = true;
            any = true;
        }

        return any;
    }

    /// <summary>
    /// 슬로우가 실패·강제 리셋으로 끝났다 — 그 발동 번호의 강 시도를 닫은 창에서 빼고, 남은 시도도 성공도 없으면 다시 연다.
    /// 다시 열린 대상 수를 돌려준다.
    /// </summary>
    public int AttemptEnded(uint triggerId)
    {
        if (triggerId == 0)
            return 0;

        _reopened.Clear();
        foreach (KeyValuePair<ulong, Claim> pair in _claims)
        {
            Claim claim = pair.Value;
            if (claim.PendingAttempts.Remove(triggerId) && !claim.Succeeded && claim.PendingAttempts.Count == 0)
                _reopened.Add(pair.Key);
        }

        for (int i = 0; i < _reopened.Count; i++)
            _claims.Remove(_reopened[i]);

        return _reopened.Count;
    }

    /// <summary>이 창을 다른 플레이어가 먼저 잡았다고 봤는가 — 오너 시도 예측이 창 닫힘으로 친다.</summary>
    public bool IsClosed(ulong target) =>
        target != 0 && _claims.TryGetValue(target, out Claim claim) && (claim.Succeeded || claim.PendingAttempts.Count > 0);

    /// <summary>대상이 디스폰된다 — 기록을 지운다(id 재사용 대비).</summary>
    public void Forget(ulong target) => _claims.Remove(target);

    public void Clear() => _claims.Clear();

    private Claim ClaimOf(ulong target)
    {
        if (!_claims.TryGetValue(target, out Claim claim))
        {
            claim = new Claim();
            _claims.Add(target, claim);
        }

        return claim;
    }
}
