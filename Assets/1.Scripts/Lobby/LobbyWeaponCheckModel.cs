using System;
using System.Collections.Generic;

/// <summary>TRAIN_01 무기 점검 개인 항목(할 일 86 PLAN §3.3, 명세 v0.2 §7) — 값 = 체크 마스크 비트 번호이자 표시 순서.</summary>
public enum LobbyWeaponCheckItem : byte
{
    BasicAttack = 0,
    SkillQ = 1,
    SkillE = 2,
    SkillR = 3,
    RightClick = 4,     // 명세 '간파' 단독 체크 대신 '우클릭 스킬'
    Dash = 5,
}

/// <summary>
/// TRAIN_01 무기 점검 판정 규칙 — 서버 신호(존1 훅 재사용)를 개인 항목으로 바꾼다. Unity·NGO 비의존(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>기본 공격 = <c>Player.ServerBasicAttackStarted</c>(한 타 실제 시작, 적중 무관).</item>
/// <item>Q·E·R·우클릭 = <c>PlayerSkillController.ServerSkillUsed</c>(서버 승인 — 거부·조준 취소는 안 온다). 일반·변신 Q/E 는 같은 슬롯 = 같은 항목.</item>
/// <item>발사형(거너 Q — 승인 = 충전 시작)은 승인으로 세지 않고 발사 신호(<c>GunnerChargeLaserSkill.ServerFired</c>)로 센다.</item>
/// <item>대시 = <c>PlayerDashController.ServerDashApproved</c>.</item>
/// </list>
/// </summary>
public static class LobbyWeaponCheckRules
{
    public const int ItemCount = 6;

    /// <summary>6항목 전부.</summary>
    public const int FullMask = (1 << ItemCount) - 1;

    /// <summary>체크리스트 표시 순서(기본 공격·Q·E·R·우클릭·대시).</summary>
    public static readonly LobbyWeaponCheckItem[] DisplayOrder =
    {
        LobbyWeaponCheckItem.BasicAttack, LobbyWeaponCheckItem.SkillQ, LobbyWeaponCheckItem.SkillE,
        LobbyWeaponCheckItem.SkillR, LobbyWeaponCheckItem.RightClick, LobbyWeaponCheckItem.Dash,
    };

    public static int BitOf(LobbyWeaponCheckItem item) => 1 << (int)item;

    public static bool Has(int mask, LobbyWeaponCheckItem item) => (mask & BitOf(item)) != 0;

    public static bool IsComplete(int mask) => (mask & FullMask) == FullMask;

    /// <summary>체크한 항목 수.</summary>
    public static int CountOf(int mask)
    {
        var n = 0;
        for (var i = 0; i < ItemCount; i++)
        {
            if ((mask & (1 << i)) != 0)
                n++;
        }

        return n;
    }

    /// <summary>스킬 슬롯 → 항목. 모르는 슬롯이면 false.</summary>
    public static bool TryGetSkillItem(PlayerSkillSlot slot, out LobbyWeaponCheckItem item)
    {
        switch (slot)
        {
            case PlayerSkillSlot.Main: item = LobbyWeaponCheckItem.SkillQ; return true;
            case PlayerSkillSlot.Sub: item = LobbyWeaponCheckItem.SkillE; return true;
            case PlayerSkillSlot.Ultimate: item = LobbyWeaponCheckItem.SkillR; return true;
            case PlayerSkillSlot.Interrupt: item = LobbyWeaponCheckItem.RightClick; return true;
            default: item = default; return false;
        }
    }

    /// <summary>
    /// 승인 신호로 셀 항목인가. <paramref name="countsOnFire"/> = 그 슬롯 스킬이 발사 신호로 세는 스킬(거너 Q)이면 승인은 무시한다.
    /// </summary>
    public static bool TryGetApprovalItem(PlayerSkillSlot slot, bool countsOnFire, out LobbyWeaponCheckItem item)
    {
        if (countsOnFire)
        {
            item = default;
            return false;
        }

        return TryGetSkillItem(slot, out item);
    }
}

/// <summary>
/// [서버] TRAIN_01 개인 체크 장부 — 명세 v0.2 §7 "사용 완료". NGO 비의존(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>시작(<see cref="Begin"/>) 이후 행동만 센다 — 시작 전·종료 뒤 보고는 무시.</item>
/// <item>대상 = 시작 때 넣은 훈련 명단(입구 확정 때 고정)뿐. 늦은 참가자·명단 밖 보고는 무시.</item>
/// <item>항목별 최초 1회. 보고자 본인 마스크에만 기록(다른 사람이 대신 완료 불가 — 호출자가 행동한 플레이어의 clientId 를 넘긴다).</item>
/// <item>명단 전원 6/6 → <see cref="TryConsumeAllComplete"/> 가 한 번만 true(단계 완료 보고 1회). 시간 초과 자동 완료 없음.</item>
/// <item>명단 사람이 연결 종료하면 명단에서 빠진다(남은 사람만으로 완료 판정).</item>
/// </list>
/// </summary>
public sealed class LobbyWeaponCheckModel
{
    readonly List<ulong> _roster = new List<ulong>();
    readonly Dictionary<ulong, int> _masks = new Dictionary<ulong, int>();
    bool _completionReported;

