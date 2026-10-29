#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 108 야간 QA 순수 로직 — 시드 파싱·103 설정 확장·날짜 폴더·리포트 머리말·녹화 기록·OBS 요청 메시지·PC 설정.
/// 할 일 120 — 화질·보관·리플레이 설정, OBS 요청 데이터·응답·이벤트, 영상 타임코드, 이벤트 표·시각표, 클립 합치기·쿨다운·파일명, 보관 삭제 대상, 요약 절.
/// </summary>
public sealed class QANightlyTests
{
    private const string ValidSeed =
        "{\"name\":\"eunhee\",\"baseSeed\":777,\"traitSeed\":55,\"botTier\":\"skilled\",\"characterOrder\":[\"Gunner\",\"Paladin\",\"Assassin\"],\"runs\":12,\"note\":\"n\"}";

    // ── 시드 ─────────────────────────────────────────────────

    [Test]
    public void 시드_파일을_읽는다()
    {
        Assert.IsTrue(QANightlySeed.TryParse(ValidSeed, out QANightlySeed s, out List<string> errors), string.Join("/", errors));
        Assert.AreEqual("eunhee", s.Name);
        Assert.AreEqual(777, s.BaseSeed);
        Assert.AreEqual(55, s.TraitSeed);
        Assert.AreEqual(QABoss103BotTier.Skilled, s.Tier);
        CollectionAssert.AreEqual(new[] { QABoss103Character.Gunner, QABoss103Character.Paladin, QABoss103Character.Assassin }, s.Order);
        Assert.AreEqual(12, s.Runs);
    }

    [Test]
    public void 빠진_칸은_기본값()
    {
        Assert.IsTrue(QANightlySeed.TryParse("{\"name\":\"base\"}", out QANightlySeed s, out _));
        Assert.AreEqual(108, s.BaseSeed);
        Assert.AreEqual(0, s.TraitSeed);
        Assert.IsNull(s.Tier);
        CollectionAssert.AreEqual(QABoss103Plan.PartyOrder, s.Order);
        Assert.AreEqual(10, s.Runs);
    }

    [Test]
    public void 레포의_기본_시드_파일이_유효하다()
    {
        string path = Path.Combine(QANetSim88.OwnRoot, QANightlySeed.RelativePath);
        Assert.IsTrue(File.Exists(path), path);
        Assert.IsTrue(QANightlySeed.TryParse(File.ReadAllText(path), out QANightlySeed s, out List<string> errors), string.Join("/", errors));
        Assert.AreEqual("base", s.Name);
    }

    [TestCase("{\"name\":\"a b\"}", "name")]
    [TestCase("{\"name\":\"\"}", "name")]
    [TestCase("{\"name\":\"x\",\"baseSeed\":0}", "baseSeed")]
    [TestCase("{\"name\":\"x\",\"botTier\":\"2\"}", "botTier")]
    [TestCase("{\"name\":\"x\",\"botTier\":\"pro\"}", "botTier")]
    [TestCase("{\"name\":\"x\",\"characterOrder\":[\"Paladin\",\"Paladin\",\"Gunner\"]}", "characterOrder")]
    [TestCase("{\"name\":\"x\",\"characterOrder\":[\"Paladin\",\"Gunner\"]}", "characterOrder")]
    [TestCase("{\"name\":\"x\",\"runs\":0}", "runs")]
    [TestCase("{\"name\":\"x\",\"runs\":501}", "runs")]
    [TestCase("", "비어")]
    public void 틀린_시드는_칸을_짚어_거절(string json, string field)
    {
        Assert.IsFalse(QANightlySeed.TryParse(json, out _, out List<string> errors));
        Assert.IsTrue(errors.Exists(e => e.Contains(field)), string.Join("/", errors));
    }

    [Test]
    public void JSON_이_아니면_거절()
    {
        Assert.IsFalse(QANightlySeed.TryParse("not json", out _, out List<string> errors));
        Assert.IsNotEmpty(errors);
    }

    [Test]
    public void 시드가_103_판_설정이_된다()
    {
        QANightlySeed.TryParse(ValidSeed, out QANightlySeed s, out _);
        QABoss103Config cfg = s.ToConfig(3, 1, "C:/out");
        Assert.AreEqual(777, cfg.Seed);
        Assert.AreEqual(3, cfg.Run);
        Assert.AreEqual(1, cfg.Retry);
        Assert.AreEqual(1, cfg.Batch);
        Assert.AreEqual(QABoss103BotTier.Skilled, cfg.BotTier);
        Assert.AreEqual("eunhee", cfg.Nightly);
        Assert.AreEqual(QABoss103Character.Gunner, cfg.CharacterForSlot(0));
        Assert.AreEqual(QABoss103Character.Assassin, cfg.CharacterForSlot(2));
        Assert.AreEqual(QABoss103Plan.RunSeed(55, 3), cfg.TraitRunSeed);
        Assert.AreNotEqual(cfg.RunSeed, cfg.TraitRunSeed);
    }

    // ── 103 설정 확장 ────────────────────────────────────────

    [Test]
    public void 설정_파일_왕복에_순서_특성시드_이름이_남는다()
    {
        var cfg = new QABoss103Config
        {
            Seed = 9, Run = 4, Order = new[] { QABoss103Character.Assassin, QABoss103Character.Gunner, QABoss103Character.Paladin },
            TraitSeed = -12, Nightly = "kyungseok",
        };
        Assert.IsTrue(QABoss103Config.TryParse(cfg.Format(), out QABoss103Config back));
        CollectionAssert.AreEqual(cfg.Order, back.Order);
        Assert.AreEqual(-12, back.TraitSeed);
        Assert.AreEqual("kyungseok", back.Nightly);
        Assert.AreEqual(QABoss103Character.Assassin, back.CharacterForSlot(0));
    }

    [Test]
    public void 확장_키가_없으면_103_과_같다()
    {
        var cfg = new QABoss103Config { Seed = 103, Run = 2 };
        StringAssert.DoesNotContain("order=", cfg.Format());
        StringAssert.DoesNotContain("traitSeed=", cfg.Format());
        StringAssert.DoesNotContain("nightly=", cfg.Format());
        Assert.AreEqual(cfg.RunSeed, cfg.TraitRunSeed);
        for (int slot = 0; slot < 3; slot++)
            Assert.AreEqual(QABoss103Plan.CharacterForSlot(slot), cfg.CharacterForSlot(slot));
    }

    [TestCase("paladin, gunner ,ASSASSIN", true)]
    [TestCase("Paladin,Gunner", false)]
    [TestCase("Paladin,Gunner,Gunner", false)]
    [TestCase("0,1,2", false)]
    [TestCase("Paladin,Gunner,Wizard", false)]
    [TestCase("", false)]
    public void 자리_순서_파싱(string text, bool ok)
    {
        Assert.AreEqual(ok, QABoss103Plan.TryParseOrder(text, out QABoss103Character[] order));
        if (ok)
            Assert.AreEqual("Paladin,Gunner,Assassin", QABoss103Plan.FormatOrder(order));
    }

    // ── 폴더·경로 ────────────────────────────────────────────

    [Test]
    public void 공유_날짜_폴더는_MM_dd_평평한_구조()
    {
        var at = new DateTime(2026, 10, 27, 23, 40, 0);
        const string root = @"C:\Team-Vault\MainProject-Vault\QA_LogAndVideo";
        Assert.AreEqual(Path.Combine(root, "10-27"), QANightlyPaths.ShareDir(" " + root + " ", at));
        Assert.AreEqual(Path.Combine(root, "01-05"), QANightlyPaths.ShareDir(root, new DateTime(2027, 1, 5, 0, 30, 0)));
        Assert.IsNull(QANightlyPaths.ShareDir("", at));
        Assert.IsNull(QANightlyPaths.ShareDir("   ", at));
    }

