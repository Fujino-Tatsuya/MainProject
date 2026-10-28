#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 103 C1 — 3인 보스전 1판(맵 진입 뒤). 시작 경로는 실제 게임 흐름을 탄다(<see cref="BossTeleportManager.ForceStartEncounter"/> 직접 호출 없음):
/// <list type="number">
/// <item>[서버] 특성 4라운드(<see cref="QABoss103Plan.TraitRounds"/> = 복도 1~4) — 정식 라운드(<see cref="TraitDirector.StartOfferRoundServer(IEnumerable{ulong}, TraitRoundDisplay)"/>,
///   복도와 같은 표시 옵션)를 연다. 후보 = 캐릭터·차수별 고정 3개(할 일 125)라 시드 주입이 필요 없다.
///   각 인스턴스의 <see cref="QABoss103LocalBot"/> 가 자기 제시에서 판 시드로 골라 정식 선택 RPC 로 보낸다. 같은 시드 = 같은 선택.
///   호스트는 획득마다 그 ID 가 그 캐릭터·차수의 고정 후보인지 확인한다(<see cref="QABoss103Plan.IsFixedPick"/>).</item>
/// <item>[서버] G10 까지 개방(<see cref="MapSceneDebugActions.OpenGatesThrough"/> — 88·P572 와 같은 진입) → 파티 3명을 복도4 보스 진입 발판(<see cref="BossEnterTrigger"/>) 안에
///   일렬로 옮긴다(<see cref="PlayerFallRecovery.RelocateServer"/>). 봇은 움직이지 않는다 → 게임의 집결 판정(전원 발판 5초)이 엘리베이터를 연다.</item>
/// <item>엘리베이터 → 23호 등장 연출(연출 잠금) → 각 인스턴스 봇이 Space 홀드 스킵 → <see cref="BossEncounterPhase.Combat"/> 진입 시각 기록.</item>
/// <item>[서버] 판 종료 판정(<see cref="QABoss103EndSignals"/> → <see cref="QABoss103Plan.Judge"/>): 보스 처치 = 클리어(할 일 119 — 보스 <see cref="Unit.Died"/> 이벤트 또는
///   <see cref="BossEncounterDirector.IsBossDefeatedServer"/>, 한 번 켜지면 유지) / 공용 목숨 0 전멸(<see cref="PartyWipeRespawner.PartyWipeRespawned"/>,
///   리스폰 대상이 아니라 결과 화면으로 가면 씬 이탈) = 실패 / Combat 부터 30분 = 시간초과(C7g — 목숨 0·보스 HP 5분 불변이면 조기 시간초과,
///   <see cref="QABoss103EarlyTimeout"/>). 판 기록(판정 출처 <see cref="QABoss103RunRecord.OutcomeSource"/>) → <see cref="LastRecord"/> + 로그 1줄.
///   부활(할 일 119) = 각 인스턴스 봇이 Soul 3초 뒤 F10 디버그 부활과 같은 RPC(<c>QABoss103CombatBot.Recovery.cs</c>) — 게임이 목숨 1 차감·0 이면 거부 → 전멸.</item>
/// </list>
/// 88 의 체력 채우기·어그로 강제·보스 타이머 정지·QA 슈퍼아머는 켜지 않는다(혹시 남아 있으면 시작 때 해제).
/// </summary>
public static class QABoss103Match
{
    private const string Tag = QABoss103Plan.Tag;
    private const float LoadingWaitSeconds = 60f;
    private const float PartyWaitSeconds = 60f;
    private const float TraitRoundWaitSeconds = 30f;
    private const float PadWaitSeconds = 15f;
    private const float GatherWaitSeconds = 30f;
    private const float IntroWaitSeconds = 120f;
    private const float CombatWaitSeconds = 120f;
    private const float ResetPromptWaitSeconds = 5f;

    private static readonly int FloorMask = LayerMask.GetMask("Default", "Ground", "Env");

    /// <summary>마지막으로 끝난 판 기록(호스트). C4 계측·C6 루프가 읽는다.</summary>
    public static QABoss103RunRecord LastRecord { get; private set; }

