using System;
using System.Collections.Generic;

// 공용 후속 시전(할 일 128 5a) — 같은 칸(Q/E/우클릭/R)의 스킬이 원래 스킬 사용 뒤 잠깐 후속 스킬로 바뀐다(르블랑 W·제드 W).
// 규칙: ① 원래 스킬 사용 때 스킬이 정한 조건(PlayerSkillBase.OpensFollowUp)이면 칸이 후속으로 바뀌고 원래 쿨은 시작하지 않는다.
//       ② 후속 사용 = 원래로 복귀 + 그때 원래 쿨 시작. ③ 창(길이·시작은 스킬이 정함) 안에 안 쓰면 창 끝에 복귀 + 쿨 시작.
//       ④ 사망·대체 세트 교체·씬 이동·스킬 막힘 = 복귀 + 쿨 시작.
// 배선(서버 승인·복제·HUD)은 PlayerSkillController, 이 파일은 상태 전이만 담는 순수 로직이다(PlayerSkillFollowUpTests).

/// <summary>이번 승인이 후속 시전 장치에서 어떤 시전인가.</summary>
public enum PlayerSkillFollowUpKind
{
    Normal,         // 후속과 무관 — 기존 동작(승인 때 쿨 시작, 수동 커밋은 스킬이 시작)
    OpenFollowUp,   // 원래 스킬 — 이 시전으로 칸이 후속으로 바뀐다(쿨은 복귀 때)
    UseFollowUp     // 지금 칸에 올라온 후속 스킬 — 원래로 복귀 + 원래 쿨 시작(후속 자체 쿨 없음)
}

public static class PlayerSkillFollowUpRules
{
    public const int SlotCount = 4;

    public static PlayerSkillFollowUpKind Classify(bool isOpenFollowUp, bool hasLinkedFollowUp, bool opensFollowUp)
    {
        if (isOpenFollowUp)
            return PlayerSkillFollowUpKind.UseFollowUp;
        return hasLinkedFollowUp && opensFollowUp ? PlayerSkillFollowUpKind.OpenFollowUp : PlayerSkillFollowUpKind.Normal;
    }

    /// <summary>승인 때 이 스킬의 쿨을 시작하는가 — 후속을 여는 원래 스킬·후속 스킬은 시작하지 않는다.</summary>
    public static bool StartsCooldownOnApproval(PlayerSkillFollowUpKind kind, bool commitCooldownManually) =>
        kind == PlayerSkillFollowUpKind.Normal && !commitCooldownManually;

    /// <summary>
    /// 후속 창 시각(초, 실제 시간) — 세션 중 = <see cref="NetworkClock.UnslowedGameNow"/>(서버 시간 − 일시정지, 간파 슬로우·히트스톱 무관 —
    /// 슬로우 타임라인과 같은 도메인), 세션 밖 = unscaled 시간. Time.time 은 슬로우 중 덜 흘러 창이 늘어난다(할 일 133 i).
    /// </summary>
    public static double WindowNow(bool sessionRunning, double unslowedGameNow, double unscaledNow) =>
        sessionRunning ? unslowedGameNow : unscaledNow;

    public static bool IsSet(byte mask, int slot) => slot >= 0 && slot < SlotCount && (mask & (1 << slot)) != 0;

    public static byte With(byte mask, int slot, bool set)
    {
        if (slot < 0 || slot >= SlotCount)
            return mask;
        return set ? (byte)(mask | (1 << slot)) : (byte)(mask & ~(1 << slot));
    }

    /// <summary>칸에 지금 쓸 스킬 — 현재 세트의 그 칸 스킬 위에 후속 한 겹을 덮는다. 후속이 연결 안 됐으면 세트 스킬 그대로.</summary>
    public static PlayerSkillBase Resolve(PlayerSkillBase setSkill, byte followUpMask, int slot)
    {
        if (setSkill == null || !IsSet(followUpMask, slot))
            return setSkill;
        PlayerSkillBase followUp = setSkill.FollowUpSkill;
        return followUp != null ? followUp : setSkill;
    }
}

