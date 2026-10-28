using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [오너] 빛 모임 실제 출력(할 일 132 단계 4) — 오너 상태 기계(<see cref="InterruptOwnerPresentation"/>)의 <see cref="IInterruptLightGatherOutput"/>.
/// 시작 = 입력한 간파 스킬의 플레이어 소켓에서 재생하고, 그 VFX 루트를 오너 암전에 남긴다(<see cref="InterruptProductionDirector.KeepInOwnerDim"/>).
/// 정지 = 그 소켓을 멈춘다(남은 입자는 자기 수명대로 사라진다).
/// </summary>
public sealed class InterruptLightGatherOwnerOutput : IInterruptLightGatherOutput
{
    private readonly Func<PlayerInterruptSkillBase, IInterruptLightGatherPlayer> _resolve;
    private readonly Action<GameObject> _keepInDim;
    private IInterruptLightGatherPlayer _playing;

    /// <param name="resolve">스킬 → 그 플레이어의 빛 모임(없으면 null).</param>
    /// <param name="keepInDim">재생한 VFX 루트를 오너 암전에 남긴다.</param>
    public InterruptLightGatherOwnerOutput(Func<PlayerInterruptSkillBase, IInterruptLightGatherPlayer> resolve,
        Action<GameObject> keepInDim)
    {
        _resolve = resolve;
        _keepInDim = keepInDim;
    }

    /// <summary>지금 재생 중인 빛 모임(없으면 null).</summary>
    public IInterruptLightGatherPlayer Playing => _playing;

    public void StartLightGather(PlayerInterruptSkillBase skill)
    {
        StopLightGather();
        _playing = _resolve?.Invoke(skill);
        if (_playing == null)
            return;

        GameObject root = _playing.Play();
        if (root != null)
            _keepInDim?.Invoke(root);
    }

    public void StopLightGather()
    {
        _playing?.Stop();
        _playing = null;
    }

    /// <summary>기본 찾기 — 스킬의 부모 쪽 <see cref="InterruptLightGatherSocket"/>(Player.prefab base 루트).</summary>
    public static IInterruptLightGatherPlayer FindSocket(PlayerInterruptSkillBase skill)
    {
        if (skill == null)
            return null;

        // 에디터의 가짜 null 을 인터페이스로 넘기지 않는다.
        InterruptLightGatherSocket socket = skill.GetComponentInParent<InterruptLightGatherSocket>();
        return socket != null ? socket : null;
    }
}

/// <summary>
/// [각 피어] 남(발동자)의 빛 모임(할 일 132 단계 4) — 서버 강 시도 슬로우 시작에 켜고, 발동자 Hit·실패 복귀·성공·강제 리셋에 끈다.
/// 발동자마다 하나 — 같은 발동자의 빛 모임이 돌고 있으면 발동 번호만 바꾸고 다시 틀지 않는다(중복 재생 없음).
/// </summary>
public sealed class InterruptLightGatherPeers
{
    private struct Entry
    {
        public ulong Caster;
        public uint TriggerId;
        public IInterruptLightGatherPlayer Player;
    }

    private readonly List<Entry> _active = new List<Entry>();

    public int Count => _active.Count;

    public bool IsPlaying(ulong caster) => IndexOf(caster) >= 0;

    /// <summary>강 시도 시작 — 새로 틀었으면 true.</summary>
    public bool Begin(ulong caster, uint triggerId, IInterruptLightGatherPlayer player)
    {
        if (player == null)
            return false;

        int index = IndexOf(caster);
        if (index >= 0)
        {
            Entry entry = _active[index];
            entry.TriggerId = triggerId;
            _active[index] = entry;
            return false;
        }

        _active.Add(new Entry { Caster = caster, TriggerId = triggerId, Player = player });
        player.Play();
        return true;
    }

    /// <summary>발동자 Hit·성공 — 그 발동자의 빛 모임을 끈다.</summary>
    public bool EndCaster(ulong caster)
    {
        int index = IndexOf(caster);
        if (index < 0)
            return false;

        StopAt(index);
        return true;
    }

    /// <summary>실패 복귀 — 그 발동 번호로 켠 빛 모임을 끈다(0 은 무시).</summary>
    public bool EndTrigger(uint triggerId)
    {
        if (triggerId == 0)
            return false;

        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].TriggerId != triggerId)
                continue;

            StopAt(i);
            return true;
        }

        return false;
    }

    /// <summary>강제 리셋·디렉터 꺼짐 — 전부 끈다.</summary>
    public void EndAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
            StopAt(i);
    }

    private void StopAt(int index)
    {
        IInterruptLightGatherPlayer player = _active[index].Player;
        _active.RemoveAt(index);
        player?.Stop();
    }

    private int IndexOf(ulong caster)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].Caster == caster)
                return i;
        }

        return -1;
    }
}