    /// <summary>판 끝에 클론 봇 판 파일(<c>done</c>)을 기다리는 상한(초) — 못 받으면 그 열을 비우고 runs.csv note 에 남긴다.</summary>
    private const float PartsWaitSeconds = 10f;

    /// <summary>시나리오가 설정을 읽은 시각 = 판 시작(runs.csv started_at).</summary>
    private static DateTime _startedAt = DateTime.Now;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => LastRecord = null;

    // ── 설정 ─────────────────────────────────────────────────

    public static string ConfigPath => Path.Combine(QANetSim88.OwnRoot, QABoss103Config.FileName);

    /// <summary>자기 루트의 설정 파일. 없거나 깨졌으면 기본값(시드 103·판 1) + 사유.</summary>
    public static QABoss103Config ReadConfig(out string note)
    {
        _startedAt = DateTime.Now;
        try
        {
            if (File.Exists(ConfigPath) && QABoss103Config.TryParse(File.ReadAllText(ConfigPath), out QABoss103Config cfg))
            {
                note = $"설정 {ConfigPath}";
                return cfg;
            }
            note = $"설정 파일 없음({ConfigPath}) — 기본값";
        }
        catch (Exception e)
        {
            note = $"설정 읽기 실패({e.Message}) — 기본값";
        }
        return new QABoss103Config();
    }

    /// <summary>
    /// 판 시작 전에 끊긴 판 — 판 기록(기본 Aborted)과 로그 1줄만 남긴다(호스트만 기록).
    /// C6 데이터 출처 불일치는 <paramref name="outcome"/> = Error(<see cref="QABoss103Batch.ReasonDataSource"/>).
    /// </summary>
    public static void AbortBeforeMap(QAScenarioContext ctx, QABoss103Config cfg, string why, QABoss103Outcome outcome = QABoss103Outcome.Aborted)
    {
        if (ctx.Role != "host")
            return;
        var rec = NewRecord(cfg);
        rec.Outcome = outcome;
        rec.Detail = why;
        Finish(ctx, cfg, rec, null, double.NaN);
    }

    private static QABoss103RunRecord NewRecord(QABoss103Config cfg) =>
        new QABoss103RunRecord { Run = cfg.Run, BaseSeed = cfg.Seed, RunSeed = cfg.RunSeed, BotTier = cfg.BotTier };

    private static void Finish(QAScenarioContext ctx, QABoss103Config cfg, QABoss103RunRecord rec, QABoss103Recorder recorder, double bossHpPct)
    {
        LastRecord = rec;
        QABoss103Heartbeat.SetPhase("end");
        Debug.Log(rec.ToLogLine());
        QABoss103RunCsv.Write(cfg, rec, recorder, bossHpPct, _startedAt);
        string evidence = $"결과 {rec.Outcome} · 전투 {(rec.CombatSeconds < 0 ? "na" : rec.CombatSeconds.ToString("F1") + "s")} · 시드 {rec.BaseSeed}/판 {rec.Run}/판 시드 {rec.RunSeed} · 특성 {rec.TraitsToken()} · {rec.Detail}";
        if (rec.Outcome == QABoss103Outcome.Aborted || rec.Outcome == QABoss103Outcome.Error)
            ctx.Fail("103 판 기록", evidence, ctx.Shot("b103_abort"));
        else
            ctx.Pass("103 판 기록", evidence, ctx.Shot("b103_end"));
    }

    // ── 호스트 ───────────────────────────────────────────────

