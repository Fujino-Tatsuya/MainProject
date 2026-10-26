#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 할 일 103 3인 보스전 밸런싱(B103, MPPM 호스트 + 가상 2) — 로비에서 자리(clientId 순위)대로 팔라딘·어쌔신·거너를 고르고
/// 정식 흐름으로 맵에 들어간 뒤, 호스트는 판을 진행·판정하고(<see cref="QABoss103Match.RunHost"/>) 가상 플레이어는 자기 몫 입력만 한다
/// (<see cref="QABoss103Match.RunClient"/> — 특성 선택·연출 스킵·재선택 팝업 '아니오'). 설정·실행은 에디터 메뉴 <c>Tools/QA/103</c>.
/// 88 의 체력 채우기·어그로 강제·보스 타이머 정지·QA 슈퍼아머는 쓰지 않는다(밸런스 왜곡).
/// </summary>
public static partial class QALobbyScenarios
{
    public const string Boss103Id = "B103";

    /// <summary>로비·맵 진입·시작 경로 + 30분 전투 + 여유.</summary>
    public const float Boss103TimeLimitSeconds = 40f * 60f;

    private static IEnumerator B103(QAScenarioContext ctx)
    {
        QABoss103Config cfg = QABoss103Match.ReadConfig(out string cfgNote);
        ctx.Eye("103 설정", $"{cfgNote} · 기준 시드 {cfg.Seed} · 판 {cfg.Run} · 판 시드 {cfg.RunSeed}");

        // C6 — 심장박동(에디터 루프가 읽는다) + 런타임 데이터 출처. 연속 루프 판은 호스트가 테이블이 아니면 바로 Error.
        QABoss103Heartbeat.Begin(cfg, ctx.Role);
        string source = QABoss103Heartbeat.RuntimeDataSource();
        ctx.Eye("103 데이터 출처(런타임)", $"{source} · 연속 루프 판={(cfg.Batch != 0 ? "예" : "아니오")}");
        if (cfg.Batch != 0 && ctx.Role == "host" && !QABoss103Batch.IsTable(source))
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"{QABoss103Batch.ReasonDataSource} host={source}", QABoss103Outcome.Error);
            yield break;
        }

        var lobby = new QAFlag();
        yield return EnsureLobbyPlayer(ctx, 120f, lobby);
        if (!lobby.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, "로비 플레이어 없음");
            yield break;
        }
        yield return CloseResultPanelIfOpen(ctx, null);

        // 3인이 2초 이상 안정적으로 모인 뒤 자리를 정한다(늦은 접속이 자리를 바꾸지 않게).
        float stable = 0f;
        yield return ctx.Wait(() =>
        {
            bool full = LobbySession.Active != null && LobbySession.Active.EntryCount >= ctx.ExpectedPlayers;
            stable = full ? stable + Time.unscaledDeltaTime : 0f;
            return stable >= 2f;
        }, 90f);
        if (ctx.TimedOut)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"로비 {ctx.ExpectedPlayers}인 집결 실패 — {(LobbySession.Active != null ? LobbySession.Active.EntryCount : 0)}명");
            yield break;
        }

        var ids = new List<ulong>();
        for (int i = 0; i < LobbySession.Active.EntryCount; i++)
            ids.Add(LobbySession.Active.GetEntry(i).ClientId);
        int slot = QABoss103Plan.SlotOf(LocalId, ids);
        QABoss103Character character = QABoss103Plan.CharacterForSlot(Mathf.Max(0, slot));

        var picked = new QAFlag();
        yield return PickCharacter103(ctx, character, picked);
        ctx.Check(picked.Value, $"103 캐릭터 고정(자리 {slot})", $"{character} → 로비 캐릭터 id {LocalCharacterId()} ({PrefabName(Roster(), LocalCharacterId())})");
        if (!picked.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, $"캐릭터 {character} 고정 실패");
            yield break;
        }

        var inMap = new QAFlag();
        yield return GoToMap(ctx, inMap);
        if (!inMap.Value)
        {
            QABoss103Match.AbortBeforeMap(ctx, cfg, "맵 진입 실패");
            yield break;
        }
        QABoss103Heartbeat.SetPhase("map");

        // C6 — 클론도 테이블 값으로 도는지(클론 심장박동의 출처). 모르는 클론(na)은 막지 않는다.
        if (cfg.Batch != 0 && QALobbyFlow.IsServer)
        {
            string combined = QABoss103Heartbeat.CombinedDataSource(cfg);
            if (!QABoss103Batch.IsTable(combined))
            {
                QABoss103Match.AbortBeforeMap(ctx, cfg, $"{QABoss103Batch.ReasonDataSource} {combined}", QABoss103Outcome.Error);
                yield break;
            }
        }

        if (QALobbyFlow.IsServer)
            yield return QABoss103Match.RunHost(ctx, cfg, character);
        else
            yield return QABoss103Match.RunClient(ctx, cfg, character);
    }

    /// <summary>로스터에서 그 캐릭터(<see cref="CharacterRoster.Entry.TutorialCharacter"/>) 칸을 찾아 정식 요청(<see cref="LobbySession.RequestCharacter"/>)으로 바꾼다.</summary>
    private static IEnumerator PickCharacter103(QAScenarioContext ctx, QABoss103Character character, QAFlag ok)
    {
        ok.Value = false;
        CharacterRoster roster = Roster();
        TutorialCharacter want = ToTutorialCharacter(character);
        int target = -1;
        if (roster != null)
            for (int i = 0; i < roster.Count; i++)
                if (roster.TryGetAvailableCharacter(i, out CharacterRoster.Entry e) && e.TutorialCharacter == want)
                {
                    target = i;
                    break;
                }
        if (target < 0)
        {
            ctx.Fail("103 캐릭터 칸 찾기", $"로스터={(roster != null ? roster.name : "없음")} 에 {want} 칸 없음");
            yield break;
        }

        // 교체 쿨다운(0.5초)·교체 중 거부가 있으니 바뀔 때까지 2초마다 다시 요청한다.
        float until = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < until)
        {
            if (LocalCharacterId() == target && Player.LocalPlayer != null)
            {
                ok.Value = true;
                yield break;
            }
            LobbySession.Active?.RequestCharacter(target);
            yield return ctx.Wait(() => LocalCharacterId() == target && Player.LocalPlayer != null, 2f);
        }
        ok.Value = LocalCharacterId() == target && Player.LocalPlayer != null;
    }

    private static TutorialCharacter ToTutorialCharacter(QABoss103Character c)
    {
        switch (c)
        {
            case QABoss103Character.Paladin: return TutorialCharacter.Paladin;
            case QABoss103Character.Assassin: return TutorialCharacter.Assassin;
            default: return TutorialCharacter.Gunner;
        }
    }
}
#endif