/// <summary>
/// [서버] 칸별 후속 상태 — 열림·원래/후속 스킬 인덱스·원래 시전 ID·창·쿨 시작 전 차감 보류. 닫는 메서드는 '지금 쿨을 시작할 원래 스킬 인덱스'를 돌려준다.
/// 시간은 호출자가 넣는다(컨트롤러 = 실제 시간 — 간파 슬로우·히트스톱에 창이 늘지 않는다, 할 일 133 i).
/// </summary>
public sealed class PlayerSkillFollowUpModel
{
    private struct Entry
    {
        public bool Open;
        public int Source;
        public int FollowUp;
        public int CastId;
        public bool WindowStarted;
        public float WindowEnd;
    }

    private readonly Entry[] entries = new Entry[PlayerSkillFollowUpRules.SlotCount];
    // 원래 스킬 인덱스 → 쿨 시작 전에 받은 쿨 차감(초). 쿨이 시작될 때 한 번 가져간다(할 일 133 h).
    private readonly Dictionary<int, float> heldReductions = new Dictionary<int, float>();

    /// <summary>후속이 열린 칸 비트(복제값).</summary>
    public byte OpenMask { get; private set; }

    public bool IsOpen(int slot) => IsValid(slot) && entries[slot].Open;
    public int SourceOf(int slot) => IsOpen(slot) ? entries[slot].Source : -1;
    public int FollowUpOf(int slot) => IsOpen(slot) ? entries[slot].FollowUp : -1;
    public int CastIdOf(int slot) => IsOpen(slot) ? entries[slot].CastId : 0;
    public bool IsWindowStarted(int slot) => IsOpen(slot) && entries[slot].WindowStarted;
    public float WindowEndOf(int slot) => IsWindowStarted(slot) ? entries[slot].WindowEnd : float.PositiveInfinity;

    /// <summary>이 원래 스킬이 지금 열린 후속에 묶여 쿨을 미루는 중인가.</summary>
    public bool HoldsSource(int sourceIndex)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Open && entries[i].Source == sourceIndex)
                return true;
        }

        return false;
    }

    /// <summary>원래 스킬 사용 — 칸을 후속으로 바꾼다. 창은 아직 시작하지 않는다(<see cref="BeginWindow"/>).</summary>
    public bool Open(int slot, int sourceIndex, int followUpIndex, int castId)
    {
        if (!IsValid(slot) || sourceIndex < 0 || followUpIndex < 0)
            return false;

        entries[slot] = new Entry { Open = true, Source = sourceIndex, FollowUp = followUpIndex, CastId = castId };
        heldReductions.Remove(sourceIndex); // 새 사용 — 이전 사용의 보류분은 남기지 않는다
        RecalculateMask();
        return true;
    }

    /// <summary>
    /// 후속에 묶여 쿨이 아직 시작 전인 원래 스킬이 받은 쿨 차감을 보류한다(쿨이 시작되면 <see cref="TakeHeldReduction"/> 으로 적용).
    /// 묶여 있지 않거나 0 이하면 false(호출자가 평소처럼 바로 줄인다).
    /// </summary>
    public bool HoldReduction(int sourceIndex, float seconds)
    {
        if (seconds <= 0f || !HoldsSource(sourceIndex))
            return false;

        heldReductions[sourceIndex] = (heldReductions.TryGetValue(sourceIndex, out float held) ? held : 0f) + seconds;
        return true;
    }

    /// <summary>쿨 시작 때 보류된 차감을 가져간다(가져가면 비움). 없으면 0.</summary>
    public float TakeHeldReduction(int sourceIndex)
    {
        if (!heldReductions.TryGetValue(sourceIndex, out float held))
            return 0f;

        heldReductions.Remove(sourceIndex);
        return held;
    }

    /// <summary>창을 지금부터 시작한다. 이미 시작했거나 그 원래 스킬의 후속이 아니면 무시. 0초 = 다음 만료 검사에 닫힌다.</summary>
    public bool BeginWindow(int slot, int sourceIndex, float now, float seconds)
    {
        if (!IsOpen(slot) || entries[slot].Source != sourceIndex || entries[slot].WindowStarted)
            return false;

        entries[slot].WindowStarted = true;
        entries[slot].WindowEnd = now + Math.Max(0f, seconds);
        return true;
    }

    /// <summary>후속 사용 — 닫고 원래 스킬 인덱스를 돌려준다. 그 칸의 후속이 아니면 -1.</summary>
    public int Consume(int slot, int followUpIndex)
    {
        if (!IsOpen(slot) || entries[slot].FollowUp != followUpIndex)
            return -1;
        return Close(slot);
    }

    /// <summary>강제 복귀(세트 교체 등) — 닫고 원래 스킬 인덱스를 돌려준다. 안 열려 있으면 -1.</summary>
    public int ForceClose(int slot) => IsOpen(slot) ? Close(slot) : -1;

    /// <summary>창이 끝난 칸을 닫고 원래 스킬 인덱스를 모은다. 닫은 개수.</summary>
    public int CollectExpired(float now, List<int> closedSources)
    {
        int closed = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (!entries[i].Open || !entries[i].WindowStarted || now < entries[i].WindowEnd)
                continue;
            closedSources?.Add(Close(i));
            closed++;
        }

        return closed;
    }

    /// <summary>모든 칸 강제 복귀(사망·막힘·씬 이동). 닫은 개수.</summary>
    public int CloseAll(List<int> closedSources)
    {
        int closed = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (!entries[i].Open)
                continue;
            closedSources?.Add(Close(i));
            closed++;
        }

        return closed;
    }

    private int Close(int slot)
    {
        int source = entries[slot].Source;
        entries[slot] = default;
        RecalculateMask();
        return source;
    }

    private void RecalculateMask()
    {
        byte mask = 0;
        for (int i = 0; i < entries.Length; i++)
            mask = PlayerSkillFollowUpRules.With(mask, i, entries[i].Open);
        OpenMask = mask;
    }

    private static bool IsValid(int slot) => slot >= 0 && slot < PlayerSkillFollowUpRules.SlotCount;
}