    public static IEnumerator RunHost(QAScenarioContext ctx, QABoss103Config cfg, QABoss103Character localCharacter)
    {
        QABoss103RunRecord rec = NewRecord(cfg);
        QA88TwentyThreeAssist.ReleaseActive("b103_start");
        QABossTimerHold88.ReleaseActive("b103_start");

        QABoss103LocalBot bot = QABoss103LocalBot.Create(cfg.TraitRunSeed, localCharacter);
        QABoss103CombatBot fighter = CreateFighter(ctx, cfg, localCharacter);
        var characters = new Dictionary<ulong, QABoss103Character>();
        var offPicks = new List<string>();
        Action<TraitChosen> onChosen = c =>
        {
            if (!characters.TryGetValue(c.ClientId, out QABoss103Character ch))
                return;
            string id = c.Trait != null ? c.Trait.Id : $"#{c.TraitHash}";
            rec.AddTrait(ch, id);
            int tier = rec.TraitCount(ch);
            if (!QABoss103Plan.IsFixedPick(ch, tier, id))
            {
                offPicks.Add($"{ch}:{tier}:{id}");
                Debug.LogWarning($"{Tag} event=trait_fixed ok=false character={ch} tier={tier} id={QABoss103Plan.Token(id)} auto={B(c.Auto)}");
            }
        };
        var signals = new QABoss103EndSignals();
        Action<IReadOnlyList<ulong>> onWipe = _ => signals.OnWipe();
        Action onBossDied = signals.OnBossDied;
        Unit bossUnit = null;
        TraitDirector traits = null;
        QABoss103Recorder recorder = null;
        double bossHpPct = double.NaN;
        try
        {
            // 0) 맵 로딩 흐름이 끝나고 파티 전원이 맵에 설 때까지.
            yield return ctx.Wait(() => !IsLoadingFlowInProgress && QALobbyFlow.SpawnedPlayers().Count >= ctx.ExpectedPlayers, LoadingWaitSeconds + PartyWaitSeconds);
            if (ctx.TimedOut)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = $"맵 파티 {QALobbyFlow.SpawnedPlayers().Count}/{ctx.ExpectedPlayers}·로딩 흐름 {IsLoadingFlowInProgress}";
                yield break;
            }
            List<ulong> ids = SortedClientIds();
            var party = new List<string>();
            string mismatch = null;
            foreach (Player p in QALobbyFlow.SpawnedPlayers())
            {
                int slot = QABoss103Plan.SlotOf(p.OwnerClientId, ids);
                QABoss103Character ch = cfg.CharacterForSlot(slot);
                characters[p.OwnerClientId] = ch;
                party.Add($"{p.OwnerClientId}:{ch}:{QALobbyFlow.CleanName(p)}");
                mismatch ??= QABoss103Plan.CharacterMismatch(slot, ch, QALobbyFlow.CleanName(p));
            }
            Debug.Log($"{Tag} event=run_start run={rec.Run} seed={rec.BaseSeed} runSeed={rec.RunSeed} botTier={rec.BotTier} party={QABoss103Plan.Token(string.Join(",", party))}");
            ctx.Eye("103 파티", string.Join(", ", party));
            // 할 일 129 — 클라 캐릭터 고정이 안 돌아 기본 캐릭터로 남으면 봇이 다른 캐릭터로 취급한다(특성·통계 오염).
            if (mismatch != null)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = mismatch;
                yield break;
            }

