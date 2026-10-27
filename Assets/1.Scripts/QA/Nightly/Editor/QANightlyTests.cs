#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

/// <summary>할 일 108 야간 QA 순수 로직 — 시드 파싱·103 설정 확장·날짜 폴더·리포트 머리말·녹화 기록·OBS 요청 메시지·PC 설정.</summary>
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
    [TestCase("{\"name\":\"x\",\"runs\":91}", "runs")]
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
    public void 날짜별_공유_폴더()
    {
        var at = new DateTime(2026, 10, 27, 23, 40, 0);
        Assert.AreEqual(Path.Combine(@"C:\Team-Vault\QA-Nightly", "2026-10-27", "eunhee"), QANightlyPaths.ShareDir(@" C:\Team-Vault\QA-Nightly ", at, "eunhee"));
        Assert.IsNull(QANightlyPaths.ShareDir("", at, "eunhee"));
        Assert.IsNull(QANightlyPaths.ShareDir("   ", at, "eunhee"));
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

    [Test]
    public void 판_시작_뒤_48_리포트만_고른다()
    {
        var start = new DateTime(2026, 10, 27, 23, 0, 0);
        Assert.IsTrue(QANightlyPaths.IsScenarioReportOf("48-20261027_230000", start));
        Assert.IsTrue(QANightlyPaths.IsScenarioReportOf("48-20261028_010203_2-B103", start));
        Assert.IsFalse(QANightlyPaths.IsScenarioReportOf("48-20261027_225500", start));
        Assert.IsFalse(QANightlyPaths.IsScenarioReportOf("88-20261027_235500", start));
        Assert.IsFalse(QANightlyPaths.IsScenarioReportOf("48-abc", start));
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
            "qa/Nightly-eunhee\n", "abc1234", "6000.3.16f1", true, @"C:\share\2026-10-27\eunhee");
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
        var recs = new List<QANightlyRecording> { new QANightlyRecording { Run = 2, Retry = 1, Path = @"D:\obs\a.mkv", Shared = @"\\s\v\a.mkv" } };
        var attempts = new List<QABoss103Attempt> { new QABoss103Attempt { Run = 1, OutcomeText = "Clear", DataSource = "table" } };
        string md = QANightlyHeader.Summary(batch, recs, attempts, "done", @"C:\QA\N108\report.html");
        StringAssert.Contains("| 시드 이름 | eunhee |", md);
        StringAssert.Contains("| GameData.xlsx SVN 리비전 | 528 |", md);
        StringAssert.Contains("qa/Nightly-eunhee / abc1234", md);
        StringAssert.Contains("| 1 | 0 | Clear | table |", md);
        StringAssert.Contains(@"판 2 재실행: D:\obs\a.mkv → \\s\v\a.mkv", md);
        StringAssert.Contains("(설정 없음 — 복사 안 함)", md);
    }

    [Test]
    public void 녹화_기록_한줄_왕복()
    {
        var r = new QANightlyRecording { Run = 3, Retry = 0, Path = "D:\\v\\x\ty.mkv", Shared = "", Note = "영상 경로를 못 받음" };
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
}
#endif