    [Test]
    public void 날짜_폴더는_배치_시작_날()
    {
        Assert.AreEqual("10-27", QANightlyPaths.DateFolderName(new DateTime(2026, 10, 27, 23, 59, 59)));
        Assert.AreEqual("03-01", QANightlyPaths.DateFolderName(new DateTime(2026, 3, 1, 0, 0, 0)));
        Assert.AreEqual("12-31", QANightlyPaths.DateFolderName(new DateTime(2026, 12, 31, 22, 0, 0)), "자정을 넘겨도 시작 시각으로 정한다");
    }

    [Test]
    public void 리포트_이름_번호_매기기()
    {
        Assert.AreEqual("report-eunhee.md", QANightlyPaths.ReportFileName("eunhee", null));
        Assert.AreEqual("report-eunhee.md", QANightlyPaths.ReportFileName("eunhee", new[] { "report-kim.md", "eunhee_230000_r1_stall.mp4" }));
        Assert.AreEqual("report-eunhee-2.md", QANightlyPaths.ReportFileName("eunhee", new[] { @"C:\v\10-27\report-eunhee.md" }));
        Assert.AreEqual("report-eunhee-3.md", QANightlyPaths.ReportFileName("eunhee", new[] { "report-eunhee.md", "REPORT-EUNHEE-2.md" }), "대소문자 무시");
        Assert.AreEqual("report-eunhee-2.md", QANightlyPaths.ReportFileName("eunhee", new[] { "report-eunhee.md", "report-eunhee-3.md" }), "빈 번호부터");
    }

    [Test]
    public void 배치_폴더_이름은_시각순()
    {
        string a = QANightlyPaths.BatchFolderName("base", new DateTime(2026, 10, 27, 23, 5, 0));
        string b = QANightlyPaths.BatchFolderName("base", new DateTime(2026, 10, 28, 1, 0, 0));
        Assert.AreEqual("N108-20261027_2305-base", a);
        Assert.Less(string.CompareOrdinal(a, b), 0);
        StringAssert.StartsWith(QANightlyPaths.BatchPattern.TrimEnd('*'), a);
    }

    [TestCase("527", "527")]
    [TestCase(" 528\n", "528")]
    [TestCase("na", "unknown")]
    [TestCase("", "unknown")]
    [TestCase(null, "unknown")]
    [TestCase("svn: E155007: not a working copy", "unknown")]
    public void xlsx_리비전_정규화(string svn, string want)
    {
        Assert.AreEqual(want, QANightlyPaths.NormalizeRevision(svn));
    }

    // ── 머리말·요약 ──────────────────────────────────────────

    [Test]
    public void 배치_머리에_출처_리비전_브랜치_커밋_시드이름()
    {
        QANightlySeed.TryParse(ValidSeed, out QANightlySeed s, out _);
        List<KeyValuePair<string, string>> kv = QANightlyHeader.BatchKeys(s, new DateTime(2026, 10, 27, 23, 0, 0), "table", "na",
            "qa/Nightly-eunhee\n", "abc1234", "6000.3.16f1", QANightlyLocalSettings.RecordFull, @"C:\share\10-27");
        Dictionary<string, string> d = QABoss103Batch.ParseKeyValues(QABoss103Batch.FormatKeyValues(kv));
        Assert.AreEqual("table", d["data_source_mode"]);
        Assert.AreEqual("unknown", d["xlsx_svn_rev"]);
        Assert.AreEqual("qa/Nightly-eunhee", d["git_branch"]);
        Assert.AreEqual("abc1234", d["git_commit"]);
        Assert.AreEqual("eunhee", d[QANightlyHeader.KeySeedName]);
        Assert.AreEqual("777", d["seed"]);
        Assert.AreEqual("12", d["total"]);
        Assert.AreEqual("N108", d["scenario"]);
        Assert.AreEqual("Gunner,Paladin,Assassin", d["nightly_order"]);
        Assert.AreEqual("obs", d["record"]);
        Assert.AreEqual("2026-10-27 23:00:00", d["started_at"]);
    }

    [Test]
    public void 아침_요약에_머리말_판결과_녹화()
    {
        var batch = new Dictionary<string, string>
        {
            { QANightlyHeader.KeySeedName, "eunhee" }, { "xlsx_svn_rev", "528" }, { "git_branch", "qa/Nightly-eunhee" }, { "git_commit", "abc1234" },
            { "data_source_mode", "table" }, { "record", "obs" },
        };
        var recs = new List<QANightlyRecording> { new QANightlyRecording { Run = 2, Retry = 1, Path = @"D:\obs\a.mkv" } };
        var attempts = new List<QABoss103Attempt> { new QABoss103Attempt { Run = 1, OutcomeText = "Clear", DataSource = "table" } };
        string md = QANightlyHeader.Summary(batch, recs, attempts, "done", @"C:\QA\N108\report.html");
        StringAssert.Contains("| 시드 이름 | eunhee |", md);
        StringAssert.Contains("| GameData.xlsx SVN 리비전 | 528 |", md);
        StringAssert.Contains("qa/Nightly-eunhee / abc1234", md);
        StringAssert.Contains("| 1 | 0 | Clear | table |", md);
        StringAssert.Contains(@"판 2 재실행: D:\obs\a.mkv", md);
        StringAssert.Contains("(설정 없음 — 클립은 OBS 녹화 경로에)", md);
    }

    [Test]
    public void 녹화_기록_한줄_왕복()
    {
        var r = new QANightlyRecording { Run = 3, Retry = 0, Path = "D:\\v\\x\ty.mkv", Note = "영상 경로를 못 받음" };
        List<QANightlyRecording> back = QANightlyRecording.ParseAll(r.Format() + "\n\nbad line\n");
        Assert.AreEqual(1, back.Count);
        Assert.AreEqual(3, back[0].Run);
        Assert.AreEqual("D:\\v\\x y.mkv", back[0].Path);
        Assert.AreEqual("영상 경로를 못 받음", back[0].Note);
    }

    // ── OBS WebSocket v5 ─────────────────────────────────────