/// <summary>
/// [원격 오너] 후속 칸 예측 — 시전 요청을 보낼 때 칸을 먼저 바꿔 두고(즉시 반응), 서버 복제값이 같아지면 예측을 내린다.
/// 서버 거부 = 예측 폐기(서버 값으로 원복). 승인됐는데 서버가 다르게 정했으면(조건 불일치) 유예 뒤 서버 값을 따른다. 요청은 한 번에 하나다.
/// </summary>
public sealed class PlayerSkillFollowUpPrediction
{
    private int slot = -1;
    private bool open;
    private bool approved;
    private float approvedAt;

    public bool HasPrediction => slot >= 0;

    public byte Effective(byte serverMask) =>
        HasPrediction ? PlayerSkillFollowUpRules.With(serverMask, slot, open) : serverMask;

    public void Predict(int predictedSlot, bool predictedOpen)
    {
        if (predictedSlot < 0 || predictedSlot >= PlayerSkillFollowUpRules.SlotCount)
            return;

        slot = predictedSlot;
        open = predictedOpen;
        approved = false;
    }

    /// <summary>서버 복제값이 왔다 — 예측한 칸이 서버와 같아졌으면 예측을 내린다.</summary>
    public void ResolveServerMask(byte serverMask)
    {
        if (HasPrediction && PlayerSkillFollowUpRules.IsSet(serverMask, slot) == open)
            Clear();
    }

    /// <summary>서버가 시전을 거부했다 — 서버 값으로 원복.</summary>
    public void Reject() => Clear();

    /// <summary>서버가 시전을 승인했다(PlaySkillClientRpc) — 복제값이 유예 안에 안 맞으면 서버 값을 따른다.</summary>
    public void MarkApproved(float now)
    {
        if (!HasPrediction)
            return;
        approved = true;
        approvedAt = now;
    }

    /// <summary>승인 뒤 유예가 지나도 복제값이 안 맞으면 예측을 내린다. 내렸으면 true.</summary>
    public bool Tick(float now, float graceSeconds)
    {
        if (!HasPrediction || !approved || now - approvedAt < graceSeconds)
            return false;
        Clear();
        return true;
    }

    public void Clear()
    {
        slot = -1;
        open = false;
        approved = false;
    }
}
