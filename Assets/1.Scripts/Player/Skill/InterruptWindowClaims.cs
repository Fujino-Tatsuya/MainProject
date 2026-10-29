using System.Collections.Generic;
using UnityEngine;

/// <summary>간파 선착순 판정(할 일 143) — 이 시전이 그 대상의 창에서 어떤 자리인가.</summary>
public enum InterruptClaimVerdict
{
    /// <summary>아직 아무도 이 창을 성공시키지 않았다(또는 이 시전이 선점 뒤에 새로 시작했다) — 지금처럼 처리.</summary>
    Free,
    /// <summary>이 창의 첫 성공자가 이 시전자 자신이다 — 지금처럼 처리.</summary>
    Mine,
    /// <summary>다른 시전자가 이 창을 먼저 성공시켰다 — 피해만(간파 판정 없음 · 반응·연출 없음).</summary>
    Preempted,
}

/// <summary>
/// [서버] 간파 선착순 장부(할 일 143) — 대상(카운터 창 소유자)마다 지금 창의 첫 성공자를 적는다. 순수 로직(EditMode 테스트 대상).
///
/// <list type="bullet">
/// <item>창 = 대상의 카운터 창이 한 번 열린 것. <see cref="WindowOpened"/> 가 그 대상의 기록을 새로 시작한다(새 창 = 초기화).</item>
/// <item>첫 성공(<see cref="Claim"/>)은 그 창의 기록에 남는다. 보스는 첫 성공을 받는 그 수신 호출 안에서 창을 닫으므로
///       닫힘(<see cref="WindowClosed"/>)은 기록을 지우지 않고 '닫힘'만 표시한다.</item>
/// <item>판정(<see cref="Evaluate"/>) — 다른 사람이 성공시킨 창이 열려 있거나, 닫혔어도 이 시전이 그 첫 성공 이전(같은 시각 포함)에
///       시작했으면 <see cref="InterruptClaimVerdict.Preempted"/>. 닫힌 뒤 새로 시작한 시전은 <see cref="InterruptClaimVerdict.Free"/>
///       (= 닫힘 초기화 — 취약 중 간파 넉백 같은 기존 규칙이 그대로 돈다).</item>
/// </list>
/// 키 = 대상 GameObject 인스턴스 id · 시전자(Player) 인스턴스 id. 시각 = 같은 시계(서버 <c>Time.time</c>)면 된다.
/// 창 전이는 <see cref="InterruptLagMonitor.RecordWindow"/> 가 넘긴다(보스·허수아비 코드 무수정). 문서: Docs/tech/interrupt-presentation.md §0.10.
/// </summary>
public sealed class InterruptWindowClaims
{
    private struct Record
    {
        public bool Open;
        public bool Claimed;
        public int Claimant;
        public float ClaimTime;
    }

    private readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();

    /// <summary>[서버] 런타임 장부 하나 — 간파 판정(<see cref="PlayerInterruptSkillBase"/>)과 창 전이 기록이 같이 쓴다.</summary>
    public static InterruptWindowClaims Server { get; } = new InterruptWindowClaims();

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 기록이 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Server.Clear();

    /// <summary>창 전이 하나 — 열림 = <see cref="WindowOpened"/>, 닫힘 = <see cref="WindowClosed"/>.</summary>
    public void RecordWindow(int target, bool open)
    {
        if (open)
            WindowOpened(target);
        else
            WindowClosed(target);
    }

    /// <summary>새 창이 열렸다 — 그 대상의 앞 창 기록(첫 성공자 포함)을 버린다.</summary>
    public void WindowOpened(int target) => _records[target] = new Record { Open = true };

    /// <summary>창이 닫혔다 — 첫 성공자는 남긴다(그 창에 이미 시작한 시전만 거른다). 기록이 없으면 아무것도 안 한다.</summary>
    public void WindowClosed(int target)
    {
        if (!_records.TryGetValue(target, out Record record))
            return;

        record.Open = false;
        _records[target] = record;
    }

    /// <summary>
    /// 이 시전(<paramref name="claimant"/>, 시작 시각 <paramref name="castStartTime"/>)이 그 대상에 간파로 들어가도 되는가.
    /// </summary>
    public InterruptClaimVerdict Evaluate(int target, int claimant, float castStartTime)
    {
        if (!_records.TryGetValue(target, out Record record) || !record.Claimed)
            return InterruptClaimVerdict.Free;

        if (record.Claimant == claimant)
            return InterruptClaimVerdict.Mine;

        return record.Open || castStartTime <= record.ClaimTime
            ? InterruptClaimVerdict.Preempted
            : InterruptClaimVerdict.Free;
    }

    /// <summary>
    /// 그 대상의 지금 창을 <paramref name="claimant"/> 가 성공시켰다(서버 판정 순간 <paramref name="time"/>).
    /// 이미 첫 성공자가 있으면 바꾸지 않고 false. 열린 기록이 없으면(전이를 못 들은 대상) 닫힌 창 기록으로 남긴다.
    /// </summary>
    public bool Claim(int target, int claimant, float time)
    {
        _records.TryGetValue(target, out Record record);
        if (record.Claimed)
            return false;

        record.Claimed = true;
        record.Claimant = claimant;
        record.ClaimTime = time;
        _records[target] = record;
        return true;
    }

    // 디스폰 때 지우지 않는다 — 인스턴스 id 는 세션 안에서 다시 쓰이지 않아 남은 기록이 다른 대상에 붙지 않는다(보스 몇 개라 크기도 무시).
    public void Clear() => _records.Clear();
}
