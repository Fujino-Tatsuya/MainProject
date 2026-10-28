using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [서버] 제압 가속 버프(할 일 91 T6, PLAN-91 §3.4) — 보스가 제압에 들어가면 살아있는 플레이어 전원에게
/// 이동속도·공격속도 최종 배율 + 쿨 속도 배율을 이번 제압 길이만큼 건다. 진입 순간 Q/E/우클릭 남은 쿨도 유효 전체 쿨의 50% 만큼 뺀다(특성 명세 §6).
/// 규칙은 <see cref="SuppressHasteRules"/>.
///   · 23호: <see cref="TwentyThreeBoss.SuppressStarted"/> 를 받아 <see cref="GrantServer"/>.
///   · 다른 출처(로비 훈련 상대 등)도 보스 종류와 무관하게 <see cref="GrantServer"/> 를 부르면 된다.
///   · 버프 중 Alive 를 벗어나면(<see cref="PlayerLifeCycleController.ServerLifeStateChanged"/>) 그 출처 3개를 뺀다.
/// 만료는 상태이상 스윕이 처리한다 — 제압 종료 이벤트는 쓰지 않는다. 프리팹·씬 배선 없음(정적).
/// </summary>
public static class SuppressHaste
{
    // 버프를 건 적 있는 출처 — 쓰러짐 시 이 출처들만 뺀다(같은 타입을 다른 출처가 걸었으면 남긴다).
    private static readonly HashSet<ulong> GrantedSources = new HashSet<ulong>();
    private static SuppressHasteData data;
    private static bool dataMissingLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        GrantedSources.Clear();
        data = null;
        dataMissingLogged = false;

        TwentyThreeBoss.SuppressStarted -= HandleBossSuppressStarted;
        TwentyThreeBoss.SuppressStarted += HandleBossSuppressStarted;
        PlayerLifeCycleController.ServerLifeStateChanged -= HandleServerLifeStateChanged;
        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;
    }

    /// <summary>
    /// [서버] 그 순간 살아있는 플레이어 전원에게 버프를 건다. 같은 출처 재호출 = 지속 갱신(스택 1).
    /// </summary>
    /// <param name="sourceId">출처 — 보스 NetworkObjectId.</param>
    /// <param name="suppressDuration">이번 제압 길이(초). 시트 durationOverride &gt; 0 이면 그 값이 이긴다.</param>
    /// <returns>버프를 받은 플레이어 수.</returns>
    public static int GrantServer(ulong sourceId, float suppressDuration)
    {
        List<ISuppressHasteTarget> targets = CollectServerTargets();
        int granted = SuppressHasteRules.Grant(targets, LoadValues(), sourceId, suppressDuration);
        if (granted > 0)
            GrantedSources.Add(sourceId);

        Debug.Log($"[SuppressHaste] 제압 가속 — 출처 {sourceId} · 제압 {suppressDuration:0.##}초 · {granted}/{targets.Count}명");
        return granted;
    }

    private static void HandleBossSuppressStarted(TwentyThreeBoss boss, float duration)
    {
        if (boss == null || !boss.IsServer)
            return;

        GrantServer(boss.NetworkObjectId, duration);
    }

    private static void HandleServerLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState next)
    {
        if (GrantedSources.Count == 0 || !SuppressHasteRules.ShouldRevoke(next))
            return;

        ServerTarget target = FindServerTarget(clientId);
        if (target == null)
            return;

        foreach (ulong sourceId in GrantedSources)
        {
            if (SuppressHasteRules.Revoke(target, sourceId))
                Debug.Log($"[SuppressHaste] 쓰러짐({next}) — client {clientId} 의 출처 {sourceId} 버프 해제");
        }
    }

    private static List<ISuppressHasteTarget> CollectServerTargets()
    {
        var targets = new List<ISuppressHasteTarget>();
        IReadOnlyList<PlayerLifeCycleController> lives = PlayerLifeCycleController.Active;
        for (int i = 0; i < lives.Count; i++)
        {
            ServerTarget target = ServerTarget.From(lives[i]);
            if (target != null)
                targets.Add(target);
        }

        return targets;
    }

    private static ServerTarget FindServerTarget(ulong clientId)
    {
        IReadOnlyList<PlayerLifeCycleController> lives = PlayerLifeCycleController.Active;
        for (int i = 0; i < lives.Count; i++)
        {
            if (lives[i] != null && lives[i].OwnerClientId == clientId)
            {
                ServerTarget target = ServerTarget.From(lives[i]);
                if (target != null)
                    return target;
            }
        }

        return null;
    }

    private static SuppressHasteValues LoadValues()
    {
        if (data == null)
            data = Resources.Load<SuppressHasteData>(SuppressHasteData.ResourcesPath);

        if (data != null)
            return data.Values;

        if (!dataMissingLogged)
        {
            dataMissingLogged = true;
            Debug.LogWarning($"[SuppressHaste] Resources/{SuppressHasteData.ResourcesPath} 가 없어 기본값(×2·×2·×2·제압 길이)을 쓴다.");
        }

        return SuppressHasteValues.Default;
    }

    // 생명주기 컨트롤러 + 같은 오브젝트의 상태이상 컨트롤러. 서버·스폰된 것만.
    private sealed class ServerTarget : ISuppressHasteTarget
    {
        private readonly PlayerLifeCycleController life;
        private readonly StatusEffectController effects;
        private readonly PlayerEncounterLock encounterLock;
        private readonly PlayerSkillController skills;

        private ServerTarget(PlayerLifeCycleController life, StatusEffectController effects, PlayerEncounterLock encounterLock,
            PlayerSkillController skills)
        {
            this.life = life;
            this.effects = effects;
            this.encounterLock = encounterLock;
            this.skills = skills;
        }

        public static ServerTarget From(PlayerLifeCycleController life)
        {
            if (life == null || !life.IsSpawned || !life.IsServer)
                return null;

            StatusEffectController effects = life.GetComponent<StatusEffectController>();
            return effects != null
                ? new ServerTarget(life, effects, life.GetComponent<PlayerEncounterLock>(), life.GetComponent<PlayerSkillController>())
                : null;
        }

        public float GetEffectiveCooldownTime(PlayerSkillSlot slot) => skills != null ? skills.GetEffectiveCooldownTime(slot) : 0f;

        public void ReduceCooldown(PlayerSkillSlot slot, float seconds)
        {
            if (skills != null)
                skills.ReduceCooldownServer(slot, seconds);
        }

        public PlayerLifeState LifeState => life.State;

        public void Apply(StatusEffectType type, float magnitude, float duration, ulong sourceId, int maxStacks)
        {
            // 연출 잠금(보스 등장 컷신) 중엔 StatusEffectController.Apply 가 조용히 거부한다 — 제압 중 컷신 경로는 없다고 보고 로그만(PLAN-91 §3.4).
            if (encounterLock != null && encounterLock.IsCinematicLocked)
                Debug.LogWarning($"[SuppressHaste] client {life.OwnerClientId} 연출 잠금 중 — {type} 적용이 거부된다.", life);

            effects.Apply(type, magnitude, duration, sourceId, maxStacks);
        }

        public bool Remove(StatusEffectType type, ulong sourceId) => effects.Remove(type, sourceId);
    }
}