    /// <summary>점검 진행 중(시작 ~ 종료).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>대상 명단(시작 때 고정, 연결 종료로만 준다).</summary>
    public IReadOnlyList<ulong> Roster => _roster;

    /// <summary>시작 — 명단 고정, 체크 전부 비움. 이미 진행 중이면 처음부터 다시.</summary>
    public void Begin(IEnumerable<ulong> roster)
    {
        _roster.Clear();
        _masks.Clear();
        _completionReported = false;
        if (roster != null)
        {
            foreach (var clientId in roster)
            {
                if (_masks.ContainsKey(clientId))
                    continue;

                _roster.Add(clientId);
                _masks[clientId] = 0;
            }
        }

        IsRunning = true;
    }

    /// <summary>종료(단계를 떠남) — 이후 보고는 무시. 기록은 비운다.</summary>
    public void Stop()
    {
        IsRunning = false;
        _roster.Clear();
        _masks.Clear();
        _completionReported = false;
    }

    /// <summary>이 사람의 행동 보고. 처음 체크된 항목이면 true(복제 갱신할 때), 무시·중복이면 false.</summary>
    public bool Report(ulong clientId, LobbyWeaponCheckItem item)
    {
        if (!IsRunning || !_masks.TryGetValue(clientId, out var mask))
            return false;

        var bit = LobbyWeaponCheckRules.BitOf(item);
        if ((mask & bit) != 0 || (bit & LobbyWeaponCheckRules.FullMask) == 0)
            return false;

        _masks[clientId] = mask | bit;
        return true;
    }

    /// <summary>연결 종료 — 명단·기록에서 뺀다. 명단에 있었으면 true.</summary>
    public bool RemoveClient(ulong clientId)
    {
        if (!_masks.Remove(clientId))
            return false;

        _roster.Remove(clientId);
        return true;
    }

    public bool IsInRoster(ulong clientId) => _masks.ContainsKey(clientId);

    /// <summary>이 사람의 체크 마스크(명단 밖이면 0).</summary>
    public int MaskOf(ulong clientId) => _masks.TryGetValue(clientId, out var mask) ? mask : 0;

    /// <summary>6/6 을 마친 명단 인원.</summary>
    public int CompletedCount
    {
        get
        {
            var n = 0;
            foreach (var clientId in _roster)
            {
                if (LobbyWeaponCheckRules.IsComplete(_masks[clientId]))
                    n++;
            }

            return n;
        }
    }

    /// <summary>진행 중이고 명단이 비어 있지 않고 전원 6/6.</summary>
    public bool IsAllComplete => IsRunning && _roster.Count > 0 && CompletedCount == _roster.Count;

    /// <summary>전원 완료를 처음 확인했을 때 한 번만 true — 단계 완료 보고용 래치.</summary>
    public bool TryConsumeAllComplete()
    {
        if (_completionReported || !IsAllComplete)
            return false;

        _completionReported = true;
        return true;
    }
}

/// <summary>
/// 로비 훈련 보정 범위(명세 v0.2 §6·§7) — 어느 단계에서 무엇이 켜지는가. Unity·NGO 비의존(EditMode 테스트 대상).
/// </summary>
public static class LobbyTrainingSupportRules
{
    /// <summary>훈련 보정 최소 체력(1 미만으로 안 내려감).</summary>
    public const int TrainingHealthFloor = 1;

    /// <summary>훈련 단계(Train01~Train03D) — 체력 하한 등 훈련 보정이 켜지는 구간. 종료 집결·브리핑·출격·로비는 아니다.</summary>
    public static bool IsTrainingStage(LobbyTrainingStage stage) =>
        stage >= LobbyTrainingStage.Train01 && stage <= LobbyTrainingStage.Train03D;

    /// <summary>이 사람의 체력 하한 — 훈련 명단 안 + 훈련 단계면 1, 아니면 0(없음). 늦은 참가자는 0.</summary>
    public static int HealthFloorFor(LobbyTrainingStage stage, bool isInTrainingRoster) =>
        isInTrainingRoster && IsTrainingStage(stage) ? TrainingHealthFloor : 0;

    /// <summary>TRAIN_01 표적 3개가 있어야 하는가 — Train01 진행 중에만(시작 전·완료 뒤에는 디스폰).</summary>
    public static bool Train01TargetsActive(LobbyTrainingStage stage, LobbyTrainingPhase phase) =>
        stage == LobbyTrainingStage.Train01 && phase == LobbyTrainingPhase.InProgress;
}