            // 1) 특성 4라운드.
            QABoss103Heartbeat.SetPhase("traits");
            traits = TraitDirector.Instance;
            if (traits == null || !traits.IsSpawned)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = "특성 디렉터 없음";
                yield break;
            }
            traits.ServerTraitChosen += onChosen;
            for (int round = 1; round <= QABoss103Plan.TraitRounds; round++)
            {
                bool offered = traits.StartOfferRoundServer(ids, MapSceneTraitCorridorRules.DisplayOf(round));
                if (!offered)
                {
                    Debug.LogWarning($"{Tag} event=trait_round round={round} offered=false — 풀 소진·미스폰");
                    continue;
                }
                TraitDirector t = traits;
                yield return ctx.Wait(() => t == null || t.IsRoundCompleteServer, TraitRoundWaitSeconds);
                int auto = 0;
                if (ctx.TimedOut && traits != null)
                    auto = traits.AutoChooseAllPendingServer();
                Debug.Log($"{Tag} event=trait_round round={round} directorRound={(traits != null ? traits.Round : 0)} complete={(!ctx.TimedOut ? "true" : "false")} autoChosen={auto}");
            }
            var shortCounts = new List<string>();
            foreach (QABoss103Character ch in QABoss103Plan.PartyOrder)
                if (rec.TraitCount(ch) != QABoss103Plan.TraitRounds)
                {
                    shortCounts.Add($"{ch}:{rec.TraitCount(ch)}");
                    Debug.LogWarning($"{Tag} event=trait_count character={ch} n={rec.TraitCount(ch)} want={QABoss103Plan.TraitRounds}");
                }
            bool fixedOk = offPicks.Count == 0 && shortCounts.Count == 0;
            ctx.Check(fixedOk, "103 특성 고정 후보",
                $"캐릭터·차수 고정 후보 밖 획득 {(offPicks.Count == 0 ? "없음" : string.Join(", ", offPicks))} · " +
                $"{QABoss103Plan.TraitRounds}개 미달 {(shortCounts.Count == 0 ? "없음" : string.Join(", ", shortCounts))} · " +
                $"호스트 제시 불일치 {bot.OfferMismatches}회 · 특성 {rec.TraitsToken()}");

            // 2) G10 개방 → 발판 배치.
            MapSceneDirector tutorial = MapSceneDirector.Instance;
            if (tutorial != null && tutorial.IsTutorialActive && !tutorial.IsGateOpen(MapSceneGate.G10_BossEnterAndPad))
                MapSceneDebugActions.OpenGatesThrough(tutorial, MapSceneGate.G10_BossEnterAndPad);
            BossEnterTrigger pad = null;
            yield return ctx.Wait(() => (pad = FindPad()) != null && MapSceneBossPath.IsBossPadEnabled, PadWaitSeconds);
            if (pad == null || !MapSceneBossPath.IsBossPadEnabled)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = $"보스 진입 발판 없음·잠김(pad={(pad != null)} enabled={MapSceneBossPath.IsBossPadEnabled})";
                yield break;
            }
            int moved = PlacePartyOnPad(pad, ids, out string placeDetail);
            Debug.Log($"{Tag} event=pad_place moved={moved}/{ids.Count} {placeDetail}");

            // 3) 집결(전원 발판 유지) → 엘리베이터 → 등장 연출 → Combat.
            QABoss103Heartbeat.SetPhase("gather");
            BossTeleportManager tp = BossTeleportManager.Instance;
            float gatherAt = Time.realtimeSinceStartup;
            yield return ctx.Wait(() => tp != null && (tp.IsCountdownActive || tp.IsElevatorRunning || Phase != BossEncounterPhase.Idle), GatherWaitSeconds);
            if (ctx.TimedOut)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = $"집결 판정 안 됨 {GatherWaitSeconds:0}초(집결 {(tp != null ? tp.GatheredCount : 0)}/{(tp != null ? tp.GatherTotal : 0)}, {placeDetail})";
                yield break;
            }
            Debug.Log($"{Tag} event=gather ok=true waitS={Time.realtimeSinceStartup - gatherAt:F1} gathered={tp.GatheredCount}/{tp.GatherTotal} countdown={tp.IsCountdownActive} elevator={tp.IsElevatorRunning}");

            yield return ctx.Wait(() => IsIntroOrLater(Phase), IntroWaitSeconds);
            if (ctx.TimedOut || Phase == BossEncounterPhase.FailedSafe)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = $"엘리베이터 → 연출 안 됨(단계={Phase}, 엘리베이터={tp.IsElevatorRunning})";
                yield break;
            }
            QABoss103Heartbeat.SetPhase("intro");
            float introAt = Time.realtimeSinceStartup;
            yield return ctx.Wait(() => Phase == BossEncounterPhase.Combat || Phase == BossEncounterPhase.FailedSafe, CombatWaitSeconds);
            if (Phase != BossEncounterPhase.Combat)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = $"연출 → Combat 안 됨(단계={Phase}, 스킵 {SkipVotes()})";
                yield break;
            }
            double combatStart = Time.realtimeSinceStartupAsDouble;
            QABoss103Heartbeat.MarkCombat();
            recorder = QABoss103Recorder.Begin(characters);   // C4 계측 — Combat 진입부터(F10 부활 집계 포함, 할 일 119)
            QABoss103Relocator.Begin(characters, recorder.Stats, cfg);   // C7d 20초 끼임 → 가장 가까운 링 지점 · 119 벽 끼임 → 무작위 링 지점 · 138-2 직전 스크린샷
            string combatWall = DateTime.Now.ToString("HH:mm:ss.fff");
            Debug.Log($"{Tag} event=combat_start at={combatWall} introS={Time.realtimeSinceStartup - introAt:F1} skip={SkipVotes()} hostSkipPath={bot.SkipPath} hostSawLock={bot.SawCinematicLock}");
            ctx.Check(bot.SawCinematicLock, "103 연출 중 연출 잠금", $"호스트 플레이어 PlayerEncounterLock.IsCinematicLocked 관측={bot.SawCinematicLock}");
            ctx.Pass("103 Combat 진입", $"{combatWall} · 연출 {Time.realtimeSinceStartup - introAt:F1}s · 스킵 {SkipVotes()} · 호스트 스킵 경로 {bot.SkipPath}", ctx.Shot("b103_combat"));

            // 4) 판 종료 판정.
            PartyWipeRespawner.PartyWipeRespawned += onWipe;
            // 할 일 119 — 클리어 = 게임의 실제 사망 신호(보스 Unit.Died, 서버). 디렉터 클리어 확정과 함께 래치에 모은다.
            bossUnit = UnityEngine.Object.FindAnyObjectByType<TwentyThreeBoss>();
            if (bossUnit != null)
                bossUnit.Died += onBossDied;
            Debug.Log($"{Tag} event=clear_watch bossDied={(bossUnit != null ? "subscribed" : "no_boss")} director={(BossEncounterDirector.Instance != null ? "ok" : "none")}");
            string mapScene = SceneManager.GetActiveScene().name;
            BossEncounterDirector director = BossEncounterDirector.Instance;
            var stall = new QABoss103StallWatch();
            var early = new QABoss103EarlyTimeout();
            while (true)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - combatStart;
                signals.Poll(director != null && director.IsBossDefeatedServer, SceneManager.GetActiveScene().name != mapScene || director == null);
                QABoss103Outcome outcome = signals.Judge(elapsed);
                if (outcome == QABoss103Outcome.None && stall.Tick(elapsed, recorder.Activity.Count))
                {
                    // C7d·C7f — 피해·사망/부활·목숨·기믹 진행·보스 HP 어느 것도 없는 교착(끼임·보스 대상 없음). 통계 제외 → 끝에서 1회 재실행.
                    rec.Outcome = QABoss103Outcome.Error;
                    rec.CombatSeconds = elapsed;
                    rec.OutcomeSource = QABoss103Batch.ReasonStall;
                    rec.Detail = $"{QABoss103Batch.ReasonStall} — Combat 중 {QABoss103StallWatch.StallSeconds:0}초 동안 전투 활동 0(마지막 {recorder.Activity.LastKind})";
                    Debug.LogWarning($"{Tag} event=stall combatS={elapsed:F1} quietS={stall.QuietSeconds(elapsed):F1} lastActivity={recorder.Activity.LastKind} " +
                                     $"activity={recorder.Activity.Count} dealt={recorder.Stats.DamageDealtTotal()} " +
                                     $"taken={recorder.Stats.TakenActivity()} relocates={QABoss103Relocator.Relocates} {BossDiag()}");
                    break;
                }
                if (outcome == QABoss103Outcome.None && early.Tick(elapsed, recorder.Stats.LivesNow, recorder.BossHp))
                {
                    // C7g — 목숨 0 + 보스 HP 5분 불변(북진 교착) → 30분까지 가도 같은 Timeout. 통계는 일반 Timeout 과 같이, note 로 구분.
                    rec.Outcome = QABoss103Outcome.Timeout;
                    rec.CombatSeconds = elapsed;
                    rec.OutcomeSource = QABoss103EarlyTimeout.Note;
                    rec.Detail = QABoss103EarlyTimeout.Note;
                    Debug.LogWarning($"{Tag} event=early_timeout combatS={elapsed:F1} flatS={early.FlatSeconds(elapsed):F1} lives={recorder.Stats.LivesNow} " +
                                     $"bossHp={recorder.BossHp} lastActivity={recorder.Activity.LastKind} {BossDiag()}");
                    break;
                }
                if (outcome != QABoss103Outcome.None)
                {
                    rec.Outcome = outcome;
                    rec.CombatSeconds = elapsed;
                    rec.OutcomeSource = signals.Source(outcome);
                    rec.Detail = outcome == QABoss103Outcome.Wipe
                        ? (signals.Wiped ? "전멸 리스폰(PartyWipeRespawned)" : $"맵 이탈(결과 화면 — 리스폰 대상 아님) scene={SceneManager.GetActiveScene().name}")
                        : outcome == QABoss103Outcome.Clear ? $"보스 처치({rec.OutcomeSource})" : $"Combat 부터 {QABoss103Plan.CombatLimitSeconds / 60:0}분";
                    Debug.Log($"{Tag} event=run_judge outcome={outcome} source={rec.OutcomeSource} combatS={elapsed:F1} bossDied={B(signals.BossDied)} " +
                              $"directorDefeated={B(signals.DirectorDefeated)} wiped={B(signals.Wiped)} leftMap={B(signals.LeftMap)} {BossDiag()}");
                    break;
                }
                yield return null;
            }
            QABoss103Relocator.Stop();
            bossHpPct = recorder.Stop();
            QABoss103Heartbeat.SetPhase("end");

            // 전멸 리스폰 뒤 특성 초기화 팝업 — 호스트 봇도 '아니오' 를 보낼 시간을 준다.
            if (rec.Outcome == QABoss103Outcome.Wipe && signals.Wiped)
            {
                int before = bot.ResetAnswers;
                yield return ctx.Wait(() => bot == null || bot.ResetAnswers > before, ResetPromptWaitSeconds);
                Debug.Log($"{Tag} event=reset_prompt host_answered={(bot != null && bot.ResetAnswers > before ? "true" : "false")}");
            }

            // 5) C4 — 봇 판 파일: 호스트 봇을 닫고, 끝 신호를 쓰고, 클론 봇의 끝 표시를 상한까지 기다린다.
            if (cfg.HasOut)
            {
                if (fighter != null)
                    fighter.ClosePart("host_end");
                QABoss103RunCsv.SignalEnd(cfg);
                var others = new List<int>();
                foreach (QABoss103Character ch in characters.Values)
                    if (ch != localCharacter)
                        others.Add((int)ch);
                float partsAt = Time.realtimeSinceStartup;
                yield return ctx.Wait(() => QABoss103RunCsv.PendingParts(cfg, others) == 0, PartsWaitSeconds);
                Debug.Log($"{Tag} event=parts_wait ok={(!ctx.TimedOut ? "true" : "false")} waitS={Time.realtimeSinceStartup - partsAt:F1} pending={QABoss103RunCsv.PendingParts(cfg, others)}/{others.Count}");
            }
        }
        finally
        {
            PartyWipeRespawner.PartyWipeRespawned -= onWipe;
            if (bossUnit != null)
                bossUnit.Died -= onBossDied;
            if (traits != null)
                traits.ServerTraitChosen -= onChosen;
            if (rec.Outcome == QABoss103Outcome.None)
            {
                rec.Outcome = QABoss103Outcome.Aborted;
                rec.Detail = "중단(시나리오 상한·예외·정지)";
            }
            QABoss103Relocator.Stop();
            if (recorder != null && double.IsNaN(bossHpPct))
                bossHpPct = recorder.Stop();
            Finish(ctx, cfg, rec, recorder, bossHpPct);
            if (recorder != null)
                UnityEngine.Object.Destroy(recorder.gameObject);
            if (bot != null)
                UnityEngine.Object.Destroy(bot.gameObject);
            if (fighter != null)
                UnityEngine.Object.Destroy(fighter.gameObject);
        }
    }

    /// <summary>C2 전투 봇 — 이 판 단계(<see cref="QABoss103Config.BotTier"/>)의 프로필을 리포트에 그대로 남긴다.</summary>
    private static QABoss103CombatBot CreateFighter(QAScenarioContext ctx, QABoss103Config cfg, QABoss103Character character)
    {
        QABoss103CombatBot fighter = QABoss103CombatBot.Create(cfg.RunSeed, character, cfg.BotTier);
        string source = cfg.Tier.HasValue ? "수동 지정" : $"(판 {cfg.Run} − 1) mod 3";
        ctx.Eye($"103 봇 프로필({character})", $"{fighter.Profile.Describe()} · 단계 출처 {source}");
        fighter.AttachPart(cfg);
        return fighter;
    }

    // ── 가상 플레이어 ────────────────────────────────────────

    /// <summary>
    /// [클라] 내 몫 입력(특성·연출 스킵·초기화 팝업 '아니오')만 넣는다. 판정은 호스트. 끝 = 맵 이탈·보스 사망 관측·팝업 응답 뒤 3초·
    /// Combat 부터 30분 + 30초(호스트 판정 뒤 여유) 중 먼저.
    /// </summary>
    public static IEnumerator RunClient(QAScenarioContext ctx, QABoss103Config cfg, QABoss103Character localCharacter)
    {
        QABoss103LocalBot bot = QABoss103LocalBot.Create(cfg.TraitRunSeed, localCharacter);
        QABoss103CombatBot fighter = CreateFighter(ctx, cfg, localCharacter);
        try
        {
            string mapScene = SceneManager.GetActiveScene().name;
            DateTime startedUtc = DateTime.UtcNow;
            double combatStart = -1.0;
            float answeredAt = -1f;
            float nextEndCheck = 0f;
            string end = "";
            while (end.Length == 0)
            {
                BossEncounterDirector d = BossEncounterDirector.Instance;
                if (combatStart < 0.0 && d != null && d.Phase == BossEncounterPhase.Combat)
                {
                    combatStart = Time.realtimeSinceStartupAsDouble;
                    QABoss103Heartbeat.MarkCombat();
                    Debug.Log($"{Tag} event=combat_seen character={localCharacter} skipPath={bot.SkipPath} sawLock={bot.SawCinematicLock}");
                }
                if (answeredAt < 0f && bot.ResetAnswers > 0)
                    answeredAt = Time.realtimeSinceStartup;

                if (SceneManager.GetActiveScene().name != mapScene)
                    end = "맵 이탈";
                else if (combatStart >= 0.0 && BossDeadSeen())
                    end = "보스 사망 관측";
                else if (answeredAt >= 0f && Time.realtimeSinceStartup - answeredAt >= 3f)
                    end = "전멸 → 초기화 팝업 '아니오'";
                else if (combatStart >= 0.0 && QABoss103Plan.IsTimedOut(Time.realtimeSinceStartupAsDouble - combatStart, QABoss103Plan.CombatLimitSeconds + 30.0))
                    end = "Combat 부터 30분";
                else if (Time.realtimeSinceStartup >= nextEndCheck)
                {
                    // C4 — 호스트 판정 끝 신호(파일) → 봇을 닫아 판 파일 끝 표시(done)를 남긴다.
                    nextEndCheck = Time.realtimeSinceStartup + 0.5f;
                    if (QABoss103RunCsv.EndSignaled(cfg, startedUtc))
                        end = "호스트 판 끝 신호";
                }
                yield return null;
            }
            ctx.Eye("103 가상 플레이어 입력", $"{localCharacter} · 끝={end} · 특성 {string.Join("+", bot.Picks)} · 스킵 경로 {bot.SkipPath} · 연출 잠금 관측 {bot.SawCinematicLock} · 팝업 '아니오' {bot.ResetAnswers}회");
        }
        finally
        {
            if (bot != null)
                UnityEngine.Object.Destroy(bot.gameObject);
            if (fighter != null)
                UnityEngine.Object.Destroy(fighter.gameObject);
        }
    }

    // ── 도구 ─────────────────────────────────────────────────

    private static BossEncounterPhase Phase =>
        BossEncounterDirector.Instance != null ? BossEncounterDirector.Instance.Phase : BossEncounterPhase.Idle;

    private static bool IsIntroOrLater(BossEncounterPhase p) =>
        p == BossEncounterPhase.Preparing || p == BossEncounterPhase.Descending || p == BossEncounterPhase.Impact ||
        p == BossEncounterPhase.Dialogue || p == BossEncounterPhase.Combat || p == BossEncounterPhase.FailedSafe;

    private static string SkipVotes()
    {
        BossEncounterDirector d = BossEncounterDirector.Instance;
        return d != null ? $"{d.SkipVoteCount}/{d.EligibleCount}" : "na";
    }

    private static bool IsLoadingFlowInProgress
    {
        get
        {
            NetworkLoadingFlowController flow = NetworkManager.Singleton != null ? NetworkManager.Singleton.GetComponent<NetworkLoadingFlowController>() : null;
            return flow != null && flow.IsFlowInProgress;
        }
    }

    /// <summary>C7d 정체 로그용 — 보스 상태·어그로 대상·체력.</summary>
    private static string BossDiag()
    {
        TwentyThreeBoss boss = UnityEngine.Object.FindAnyObjectByType<TwentyThreeBoss>();
        return boss == null ? "boss=none" : $"bossState={boss.State} aggroId={boss.AggroTargetId} bossHp={boss.CurrentHealth}";
    }

    private static string B(bool v) => v ? "true" : "false";

    private static bool BossDeadSeen()
    {
        TwentyThreeBoss boss = UnityEngine.Object.FindAnyObjectByType<TwentyThreeBoss>();
        return boss != null && boss.CurrentHealth <= 0;
    }

    private static List<ulong> SortedClientIds()
    {
        var ids = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        ids.Sort();
        return ids;
    }

    private static BossEnterTrigger FindPad()
    {
        BossEnterTrigger[] pads = UnityEngine.Object.FindObjectsByType<BossEnterTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return pads.Length > 0 ? pads[0] : null;
    }

    /// <summary>
    /// [서버] 파티를 발판 박스 가운데 일렬(<see cref="QABoss103Plan.PadLineOffset"/>, 박스 긴 축)로 옮긴다. 지점마다 바닥 레이캐스트로 높이를 맞추고
    /// 발판 판정(<see cref="BossEnterTrigger.ContainsPoint"/>) 안인지 확인한다. 옮긴 수.
    /// </summary>
    private static int PlacePartyOnPad(BossEnterTrigger pad, List<ulong> ids, out string detail)
    {
        var parts = new List<string>();
        BoxCollider box = pad.GetComponent<BoxCollider>();
        Transform t = box.transform;
        Vector3 half = Vector3.Scale(box.size * 0.5f, t.lossyScale);
        bool alongX = Mathf.Abs(half.x) >= Mathf.Abs(half.z);
        float longHalf = alongX ? Mathf.Abs(half.x) : Mathf.Abs(half.z);
        int moved = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            float off = QABoss103Plan.PadLineOffset(i, ids.Count, longHalf);
            Vector3 local = box.center + new Vector3(alongX ? off / Mathf.Max(0.0001f, t.lossyScale.x) : 0f, -box.size.y * 0.5f,
                                                     alongX ? 0f : off / Mathf.Max(0.0001f, t.lossyScale.z));
            Vector3 p = t.TransformPoint(local);
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, FloorMask, QueryTriggerInteraction.Ignore))
                p = hit.point;
            bool inside = pad.ContainsPoint(p);
            NetworkClient client = NetworkManager.Singleton.ConnectedClients.TryGetValue(ids[i], out NetworkClient c) ? c : null;
            PlayerFallRecovery mover = client != null && client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerFallRecovery>() : null;
            bool ok = mover != null && mover.RelocateServer(p);
            if (ok)
                moved++;
            parts.Add($"{ids[i]}:{(ok ? "ok" : "fail")}:{(inside ? "in" : "out")}:{QABossStart88.Fmt(p)}");
        }
        detail = "points=" + string.Join(",", parts);
        return moved;
    }
}
#endif