    [Test]
    public void OBS_인증_문자열은_문서_공식과_같다()
    {
        // base64(sha256(base64(sha256(password + salt)) + challenge)) — 같은 입력 = 같은 값, 입력이 바뀌면 다른 값.
        string a = QAObsProtocol.Auth("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=");
        Assert.AreEqual(a, QAObsProtocol.Auth("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
        Assert.AreNotEqual(a, QAObsProtocol.Auth("other", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
        Assert.AreEqual(44, a.Length); // sha256 32바이트의 base64
    }

    [Test]
    public void OBS_Hello_인증_있음_없음()
    {
        Assert.IsTrue(QAObsProtocol.TryParseHello("{\"op\":0,\"d\":{\"obsWebSocketVersion\":\"5.5.0\",\"rpcVersion\":1,\"authentication\":{\"challenge\":\"c1\",\"salt\":\"s1\"}}}",
            out int rpc, out bool auth, out string ch, out string salt));
        Assert.AreEqual(1, rpc);
        Assert.IsTrue(auth);
        Assert.AreEqual("c1", ch);
        Assert.AreEqual("s1", salt);

        Assert.IsTrue(QAObsProtocol.TryParseHello("{\"op\":0,\"d\":{\"rpcVersion\":1}}", out _, out bool noAuth, out _, out _));
        Assert.IsFalse(noAuth);
        Assert.IsFalse(QAObsProtocol.TryParseHello("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}", out _, out _, out _, out _));
    }

    [Test]
    public void OBS_Identify_Request_메시지()
    {
        Assert.AreEqual("{\"op\":1,\"d\":{\"rpcVersion\":1,\"eventSubscriptions\":0}}", QAObsProtocol.IdentifyJson(null));
        Assert.AreEqual("{\"op\":1,\"d\":{\"rpcVersion\":1,\"authentication\":\"a\\\"b\",\"eventSubscriptions\":0}}", QAObsProtocol.IdentifyJson("a\"b"));
        Assert.AreEqual("{\"op\":6,\"d\":{\"requestType\":\"StartRecord\",\"requestId\":\"r1\"}}", QAObsProtocol.RequestJson(QAObsProtocol.StartRecord, "r1"));
        Assert.AreEqual(2, QAObsProtocol.OpOf("{\"op\":2,\"d\":{}}"));
        Assert.AreEqual(-1, QAObsProtocol.OpOf("garbage"));
    }

    [Test]
    public void OBS_StopRecord_응답에서_영상_경로()
    {
        const string json = "{\"op\":7,\"d\":{\"requestType\":\"StopRecord\",\"requestId\":\"r9\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                            "\"responseData\":{\"outputPath\":\"D:\\\\obs\\\\2026-10-27 23-00-00.mkv\"}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(json, "r9", out bool ok, out int code, out _, out string path, out _));
        Assert.IsTrue(ok);
        Assert.AreEqual(100, code);
        Assert.AreEqual(@"D:\obs\2026-10-27 23-00-00.mkv", path);
        Assert.IsFalse(QAObsProtocol.TryParseResponse(json, "other", out _, out _, out _, out _, out _));

        const string running = "{\"op\":7,\"d\":{\"requestType\":\"StartRecord\",\"requestId\":\"r1\",\"requestStatus\":{\"result\":false,\"code\":500,\"comment\":\"x\"}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(running, "r1", out bool ok2, out int code2, out string comment, out _, out _));
        Assert.IsFalse(ok2);
        Assert.AreEqual(QAObsProtocol.CodeOutputRunning, code2);
        Assert.AreEqual("x", comment);
    }

    [Test]
    public void OBS_연결_실패는_예외없이_오류()
    {
        // 닫힌 포트 — 바로 거절. QA 를 막지 않는다.
        QAObsResult r = QAObsClient.Send("127.0.0.1", 1, "", QAObsProtocol.GetRecordStatus, 2.0);
        Assert.IsFalse(r.Ok);
        Assert.IsNotEmpty(r.Error);
    }

    // ── PC 설정 ──────────────────────────────────────────────

    [Test]
    public void PC_설정_기본은_녹화_꺼짐_복사_안함()
    {
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge(null, _ => null);
        Assert.IsFalse(s.ObsEnabled);
        Assert.AreEqual("127.0.0.1", s.ObsHost);
        Assert.AreEqual(4455, s.ObsPort);
        Assert.AreEqual("", s.ShareRoot);
        Assert.IsTrue(QANightlyLocalSettings.Merge(QANightlyLocalSettings.Template, _ => null).Source.Contains(QANightlyLocalSettings.RelativePath));
    }

    [Test]
    public void PC_설정은_환경변수가_파일을_덮는다()
    {
        var env = new Dictionary<string, string> { { "QA_NIGHTLY_OBS", "1" }, { "QA_OBS_PORT", "4460" }, { "QA_OBS_PASSWORD", "pw" }, { "QA_NIGHTLY_SHARE_ROOT", @"C:\Team-Vault\QA" } };
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge("{\"obsEnabled\":false,\"obsHost\":\"10.0.0.2\",\"obsPort\":4455,\"shareRoot\":\"D:\\\\x\"}",
            k => env.TryGetValue(k, out string v) ? v : null);
        Assert.IsTrue(s.ObsEnabled);
        Assert.AreEqual("10.0.0.2", s.ObsHost);
        Assert.AreEqual(4460, s.ObsPort);
        Assert.AreEqual("pw", s.ObsPassword);
        Assert.AreEqual(@"C:\Team-Vault\QA", s.ShareRoot);
        StringAssert.DoesNotContain("pw", s.Describe());
    }

    [Test]
    public void PC_설정_틀린_포트는_기본값()
    {
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge("{\"obsPort\":99999}", k => k == "QA_OBS_PORT" ? "abc" : null);
        Assert.AreEqual(4455, s.ObsPort);
    }

    // ── 할 일 120: PC 설정(화질·보관·리플레이) ──────────────────

    [Test]
    public void PC_설정_120_기본은_아무것도_안_바꾼다()
    {
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge(null, _ => null);
        Assert.AreEqual(QANightlyLocalSettings.RecordOff, s.RecordMode);
        Assert.IsFalse(s.ReplayOnly);
        Assert.IsFalse(s.HasQualitySettings);
        Assert.AreEqual(0, s.VideoKeepDays);
        Assert.AreEqual(0, s.ReplaySeconds);
        Assert.AreEqual(10.0, s.ReplayAfterSeconds, 1e-9);
        Assert.AreEqual(30.0, s.ReplayCooldownSeconds, 1e-9);
        Assert.AreEqual("", s.ClipEvents);

        QANightlyLocalSettings t = QANightlyLocalSettings.Merge(QANightlyLocalSettings.Template, _ => null);
        Assert.IsFalse(t.HasQualitySettings, "서식 파일 그대로면 화질을 건드리지 않는다");
        Assert.AreEqual(QANightlyLocalSettings.RecordOff, t.RecordMode);
        Assert.AreEqual(10.0, t.ReplayAfterSeconds, 1e-9);
    }

    [Test]
    public void PC_설정_120_녹화_모드_스위치()
    {
        Assert.AreEqual(QANightlyLocalSettings.RecordFull, QANightlyLocalSettings.Merge("{\"obsEnabled\":true}", _ => null).RecordMode);
        Assert.AreEqual(QANightlyLocalSettings.RecordReplay, QANightlyLocalSettings.Merge("{\"obsEnabled\":true,\"replayOnly\":true}", _ => null).RecordMode);
        Assert.AreEqual(QANightlyLocalSettings.RecordReplay, QANightlyLocalSettings.Merge("{\"obsEnabled\":true}", k => k == "QA_NIGHTLY_REPLAY" ? "1" : null).RecordMode);
        Assert.AreEqual(QANightlyLocalSettings.RecordOff, QANightlyLocalSettings.Merge("{\"replayOnly\":true}", _ => null).RecordMode, "OBS 가 꺼져 있으면 스위치와 관계없이 꺼짐");
    }

    [Test]
    public void PC_설정_공유_모드는_리플레이_고정()
    {
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge("{\"obsEnabled\":true,\"replayOnly\":false,\"shareRoot\":\"C:\\\\Team-Vault\\\\MainProject-Vault\\\\QA_LogAndVideo\"}", _ => null);
        Assert.IsTrue(s.SharedMode);
        Assert.AreEqual(QANightlyLocalSettings.RecordReplay, s.RecordMode, "공유 루트가 있으면 판 전체 녹화를 쓰지 않는다");
        Assert.AreEqual(QANightlyLocalSettings.RecordReplay,
            QANightlyLocalSettings.Merge("{\"obsEnabled\":true}", k => k == "QA_NIGHTLY_SHARE_ROOT" ? @"D:\Vault\QA_LogAndVideo" : null).RecordMode, "환경변수 공유 루트도 같다");
        Assert.AreEqual(QANightlyLocalSettings.RecordOff, QANightlyLocalSettings.Merge("{\"shareRoot\":\"D:\\\\x\"}", _ => null).RecordMode, "OBS 꺼짐이면 공유 모드여도 녹화 없음");
        Assert.IsFalse(QANightlyLocalSettings.Merge(null, _ => null).SharedMode);
    }

    [Test]
    public void PC_설정_120_파일과_환경변수()
    {
        const string file = "{\"obsEnabled\":true,\"videoWidth\":1280,\"videoHeight\":720,\"videoFps\":24,\"videoBitrateKbps\":2500,\"videoKeepDays\":7," +
                            "\"replaySeconds\":90,\"replayAfterSeconds\":5,\"replayCooldownSeconds\":20,\"clipEvents\":\"stall,death\",\"obsBitrateParam\":\"SimpleOutput/VBitrate\"}";
        var env = new Dictionary<string, string>
        {
            { "QA_OBS_VIDEO_FPS", "abc" }, { "QA_OBS_BITRATE_KBPS", "1500" }, { "QA_NIGHTLY_VIDEO_KEEP_DAYS", "14" }, { "QA_OBS_REPLAY_AFTER", "7.5" },
            { "QA_OBS_REPLAY_COOLDOWN", "-3" }, { "QA_NIGHTLY_CLIP_EVENTS", "wipe" },
        };
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge(file, k => env.TryGetValue(k, out string v) ? v : null);
        Assert.AreEqual(1280, s.VideoWidth);
        Assert.AreEqual(720, s.VideoHeight);
        Assert.AreEqual(24, s.VideoFps, "숫자가 아닌 환경변수는 무시");
        Assert.AreEqual(1500, s.BitrateKbps);
        Assert.AreEqual(14, s.VideoKeepDays);
        Assert.AreEqual(90, s.ReplaySeconds);
        Assert.AreEqual(7.5, s.ReplayAfterSeconds, 1e-9);
        Assert.AreEqual(20.0, s.ReplayCooldownSeconds, 1e-9, "음수 환경변수는 무시");
        Assert.AreEqual("wipe", s.ClipEvents);
        Assert.AreEqual("SimpleOutput/VBitrate", s.BitrateParam);
        Assert.IsTrue(s.HasQualitySettings);
        StringAssert.Contains("1280x720/24fps/1500kbps", s.Describe());
    }

    [Test]
    public void PC_설정_120_음수는_0()
    {
        QANightlyLocalSettings s = QANightlyLocalSettings.Merge("{\"videoWidth\":-5,\"videoKeepDays\":-1,\"replayAfterSeconds\":-2}", _ => null);
        Assert.AreEqual(0, s.VideoWidth);
        Assert.AreEqual(0, s.VideoKeepDays);
        Assert.AreEqual(0.0, s.ReplayAfterSeconds, 1e-9);
    }

    // ── 할 일 120: OBS 메시지 ────────────────────────────────

    [Test]
    public void OBS_요청_데이터_메시지()
    {
        Assert.AreEqual("{\"op\":1,\"d\":{\"rpcVersion\":1,\"eventSubscriptions\":64}}", QAObsProtocol.IdentifyJson(null, QAObsProtocol.SubscriptionOutputs));
        Assert.AreEqual("{\"op\":6,\"d\":{\"requestType\":\"SaveReplayBuffer\",\"requestId\":\"r2\"}}", QAObsProtocol.RequestJson(QAObsProtocol.SaveReplayBuffer, "r2"));
        Assert.AreEqual("{\"op\":6,\"d\":{\"requestType\":\"SetVideoSettings\",\"requestId\":\"r3\",\"requestData\":{\"fpsNumerator\":24,\"fpsDenominator\":1}}}",
            QAObsProtocol.RequestJson(QAObsProtocol.SetVideoSettings, "r3", QAObsProtocol.VideoSettingsData(0, 0, 24)));
        Assert.AreEqual("{\"outputWidth\":1280,\"outputHeight\":720,\"fpsNumerator\":30,\"fpsDenominator\":1}", QAObsProtocol.VideoSettingsData(1280, 720, 30));
        Assert.AreEqual("{\"outputWidth\":960,\"outputHeight\":540}", QAObsProtocol.VideoSettingsData(960, 540, 0));
        Assert.IsNull(QAObsProtocol.VideoSettingsData(1280, 0, 0), "짝이 안 맞는 해상도는 보내지 않는다");
        Assert.IsNull(QAObsProtocol.VideoSettingsData(0, 0, 0));
        Assert.AreEqual("{\"parameterCategory\":\"SimpleOutput\",\"parameterName\":\"VBitrate\",\"parameterValue\":\"2500\"}",
            QAObsProtocol.ProfileParameterData("SimpleOutput", "VBitrate", "2500"));
        Assert.AreEqual("{\"parameterCategory\":\"Output\",\"parameterName\":\"Mode\"}", QAObsProtocol.ProfileParameterQuery("Output", "Mode"));
    }

    [Test]
    public void OBS_응답_데이터_녹화경과_클립경로_프로필값()
    {
        const string status = "{\"op\":7,\"d\":{\"requestType\":\"GetRecordStatus\",\"requestId\":\"s1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                              "\"responseData\":{\"outputActive\":true,\"outputPaused\":false,\"outputTimecode\":\"00:00:12.345\",\"outputDuration\":12345,\"outputBytes\":1000}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(status, "s1", out QAObsResult r));
        Assert.IsTrue(r.Ok);
        Assert.IsTrue(r.OutputActive);
        Assert.AreEqual(12345.0, r.OutputDurationMs, 1e-6);
        Assert.AreEqual("00:00:12.345", r.OutputTimecode);

        const string last = "{\"op\":7,\"d\":{\"requestType\":\"GetLastReplayBufferReplay\",\"requestId\":\"l1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                            "\"responseData\":{\"savedReplayPath\":\"D:\\\\obs\\\\Replay 2026-10-27 23-10-05.mkv\"}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(last, "l1", out QAObsResult l));
        Assert.AreEqual(@"D:\obs\Replay 2026-10-27 23-10-05.mkv", l.SavedReplayPath);

        const string param = "{\"op\":7,\"d\":{\"requestType\":\"GetProfileParameter\",\"requestId\":\"p1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                             "\"responseData\":{\"parameterValue\":\"Simple\",\"defaultParameterValue\":\"Simple\"}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(param, "p1", out QAObsResult p));
        Assert.AreEqual("Simple", p.ParameterValue);
        Assert.IsFalse(QAObsProtocol.TryParseResponse(param, "other", out QAObsResult _));
    }

    [Test]
    public void OBS_ReplayBufferSaved_이벤트()
    {
        const string ev = "{\"op\":5,\"d\":{\"eventType\":\"ReplayBufferSaved\",\"eventIntent\":64,\"eventData\":{\"savedReplayPath\":\"D:\\\\obs\\\\r.mkv\"}}}";
        Assert.IsTrue(QAObsProtocol.TryParseEvent(ev, out string type, out string path));
        Assert.AreEqual(QAObsProtocol.EventReplayBufferSaved, type);
        Assert.AreEqual(@"D:\obs\r.mkv", path);
        Assert.IsTrue(QAObsProtocol.TryParseEvent("{\"op\":5,\"d\":{\"eventType\":\"RecordStateChanged\",\"eventData\":{\"outputActive\":true}}}", out string t2, out string p2));
        Assert.AreEqual("RecordStateChanged", t2);
        Assert.AreEqual("", p2);
        Assert.IsFalse(QAObsProtocol.TryParseEvent("{\"op\":7,\"d\":{}}", out _, out _));
        Assert.IsFalse(QAObsProtocol.TryParseEvent("garbage", out _, out _));
    }

    [TestCase("00:01:02.500", 62500.0)]
    [TestCase("01:00:00.000", 3600000.0)]
    [TestCase("0:0:5", 5000.0)]
    public void OBS_타임코드_파싱(string tc, double ms)
    {
        Assert.AreEqual(ms, QAObsProtocol.ParseTimecodeMs(tc), 1e-6);
    }

    [TestCase("")]
    [TestCase((string)null)]
    [TestCase("12:34")]
    [TestCase("aa:bb:cc")]
    public void OBS_타임코드_형식이_아니면_NaN(string tc)
    {
        Assert.IsTrue(double.IsNaN(QAObsProtocol.ParseTimecodeMs(tc)));
    }

    [Test]
    public void OBS_프로필_파라미터_이름_출력모드별()
    {
        Assert.IsTrue(QAObsProfileParams.TryBitrate("Simple", "", out string c, out string n));
        Assert.AreEqual("SimpleOutput/VBitrate", c + "/" + n);
        Assert.IsFalse(QAObsProfileParams.TryBitrate("Advanced", "", out _, out _), "고급 모드 녹화 비트레이트는 프로필 파라미터가 아니다");
        Assert.IsFalse(QAObsProfileParams.TryBitrate("", null, out _, out _));
        Assert.IsTrue(QAObsProfileParams.TryBitrate("Advanced", " AdvOut / FFVBitrate ", out c, out n), "직접 지정은 모드와 관계없이");
        Assert.AreEqual("AdvOut/FFVBitrate", c + "/" + n);

        Assert.IsTrue(QAObsProfileParams.TryReplaySeconds("simple", out c, out n));
        Assert.AreEqual("SimpleOutput/RecRBTime", c + "/" + n);
        Assert.IsTrue(QAObsProfileParams.TryReplaySeconds("Advanced", out c, out n));
        Assert.AreEqual("AdvOut/RecRBTime", c + "/" + n);
        Assert.IsTrue(QAObsProfileParams.TryReplayEnable("Advanced", out c, out n));
        Assert.AreEqual("AdvOut/RecRB", c + "/" + n);
        Assert.IsFalse(QAObsProfileParams.TryReplaySeconds("", out _, out _));

        Assert.IsFalse(QAObsProfileParams.TrySplit("VBitrate", out _, out _));
        Assert.IsFalse(QAObsProfileParams.TrySplit("/VBitrate", out _, out _));
        Assert.IsFalse(QAObsProfileParams.TrySplit("SimpleOutput/", out _, out _));
    }

    // ── 할 일 120: 타임코드 ──────────────────────────────────

    [Test]
    public void 영상_타임코드는_기준점_경과와_보정()
    {
        var anchor = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(62.5, QANightlyTimecode.VideoSeconds(anchor, 2500, anchor.AddSeconds(60)), 1e-6, "이미 녹화 중이던 2.5초를 더한다");
        Assert.AreEqual(60.0, QANightlyTimecode.VideoSeconds(anchor, 0, anchor.AddSeconds(60)), 1e-6);
        Assert.AreEqual(0.0, QANightlyTimecode.VideoSeconds(anchor, 0, anchor.AddSeconds(-5)), 1e-6, "기준보다 앞이면 0");
        Assert.IsTrue(double.IsNaN(QANightlyTimecode.VideoSeconds(DateTime.MinValue, 0, anchor)), "기준 없음 = 모름");

        Assert.AreEqual(1200.0, QANightlyTimecode.OffsetMs(1200, "00:00:09.000"), 1e-6, "outputDuration 우선");
        Assert.AreEqual(9000.0, QANightlyTimecode.OffsetMs(0, "00:00:09.000"), 1e-6, "없으면 outputTimecode");
        Assert.AreEqual(0.0, QANightlyTimecode.OffsetMs(0, ""), 1e-6);

        Assert.AreEqual("1:02:05", QANightlyTimecode.Format(3725.9));
        Assert.AreEqual("0:00:00", QANightlyTimecode.Format(0));
        Assert.AreEqual("-", QANightlyTimecode.Format(double.NaN));
    }

    [Test]
    public void 녹화_기준점_왕복_108_형식도_읽는다()
    {
        var a = new QANightlyRecordAnchor { Run = 4, Retry = 1, AnchorUtc = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc), OffsetMs = 1530 };
        QANightlyRecordAnchor back = QANightlyRecordAnchor.Parse(a.Format());
        Assert.AreEqual(4, back.Run);
        Assert.AreEqual(1, back.Retry);
        Assert.AreEqual(a.AnchorUtc, back.AnchorUtc);
        Assert.AreEqual(1530.0, back.OffsetMs, 1e-6);

        QANightlyRecordAnchor old = QANightlyRecordAnchor.Parse("3\t0\n");
        Assert.AreEqual(3, old.Run);
        Assert.AreEqual(DateTime.MinValue, old.AnchorUtc, "108 표시 파일 = 기준 없음(타임코드 모름)");
        Assert.IsNull(QANightlyRecordAnchor.Parse(""));
        Assert.IsNull(QANightlyRecordAnchor.Parse(null));
    }

    // ── 할 일 120: 이벤트 표·시각표 ──────────────────────────

    [TestCase("[QA103] event=stall combatS=300.0 quietS=90.0 lastActivity=none", LogType.Warning, QANightlyEvents.Stall)]
    [TestCase("[QA103] event=qa_relocate character=Paladin client=1 ok=true from=(0,0,0)", LogType.Log, QANightlyEvents.Stuck)]
    [TestCase("[QA][Warning][GameplayInvariant] 플레이어 콜라이더가 벽/환경 지오메트리를 관통함", LogType.Log, QANightlyEvents.Stuck)]
    [TestCase("[QA][Warning][Softlock] 이동 불능", LogType.Log, QANightlyEvents.Stuck)]
    [TestCase("[QA103] event=death slot=2 client=2 state=DeadPresentation", LogType.Log, QANightlyEvents.Death)]
    [TestCase("[QA103] event=run_end run=3 outcome=Wipe combatS=412.0 seed=108", LogType.Log, QANightlyEvents.Wipe)]
    [TestCase("[QA103] event=run_end run=3 outcome=Timeout combatS=1800.0 seed=108", LogType.Log, QANightlyEvents.Timeout)]
    [TestCase("[QA103] event=run_end run=3 outcome=Error combatS=300.0 seed=108", LogType.Log, QANightlyEvents.Error)]
    [TestCase("[QA103] event=early_timeout combatS=700.0 flatS=300.0", LogType.Warning, QANightlyEvents.Timeout)]
    [TestCase("[QA][Error][LogError] NullReferenceException", LogType.Log, QANightlyEvents.Error)]
    [TestCase("NullReferenceException: Object reference not set", LogType.Exception, QANightlyEvents.Error)]
    [TestCase("뭔가 실패", LogType.Error, QANightlyEvents.Error)]
    public void 이벤트_표가_호스트_로그를_고른다(string line, LogType type, string want)
    {
        QANightlyEventRule r = QANightlyEvents.Match(line, type, null);
        Assert.IsNotNull(r, line);
        Assert.AreEqual(want, r.Name);
    }

    [TestCase("[QA103] event=qa_relocate_request character=Paladin n=1 sent=true", LogType.Log)]
    [TestCase("[QA103] event=qa_relocate_on registered=true", LogType.Log)]
    [TestCase("[QA103] event=run_end run=3 outcome=Clear combatS=600.0", LogType.Log)]
    [TestCase("뭔가 실패", LogType.Warning)]
    [TestCase("[QA][Info][LogError-Noise] 알려진 소음", LogType.Log)]
    [TestCase("", LogType.Error)]
    public void 이벤트가_아닌_로그는_고르지_않는다(string line, LogType type)
    {
        Assert.IsNull(QANightlyEvents.Match(line, type, null), line);
    }

    [Test]
    public void 이벤트_목록_설정()
    {
        HashSet<string> set = QANightlyEvents.ParseEnabled(" Death, stall ;wipe");
        CollectionAssert.AreEquivalent(new[] { "death", "stall", "wipe" }, set);
        Assert.IsNull(QANightlyEvents.ParseEnabled(""));
        Assert.IsNull(QANightlyEvents.ParseEnabled(" , "));
        Assert.IsNull(QANightlyEvents.Match("[QA103] event=stall combatS=1", LogType.Warning, new HashSet<string> { "death" }));
        Assert.IsNotNull(QANightlyEvents.Match("[QA103] event=death slot=0", LogType.Log, new HashSet<string> { "death" }));
        foreach (QANightlyEventRule r in QANightlyEvents.Table)
            Assert.AreEqual(r.Name, r.Name.ToLowerInvariant(), "표 이름은 소문자(설정 목록과 맞춘다)");
    }

    [Test]
    public void 특성_라운드_줄은_기록_전용으로_시각표에()
    {
        string line = "[QA103] event=trait_round round=4 directorRound=4 complete=true autoChosen=0 acquired=" + new string('x', 400) + "_TAIL";
        QANightlyEventRule r = QANightlyEvents.Match(line, LogType.Log, null);
        Assert.IsNotNull(r);
        Assert.AreEqual(QANightlyEvents.Trait, r.Name);
        Assert.IsTrue(r.Trace, "클립·반복 거르기 없음");
        StringAssert.EndsWith("_TAIL", QANightlyEvents.Detail(line, r.DetailMax), "획득 ID 가 잘리지 않는다");
        Assert.IsTrue(QANightlyEvents.IsTrace(QANightlyEvents.Trait));
        Assert.IsFalse(QANightlyEvents.IsTrace(QANightlyEvents.Stuck));
        // 에러 로그는 여전히 오류로 — 기록 전용 줄이 표 앞에서 가로채지 않는다.
        Assert.AreEqual(QANightlyEvents.Error, QANightlyEvents.Match(line, LogType.Error, null).Name);
    }

    [Test]
    public void 요약_이벤트_표에서_기록_전용은_빠진다()
    {
        var events = new List<QANightlyEvent>
        {
            new QANightlyEvent { Run = 1, Name = QANightlyEvents.Trait, At = new DateTime(2026, 10, 29, 1, 0, 0), Detail = "TRAIT_ROW" },
            new QANightlyEvent { Run = 1, Name = QANightlyEvents.Stuck, At = new DateTime(2026, 10, 29, 1, 0, 5), Detail = "STUCK_ROW" },
        };
        string md = QANightlyHeader.Summary(new Dictionary<string, string>(), null, null, "done", "", events);
        StringAssert.Contains("STUCK_ROW", md);
        StringAssert.DoesNotContain("TRAIT_ROW", md);
    }

    [Test]
    public void 시각표_상세는_첫줄_탭없이_자른다()
    {
        Assert.AreEqual("a b", QANightlyEvents.Detail("a\tb\nstack"));
        Assert.AreEqual(160, QANightlyEvents.Detail(new string('x', 500)).Length);
        Assert.AreEqual("", QANightlyEvents.Detail(null));
    }

    [Test]
    public void 시각표_한줄_왕복()
    {
        var e = new QANightlyEvent
        {
            Run = 2, Retry = 1, Name = QANightlyEvents.Stall, At = new DateTime(2026, 10, 27, 23, 10, 5, 250), GameSeconds = 312.54, VideoSeconds = double.NaN, Detail = "x\ty",
        };
        List<QANightlyEvent> back = QANightlyEvent.ParseAll(e.Format() + "\nbad\n\n");
        Assert.AreEqual(1, back.Count);
        Assert.AreEqual(2, back[0].Run);
        Assert.AreEqual(1, back[0].Retry);
        Assert.AreEqual("stall", back[0].Name);
        Assert.AreEqual(e.At, back[0].At);
        Assert.AreEqual(312.5, back[0].GameSeconds, 1e-6);
        Assert.IsTrue(double.IsNaN(back[0].VideoSeconds));
        Assert.AreEqual("x y", back[0].Detail);
    }

    [Test]
    public void 시각표는_같은_이벤트_5초_안_반복을_거른다()
    {
        var t = new QANightlyEventThrottle();
        var t0 = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        Assert.IsTrue(t.Allow("1/0", "error", t0));
        Assert.IsFalse(t.Allow("1/0", "error", t0.AddSeconds(4.9)));
        Assert.IsTrue(t.Allow("1/0", "stall", t0.AddSeconds(1)), "다른 이벤트는 따로");
        Assert.IsTrue(t.Allow("1/0", "error", t0.AddSeconds(5.1)));
        Assert.IsTrue(t.Allow("2/0", "error", t0.AddSeconds(5.2)), "판이 바뀌면 다시 센다");
        for (int i = 0; i < QANightlyEventThrottle.MaxPerAttempt + 5; i++)
            t.Allow("3/0", "error", t0.AddSeconds(10 * i));
        Assert.IsFalse(t.Allow("3/0", "death", t0.AddDays(1)), "판당 상한");
    }

    // ── 할 일 120: 리플레이 클립 ─────────────────────────────

    [Test]
    public void 클립_대기_중_이벤트는_하나로_합친다()
    {
        var p = new QANightlyClipPlanner();
        var t0 = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        p.Add(t0, "stall", 3, 0, 10, 30);
        Assert.AreEqual(t0.AddSeconds(10), p.DueUtc, "이벤트 뒤 M초까지 담게 늦춘다");
        p.Add(t0.AddSeconds(3), "death", 3, 0, 10, 30);
        p.Add(t0.AddSeconds(4), "stall", 3, 0, 10, 30);
        Assert.AreEqual(t0.AddSeconds(10), p.DueUtc, "대기 중 이벤트는 예약을 미루지 않는다");
        Assert.IsFalse(p.TryTake(t0.AddSeconds(9.9), false, out _, out _, out _));
        Assert.IsTrue(p.TryTake(t0.AddSeconds(10), false, out List<string> names, out int run, out int retry));
        CollectionAssert.AreEqual(new[] { "stall", "death" }, names);
        Assert.AreEqual(3, run);
        Assert.AreEqual(0, retry);
        Assert.IsFalse(p.Pending);
        Assert.AreEqual(t0.AddSeconds(10), p.LastSaveUtc);
    }

    [Test]
    public void 클립_쿨다운_안_이벤트는_다음_클립으로_모은다()
    {
        var p = new QANightlyClipPlanner();
        var t0 = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        p.Add(t0, "error", 1, 0, 10, 30);
        Assert.IsTrue(p.TryTake(t0.AddSeconds(10), false, out _, out _, out _));
        p.Add(t0.AddSeconds(12), "wipe", 1, 0, 10, 30);
        Assert.AreEqual(t0.AddSeconds(40), p.DueUtc, "지난 저장 + 쿨다운 30초까지 미룬다");
        p.Add(t0.AddSeconds(25), "timeout", 1, 0, 10, 30);
        Assert.IsTrue(p.TryTake(t0.AddSeconds(40), false, out List<string> names, out _, out _));
        CollectionAssert.AreEqual(new[] { "wipe", "timeout" }, names);
        p.Add(t0.AddSeconds(100), "death", 2, 1, 10, 30);
        Assert.AreEqual(t0.AddSeconds(110), p.DueUtc, "쿨다운이 지났으면 이벤트 + M초");
        Assert.AreEqual(2, p.Run);
        Assert.AreEqual(1, p.Retry);
    }

    [Test]
    public void 클립_예약은_파일로_도메인_리로드를_넘는다()
    {
        var p = new QANightlyClipPlanner();
        var t0 = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        p.Add(t0, "stall", 5, 1, 8, 30);
        p.Add(t0.AddSeconds(1), "death", 5, 1, 8, 30);
        QANightlyClipPlanner back = QANightlyClipPlanner.Parse(p.Format());
        Assert.AreEqual(p.DueUtc, back.DueUtc);
        Assert.AreEqual(5, back.Run);
        Assert.AreEqual(1, back.Retry);
        CollectionAssert.AreEqual(new[] { "stall", "death" }, back.Names);
        Assert.IsTrue(back.TryTake(t0, true, out List<string> names, out _, out _), "배치 끝 = 시각과 관계없이 꺼낸다");
        Assert.AreEqual(2, names.Count);
        QANightlyClipPlanner afterTake = QANightlyClipPlanner.Parse(back.Format());
        Assert.IsFalse(afterTake.Pending);
        Assert.AreEqual(t0, afterTake.LastSaveUtc);
        Assert.IsFalse(QANightlyClipPlanner.Parse(null).Pending);
        Assert.IsFalse(QANightlyClipPlanner.Parse("garbage").Pending);
    }

    [Test]
    public void 클립_파일명은_이름_시각_판_이벤트()
    {
        var at = new DateTime(2026, 10, 27, 23, 5, 9);
        Assert.AreEqual("eunhee_230509_r3_stall+death.mp4",
            QANightlyClipName.FileName("eunhee", at, 3, 0, new[] { "stall", "death", "stall" }, ".mp4"));
        Assert.AreEqual("eunhee_230509_r3re_wipe.mp4", QANightlyClipName.FileName("eunhee", at, 3, 1, new[] { "wipe" }, "mp4"));
        Assert.AreEqual("eunhee_000001_r1_event.mp4", QANightlyClipName.FileName("eunhee", new DateTime(2026, 10, 28, 0, 0, 1), 1, 0, new string[0], ""),
            "확장자가 없으면 .mp4");
        Assert.AreEqual("eunhee_230509_r1_wipe.mkv", QANightlyClipName.FileName("eunhee", at, 1, 0, new[] { "wipe" }, ".mkv"), "확장자는 OBS 가 만든 그대로");
        Assert.AreEqual("nightly_230509_r1_ab.mp4", QANightlyClipName.FileName("", at, 1, 0, new[] { "a/b" }, ".mp4"), "파일명에 못 쓰는 글자는 뺀다");
        StringAssert.EndsWith("_e1+e2+e3+e4+e5+e6+etc.mp4",
            QANightlyClipName.FileName("x", at, 1, 0, new[] { "e1", "e2", "e3", "e4", "e5", "e6", "e7" }, ".mp4"));
    }

    [TestCase("eunhee_230509_r3_stall.mp4", "eunhee", true)]
    [TestCase("eunhee_230509_r3re_wipe+death.mkv", "eunhee", true)]
    [TestCase("eunhee_230509_r3_stall_2.mp4", "eunhee", true)]
    [TestCase(@"C:\v\10-27\eunhee_230509_r3_stall.mp4", "eunhee", true)]
    [TestCase("kim_lee_230509_r1_stall.mp4", "kim", false)]
    [TestCase("kim2_230509_r1_stall.mp4", "kim", false)]
    [TestCase("kim_230509_r1_stall.mp4", "eunhee", false)]
    [TestCase("report-eunhee.md", "eunhee", false)]
    [TestCase("eunhee_230509_r3_stall.md", "eunhee", false)]
    [TestCase("eunhee_2305_r3_stall.mp4", "eunhee", false)]
    [TestCase("Replay 2026-10-27 23-05-09.mp4", "eunhee", false)]
    [TestCase("eunhee_230509_r3_stall.mp4", "", false)]
    public void 내_클립_판정(string file, string name, bool mine)
    {
        Assert.AreEqual(mine, QANightlyClipName.IsOwnClip(file, name));
    }

    [Test]
    public void 클립_이름이_겹치면_번호를_붙인다()
    {
        var taken = new HashSet<string> { Path.Combine("D:/obs", "a.mkv"), Path.Combine("D:/obs", "a_2.mkv") };
        Assert.AreEqual(Path.Combine("D:/obs", "a_3.mkv"), QANightlyClipName.Unique("D:/obs", "a.mkv", taken.Contains));
        Assert.AreEqual(Path.Combine("D:/obs", "b.mkv"), QANightlyClipName.Unique("D:/obs", "b.mkv", taken.Contains));
    }

    // ── 할 일 120: 공유 폴더 영상 보관 ───────────────────────

    [Test]
    public void 보관_삭제는_MM_dd_날짜_폴더만_N일_넘긴_것()
    {
        var today = new DateTime(2026, 10, 27, 23, 30, 0);
        var names = new[] { "10-27", "10-20", "10-19", "09-01", "logs", "1-05", "10-19-old", "2026-10-01", "48", "13-01" };
        CollectionAssert.AreEqual(new[] { "09-01", "10-19" }, QANightlyRetention.ExpiredFolders(names, today, 7));
        Assert.IsEmpty(QANightlyRetention.ExpiredFolders(names, today, 0), "0 = 지우지 않음");
        Assert.IsFalse(QANightlyRetention.IsExpired("10-26", today, 1));
        Assert.IsTrue(QANightlyRetention.IsExpired("10-25", today, 1));
        Assert.IsNull(QANightlyRetention.FolderDate("N108-20261027_2305-base", today));
        Assert.IsNull(QANightlyRetention.FolderDate("2026-10-27", today), "연도 붙은 옛 형식은 건드리지 않는다");
    }

    [Test]
    public void 보관_날짜_폴더는_오늘_기준_과거로_읽는다()
    {
        var jan = new DateTime(2027, 1, 3, 9, 0, 0);
        Assert.AreEqual(new DateTime(2026, 12, 30), QANightlyRetention.FolderDate("12-30", jan), "올해 날짜가 미래면 작년");
        Assert.AreEqual(new DateTime(2027, 1, 3), QANightlyRetention.FolderDate("01-03", jan), "오늘은 올해");
        Assert.IsFalse(QANightlyRetention.IsExpired("12-30", jan, 7), "해를 넘겨도 4일 전");
        Assert.IsTrue(QANightlyRetention.IsExpired("12-20", jan, 7));
        Assert.AreEqual(new DateTime(2026, 1, 4), QANightlyRetention.FolderDate("01-04", jan), "내일 날짜는 작년으로 읽는다");
        Assert.IsTrue(QANightlyRetention.IsExpired("01-04", jan, 7));
    }

    [Test]
    public void 보관_2월_29일은_가장_가까운_윤년()
    {
        Assert.AreEqual(new DateTime(2028, 2, 29), QANightlyRetention.FolderDate("02-29", new DateTime(2028, 3, 1)));
        Assert.AreEqual(new DateTime(2024, 2, 29), QANightlyRetention.FolderDate("02-29", new DateTime(2027, 3, 1)));
        Assert.IsNull(QANightlyRetention.FolderDate("02-30", new DateTime(2027, 3, 1)));
    }

    [TestCase("2026-10-27 23-10-05.mkv", true)]
    [TestCase("Replay.MP4", true)]
    [TestCase("a.mov", true)]
    [TestCase("robocopy.log", false)]
    [TestCase("report.html", false)]
    [TestCase("nightly-summary.md", false)]
    [TestCase("runs.csv", false)]
    [TestCase("events.tsv", false)]
    [TestCase("noext", false)]
    public void 보관_삭제는_영상_파일만(string name, bool video)
    {
        Assert.AreEqual(video, QANightlyRetention.IsVideoFile(name));
    }

    // ── 할 일 120: 아침 요약 ─────────────────────────────────

    [Test]
    public void 아침_요약에_이벤트_시각표와_리플레이_클립()
    {
        var batch = new Dictionary<string, string> { { QANightlyHeader.KeySeedName, "eunhee" }, { "record", QANightlyLocalSettings.RecordReplay } };
        var events = new List<QANightlyEvent>
        {
            new QANightlyEvent { Run = 1, Retry = 0, Name = "stall", At = new DateTime(2026, 10, 27, 23, 10, 5), GameSeconds = 312.5, VideoSeconds = 312.9, Detail = "a|b" },
        };
        var clips = new List<QANightlyRecording> { new QANightlyRecording { Run = 1, Path = @"D:\obs\2026-10-27_eunhee_r1_stall+death.mkv", Note = "stall+death" } };
        string md = QANightlyHeader.Summary(batch, null, null, "done", "", events, clips);
        StringAssert.Contains("## 이벤트 시각표", md);
        StringAssert.Contains("| 1 | 0 | stall | 23:10:05 | 312.5 | 0:05:12 | a\\|b |", md);
        StringAssert.Contains("## 리플레이 클립", md);
        StringAssert.Contains(@"- 판 1 [stall+death]: D:\obs\2026-10-27_eunhee_r1_stall+death.mkv", md);

        string none = QANightlyHeader.Summary(new Dictionary<string, string> { { "record", QANightlyLocalSettings.RecordFull } }, null, null, "", "");
        StringAssert.Contains("| - | - | 없음 |", none);
        StringAssert.DoesNotContain("## 리플레이 클립", none, "판 전체 녹화 모드에 클립이 없으면 절을 만들지 않는다");
    }

    // ── 할 일 120 후속: 완료 표시·OBS 녹화 경로 ───────────────

    [TestCase("done", "done")]
    [TestCase("stopped", "aborted")]
    [TestCase("", "aborted")]
    [TestCase(null, "aborted")]
    [TestCase("running", "aborted")]
    public void 완료_상태는_done_아니면_aborted(string finish, string want)
    {
        Assert.AreEqual(want, QANightlyCompletion.EndOf(finish));
    }

    [Test]
    public void 완료_키는_progress_txt_로_왕복하고_기계_판독된다()
    {
        List<KeyValuePair<string, string>> kv = QANightlyCompletion.Format(QANightlyCompletion.Done, "10판 + 오류 판 재실행 끝", new DateTime(2026, 10, 28, 4, 12, 0),
            @"C:\UnityProject\QA\N108-20261027_2305-eunhee", 10, 10, 1, @"C:\Team-Vault\MainProject-Vault\QA_LogAndVideo\10-27", "eunhee", "report-eunhee-2.md");
        var progress = new Dictionary<string, string> { { "state", "done" }, { "run", "10" } };
        foreach (KeyValuePair<string, string> p in QABoss103Batch.ParseKeyValues(QABoss103Batch.FormatKeyValues(kv)))
            progress[p.Key] = p.Value;
        Assert.IsTrue(QANightlyCompletion.IsFinished(progress, out string end));
        Assert.AreEqual("done", end);
        Assert.AreEqual("C:/UnityProject/QA/N108-20261027_2305-eunhee", progress["batch_dir"]);
        Assert.AreEqual(@"C:\Team-Vault\MainProject-Vault\QA_LogAndVideo\10-27", progress["share_dir"]);
        Assert.AreEqual("10", progress["runs_done"]);
        Assert.AreEqual("10", progress["runs_total"]);
        Assert.AreEqual("1", progress["runs_error"]);
        Assert.AreEqual("report-eunhee-2.md", progress["report_name"]);
        Assert.AreEqual("2026-10-28 04:12:00", progress["end_at"]);
        foreach (KeyValuePair<string, string> p in kv)
            CollectionAssert.Contains(QANightlyCompletion.Keys, p.Key, "Resume 이 지우는 목록에 다 있어야 한다");

        Assert.IsFalse(QANightlyCompletion.IsFinished(new Dictionary<string, string> { { "state", "running" } }, out _), "끝나기 전엔 end 가 없다");
        Assert.IsFalse(QANightlyCompletion.IsFinished(null, out _));
        List<KeyValuePair<string, string>> ab = QANightlyCompletion.Format("weird", "에디터\n재시작", DateTime.Now, "", 3, 10, 0, "", "", "");
        Dictionary<string, string> abd = QABoss103Batch.ParseKeyValues(QABoss103Batch.FormatKeyValues(ab));
        Assert.IsTrue(QANightlyCompletion.IsFinished(abd, out string end2));
        Assert.AreEqual("aborted", end2);
        Assert.AreEqual("에디터 재시작", abd["end_reason"]);
    }

    [Test]
    public void OBS_녹화_경로_요청과_응답()
    {
        Assert.AreEqual("{\"recordDirectory\":\"C:\\\\Team-Vault\\\\QA_LogAndVideo\\\\10-27\"}",
            QAObsProtocol.RecordDirectoryData(@"C:\Team-Vault\QA_LogAndVideo\10-27"));
        const string json = "{\"op\":7,\"d\":{\"requestType\":\"GetRecordDirectory\",\"requestId\":\"g1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                            "\"responseData\":{\"recordDirectory\":\"D:\\\\Videos\"}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(json, "g1", out QAObsResult r));
        Assert.IsTrue(r.Ok);
        Assert.AreEqual(@"D:\Videos", r.RecordDirectory);
    }

    [Test]
    public void OBS_리플레이_버퍼_상태_응답의_active()
    {
        const string on = "{\"op\":7,\"d\":{\"requestType\":\"GetReplayBufferStatus\",\"requestId\":\"s1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                          "\"responseData\":{\"outputActive\":true}}}";
        const string off = "{\"op\":7,\"d\":{\"requestType\":\"GetReplayBufferStatus\",\"requestId\":\"s1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                           "\"responseData\":{\"outputActive\":false}}}";
        Assert.IsTrue(QAObsProtocol.TryParseResponse(on, "s1", out QAObsResult a));
        Assert.IsTrue(a.OutputActive);
        Assert.IsTrue(QAObsProtocol.TryParseResponse(off, "s1", out QAObsResult b));
        Assert.IsTrue(b.Ok, "요청은 성공이어도");
        Assert.IsFalse(b.OutputActive, "실제로 안 켜졌으면 false(설정 창 열림)");
    }
}
#endif
