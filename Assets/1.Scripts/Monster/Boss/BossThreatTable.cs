using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23호 위협 점수표(PLAN-boss-aggro §3.1~3.2) — 서버 전용, 순수 C#(EditMode 로 경계를 고정).
///
/// 플레이어별로 <b>보스 HP 에 실제로 들어간 피해</b>를 쌓고, 반감기로 지수 감쇠한다.
/// 최근에 많이 때린 사람이 1등이 되고 옛날 딜은 잊힌다(팀장 10-14: 재료는 피해량만).
///
/// 🔴 표는 "누가 1등인가"만 안다. <b>언제 갈아탈지</b>는 <see cref="ShouldSwitch"/> 가 정하고,
///    공격 중 교체 금지(<see cref="BossAggroPolicy"/>)는 호출측이 먼저 거른다.
/// 키 = 플레이어 Transform 의 InstanceID(테스트에서 GameObject 없이 돌리려고 int).
/// </summary>
public sealed class BossThreatTable
{
    struct Entry
    {
        public float score;   // time 시점의 점수
        public float time;
    }

    readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
    readonly List<int> _keyScratch = new List<int>();

    /// <summary>반감기(초). 0 이하 = 감쇠 없음.</summary>
    public float HalfLife { get; set; } = 6f;

    public int Count => _entries.Count;

    public void Clear() => _entries.Clear();

    public void Remove(int id) => _entries.Remove(id);

    /// <summary>피해를 쌓는다. 0 이하는 무시.</summary>
    public void Add(int id, float amount, float now)
    {
        if (amount <= 0f) return;
        float current = Score(id, now);
        _entries[id] = new Entry { score = current + amount, time = now };
    }

    /// <summary>지금 시점 점수(감쇠 반영). 기록이 없으면 0.</summary>
    public float Score(int id, float now)
    {
        if (!_entries.TryGetValue(id, out Entry e)) return 0f;
        return Decay(e.score, now - e.time, HalfLife);
    }

    /// <summary>
    /// 유효한 후보 중 1등. <paramref name="isValid"/> 가 false 인 키(사망·유령·이탈)는 표에서 지운다.
    /// 아무도 점수가 없으면 false.
    /// </summary>
    public bool TryGetTop(Func<int, bool> isValid, float now, out int id, out float score)
    {
        id = 0;
        score = 0f;
        bool found = false;

        _keyScratch.Clear();
        _keyScratch.AddRange(_entries.Keys);
        foreach (int key in _keyScratch)
        {
            if (isValid != null && !isValid(key)) { _entries.Remove(key); continue; }
            float s = Score(key, now);
            if (s <= score) continue;
            score = s;
            id = key;
            found = true;
        }
        return found;
    }

    public static float Decay(float score, float elapsed, float halfLife)
    {
        if (halfLife <= 0f || elapsed <= 0f) return score;
        return score * Mathf.Pow(0.5f, elapsed / halfLife);
    }

    /// <summary>
    /// 1등에게 갈아탈 것인가. 셋 다 만족해야 한다.
    /// ① 1등 점수 ≥ <paramref name="minScore"/>(잽 몇 번에 돌지 않게)
    /// ② 1등 ≥ 현재 대상 × <paramref name="ratio"/>(문턱 — 비등하면 안 바꾼다)
    /// ③ 현재 대상을 문 지 <paramref name="minHold"/> 초 이상
    /// </summary>
    public static bool ShouldSwitch(float currentScore, float topScore, float heldSeconds,
                                    float ratio, float minHold, float minScore)
    {
        if (topScore < minScore) return false;
        if (heldSeconds < minHold) return false;
        return topScore >= currentScore * Mathf.Max(1f, ratio);
    }
}
