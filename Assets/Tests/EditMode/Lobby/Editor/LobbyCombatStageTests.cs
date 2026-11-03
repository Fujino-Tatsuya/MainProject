using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 157 ⑥ — T20 전투 구역 집결 → T21 카운트다운(표시 상태) → T22 전투(명세 §5 T20–T22, §10 수용 기준 5).
/// 집결·카운트다운 = <see cref="PartyGatherRules"/>(행 holdSeconds 3), 표시 = <see cref="LobbyTutorialCountdownDisplay"/>,
/// 상대 6마리 = <see cref="LobbyRules.CombatSpawnSet"/>, 완료 = <see cref="LobbyCombatLedger"/>.
/// </summary>
public sealed class LobbyCombatStageTests
{
    const string StepsPath = "Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset";
    const string RulesPath = "Assets/9.ScriptableObject/Lobby/LobbyRules.asset";

    static readonly PartyGatherMemberState In = new PartyGatherMemberState(true, false, false);
    static readonly PartyGatherMemberState Out = new PartyGatherMemberState(false, false, false);

    static LobbyTutorialStepTable Steps()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>(StepsPath);
        Assert.That(table, Is.Not.Null, StepsPath);
        return table;
    }

    static LobbyTutorialStepView Row(LobbyTutorialStepTable table, string stateId)
    {
        var index = table.IndexOf(stateId);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), stateId);
        return new LobbyTutorialStepView(index, table[index]);
    }

    // T20 행 유지 시간 그대로 집결 규칙을 만든다(진행기가 구역에 행 holdSeconds 를 넣는 것과 같다).
    static PartyGatherRules T20Gather(params ulong[] clientIds)
    {
        var t20 = Steps()[Steps().IndexOf("T20_CombatGather")];
        var rules = new PartyGatherRules(t20.holdSeconds);
        rules.SetParticipants(clientIds);
        rules.SetActive(true);
        return rules;
    }

    // ─── T20 집결 · T21 카운트다운 ────────────────────────────────────────

    [Test]
    public void T20_전원_집결하면_3초_카운트다운_뒤_확정_1회()
    {
        var rules = T20Gather(0, 1);
        rules.SetMemberState(0, In);
        rules.SetMemberState(1, In);

        Assert.That(rules.Tick(10d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.True);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(13d).Within(1e-4));
        Assert.That(rules.Tick(12.99d), Is.False);
        Assert.That(rules.Tick(13d), Is.True);
        Assert.That(rules.Tick(14d), Is.False, "확정은 한 번");
    }

    [Test]
    public void T21_카운트다운_중_한_명이라도_나가면_취소되고_T20_표시로_돌아간다()
    {
        var t20 = Row(Steps(), "T20_CombatGather");
        var rules = T20Gather(0, 1);
        rules.SetMemberState(0, In);
        rules.SetMemberState(1, In);
        rules.Tick(0d);
        Assert.That(LobbyTutorialCountdownDisplay.DisplayStateId(t20, rules.IsCountdownActive), Is.EqualTo("T21_CombatCountdown"));

        rules.SetMemberState(1, Out);
        Assert.That(rules.Tick(2.5d), Is.False);
        Assert.That(rules.IsCountdownActive, Is.False);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(PartyGatherRules.NoCountdown));
        Assert.That(LobbyTutorialCountdownDisplay.DisplayStateId(t20, rules.IsCountdownActive), Is.EqualTo("T20_CombatGather"));

        // 원래 끝 시각(3초)이 지나도 확정되지 않는다 — 다시 모이면 처음부터 3초.
        Assert.That(rules.Tick(3d), Is.False);
        rules.SetMemberState(1, In);
        Assert.That(rules.Tick(4d), Is.False);
        Assert.That(rules.CountdownEndTime, Is.EqualTo(7d).Within(1e-4));
        Assert.That(rules.Tick(6.99d), Is.False);
        Assert.That(rules.Tick(7d), Is.True);
    }

    [Test]
    public void T21_표시_상태는_T20_행의_카운트다운_중에만()
    {
        var table = Steps();
        var t20 = Row(table, "T20_CombatGather");
        var t22 = Row(table, "T22_Combat");
        var t30 = Row(table, "T30_CounterGather");

        Assert.That(LobbyTutorialCountdownDisplay.IsActive(t20, true), Is.True);
        Assert.That(LobbyTutorialCountdownDisplay.DisplayStateId(t20, true), Is.EqualTo("T21_CombatCountdown"));
        Assert.That(LobbyTutorialCountdownDisplay.DisplayStateId(t20, false), Is.EqualTo("T20_CombatGather"));
        // 유지 0.3초 집결 행(T30)은 구역이 잠깐 카운트다운을 돌려도 표시 상태가 바뀌지 않는다.
        Assert.That(LobbyTutorialCountdownDisplay.DisplayStateId(t30, true), Is.EqualTo("T30_CounterGather"));
        Assert.That(LobbyTutorialCountdownDisplay.IsActive(t22, true), Is.False);
        Assert.That(LobbyTutorialCountdownDisplay.IsActive(default, true), Is.False);
    }

    [Test]
    public void T21_계약_키는_전투_지시문과_시작까지_진행_줄()
    {
        var t20 = Row(Steps(), "T20_CombatGather");
        Assert.That(t20.CountdownObjectiveKey, Is.EqualTo("LOBBY_OBJ_COMBAT"));
        Assert.That(t20.CountdownProgressKey, Is.EqualTo("LOBBY_PROGRESS_COUNTDOWN"));
        Assert.That(t20.ProgressMode, Is.EqualTo(LobbyTutorialProgressMode.Gather), "카운트다운 밖은 집결 n/N");
    }

    [Test]
    public void T21_남은_정수_초는_3_2_1_로_올림()
    {
        var rules = T20Gather(0);
        rules.SetMemberState(0, In);
        rules.Tick(0d);
        var end = rules.CountdownEndTime;

        int At(double now) => LobbyTutorialCountdownDisplay.WholeSecondsLeft(PartyGatherRules.RemainingSeconds(end, now));

        Assert.That(At(0d), Is.EqualTo(3));
        Assert.That(At(0.5d), Is.EqualTo(3));
        Assert.That(At(1d), Is.EqualTo(2));
        Assert.That(At(1.01d), Is.EqualTo(2));
        Assert.That(At(2.4d), Is.EqualTo(1));
        Assert.That(At(3d), Is.EqualTo(0));
        Assert.That(At(5d), Is.EqualTo(0), "끝 지난 뒤 음수 없음");
        Assert.That(LobbyTutorialCountdownDisplay.WholeSecondsLeft(-1d), Is.EqualTo(0));
    }

    [Test]
    public void 카운트다운_중엔_상대가_없고_T22_에서만_스폰()
    {
        var table = Steps();
        var t20 = Row(table, "T20_CombatGather");
        var t22 = Row(table, "T22_Combat");

        Assert.That(t20.Kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(LobbyCombatRules.MonstersActive(t20.Activity), Is.False, "T20/T21 집결·카운트다운 = 미스폰");
        Assert.That(LobbyCombatRules.MonstersActive(t22.Activity), Is.True);
        Assert.That(table.IndexOf("T22_Combat"), Is.EqualTo(table.IndexOf("T20_CombatGather") + 1), "카운트다운 확정 → 바로 T22");
    }

    // ─── T22 상대 6마리 ─────────────────────────────────────────────────

    [Test]
    public void 로비_전투_구성은_6마리이고_프리팹이_스폰_가능하다()
    {
        var rules = AssetDatabase.LoadAssetAtPath<LobbyRules>(RulesPath);
        Assert.That(rules, Is.Not.Null, RulesPath);
        var set = rules.CombatSpawnSet;
        Assert.That(set, Is.Not.Null, "LobbyRules.combatSpawnSet 비었음 — 옛 존2 9마리로 돌아간다");
        Assert.That(set.Count, Is.EqualTo(6), "화면 12 기본안(Q9)");

        var names = new HashSet<string>();
        foreach (var entry in set.Entries)
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.prefab, Is.Not.Null, entry.pointName);
            Assert.That(entry.prefab.GetComponent<NetworkObject>(), Is.Not.Null, entry.pointName);
            Assert.That(entry.prefab.GetComponent<MonsterBase>(), Is.Not.Null, entry.pointName);
            Assert.That(names.Add(entry.pointName), Is.True, $"지점 이름 중복 {entry.pointName}");
        }
    }

    [Test]
    public void 로비_전투_구성이_있으면_그것만_기준점_오프셋으로_쓴다()
    {
        var go = new GameObject("Train02Spawns");
        var set = ScriptableObject.CreateInstance<LobbyCombatSpawnSet>();
        try
        {
            go.transform.position = new Vector3(-16f, 0f, 0f);
            var spawns = go.AddComponent<LobbyTrainingCombatSpawns>();   // 존2 source 비어 있음
            var prefab = new GameObject("Dummy");
            set.EditorSetEntries(new[]
            {
                new LobbyCombatSpawnEntry { pointName = "A", prefab = prefab, offset = new Vector3(1.4f, 0f, 7.2f), yawDegrees = 180f },
                new LobbyCombatSpawnEntry { pointName = "B", prefab = prefab, offset = Vector3.zero },
            });

            var result = new List<LobbyCombatSpawn>();
            Assert.That(spawns.Resolve(result), Is.False, "구성도 존2 도 없으면 실패");
            Assert.That(spawns.Resolve(result, set), Is.True);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].PointName, Is.EqualTo("A"));
            Assert.That(Vector3.Distance(result[0].Candidate, new Vector3(-14.6f, 0f, 7.2f)), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(result[0].Rotation, Quaternion.Euler(0f, 180f, 0f)), Is.LessThan(0.01f));
            Object.DestroyImmediate(prefab);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(set);
        }
    }

    // ─── T22 완료 ──────────────────────────────────────────────────────

    [Test]
    public void 마지막_사망을_중복_수신해도_완료는_1회()
    {
        var ledger = new LobbyCombatLedger();
        ledger.Begin(new ulong[] { 1, 2, 3, 4, 5, 6 });
        var completions = 0;

        // LobbyTrainingCombat.HandleMonsterDiedServer 와 같은 순서 — MarkDead 가 처음일 때만 완료를 묻는다.
        void Died(ulong id)
        {
            if (ledger.MarkDead(id) && ledger.TryConsumeAllDead())
                completions++;
        }

        for (ulong id = 1; id <= 5; id++)
            Died(id);
        Assert.That(completions, Is.EqualTo(0));

        Died(6);
        Died(6);
        Died(6);
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(ledger.TryConsumeAllDead(), Is.False, "MarkDead 를 거치지 않은 재질의도 1회 뒤엔 false");
    }
}
