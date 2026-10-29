#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 140-3 — 야간 QA 테이블 싱크 사전 검사(순수) — 필드 없음 판별·목록·멈춤 문구·dev 포함 여부·요약 줄.</summary>
public sealed class QANightlyTableSyncTests
{
    private static KeyValuePair<string, string> E(string loc, string msg) => new KeyValuePair<string, string>(loc, msg);

    // ── 필드 없음 판별 ────────────────────────────────────────

    [Test]
    public void 필드_없음_오류는_시트_필드로()
    {
        Assert.AreEqual("Boss.parryRetreatTime", QANightlyTableSync.MissingField("GameData.xlsx › Boss!C5",
            "BossData 에 필드 'parryRetreatTime' 가 없다(직렬화 필드 이름과 같아야 한다)."));
        Assert.AreEqual("Boss.parryRetreatTime", QANightlyTableSync.MissingField("GameData.xlsx › Boss!C5",
            "'WellsBoss' 의 BossData/BossPhase 어디에도 필드 'parryRetreatTime' 가 없다."));
        Assert.AreEqual("Boss.phases[2].hp", QANightlyTableSync.MissingField("GameData.xlsx › Boss!D3",
            "BossData.phases[2].hp — 필드가 없거나 배열 범위를 벗어났다(테이블은 배열 크기를 바꾸지 않는다)."));
    }

    [Test]
    public void 다른_오류는_필드_없음이_아니다()
    {
        Assert.IsNull(QANightlyTableSync.MissingField("GameData.xlsx › Boss!C1", "필드 'hp' 가 두 번 나온다."));
        Assert.IsNull(QANightlyTableSync.MissingField("GameData.xlsx › Boss!C5", "빈 칸이다."));
        Assert.IsNull(QANightlyTableSync.MissingField("GameData.xlsx", null));
    }

    [Test]
    public void 위치에서_시트_이름()
    {
        Assert.AreEqual("Boss", QANightlyTableSync.SheetOf("GameData.xlsx › Boss!C5"));
        Assert.AreEqual("Traits_Paladin", QANightlyTableSync.SheetOf("GameData.xlsx › Traits_Paladin"));
        Assert.AreEqual("", QANightlyTableSync.SheetOf("GameData.xlsx"));
        Assert.AreEqual("", QANightlyTableSync.SheetOf(null));
    }

    [Test]
    public void 분류는_중복_제거_순서_유지_그밖의_오류_수()
    {
        var errors = new List<KeyValuePair<string, string>>
        {
            E("GameData.xlsx › Boss!C5", "BossData 에 필드 'parryRetreatTime' 가 없다(직렬화 필드 이름과 같아야 한다)."),
            E("GameData.xlsx › Boss!C6", "BossData 에 필드 'parryRetreatTime' 가 없다(직렬화 필드 이름과 같아야 한다)."),
            E("GameData.xlsx › Player!B3", "빈 칸이다."),
            E("GameData.xlsx › Player!D3", "PlayerDashData 에 필드 'window' 가 없다(직렬화 필드 이름과 같아야 한다)."),
            E("GameData.xlsx", "xlsx 를 읽지 못했다: x"),
        };
        List<string> missing = QANightlyTableSync.Classify(errors, out int other, out string first);
        CollectionAssert.AreEqual(new[] { "Boss.parryRetreatTime", "Player.window" }, missing);
        Assert.AreEqual(2, other);
        Assert.AreEqual("GameData.xlsx › Player!B3: 빈 칸이다.", first);
    }

    // ── 목록·멈춤 문구 ────────────────────────────────────────

    [Test]
    public void 목록은_상한까지_나머지는_외_N개()
    {
        Assert.AreEqual("", QANightlyTableSync.FormatList(new List<string>()));
        Assert.AreEqual("Boss.a, Boss.b", QANightlyTableSync.FormatList(new[] { "Boss.a", "Boss.b" }));
        Assert.AreEqual("A.1, A.2, A.3, A.4, A.5, … 외 2개",
            QANightlyTableSync.FormatList(new[] { "A.1", "A.2", "A.3", "A.4", "A.5", "A.6", "A.7" }));
    }

    [Test]
    public void 멈춤_문구_필드_없음()
    {
        Assert.AreEqual("테이블 싱크 불일치(필드 없음 1개: Boss.parryRetreatTime) — development 병합 또는 SVN 업데이트 필요",
            QANightlyTableSync.StopMessage(new[] { "Boss.parryRetreatTime" }, 0, null));
    }

    [Test]
    public void 멈춤_문구_필드_없음과_그밖의_오류()
    {
        string s = QANightlyTableSync.StopMessage(new[] { "Boss.a", "Boss.b" }, 3, "GameData.xlsx › P!B3: 빈 칸이다.");
        Assert.AreEqual("테이블 싱크 불일치(필드 없음 2개: Boss.a, Boss.b) — development 병합 또는 SVN 업데이트 필요 · " +
                        "그 밖의 테이블 오류 3개(첫 오류: GameData.xlsx › P!B3: 빈 칸이다.) — Tools/Data/Verify 로 확인", s);
    }

    [Test]
    public void 멈춤_문구_그밖의_오류만_또는_없음()
    {
        Assert.AreEqual("테이블 준비 오류 — 그 밖의 테이블 오류 1개(첫 오류: x y) — Tools/Data/Verify 로 확인, development 병합 또는 SVN 업데이트 필요",
            QANightlyTableSync.StopMessage(new List<string>(), 1, "x\ny"));
        Assert.IsNull(QANightlyTableSync.StopMessage(new List<string>(), 0, null));
        Assert.IsNull(QANightlyTableSync.StopMessage(null, 0, null));
    }

    [Test]
    public void 멈춤_판별()
    {
        Assert.IsFalse(QANightlyTableSync.IsStop(""));
        Assert.IsFalse(QANightlyTableSync.IsStop(null));
        Assert.IsFalse(QANightlyTableSync.IsStop(QANightlyTableSync.Ok));
        Assert.IsFalse(QANightlyTableSync.IsStop(QANightlyTableSync.NoXlsx));
        Assert.IsTrue(QANightlyTableSync.IsStop("테이블 싱크 불일치(필드 없음 1개: Boss.x) — development 병합 또는 SVN 업데이트 필요"));
    }

    // ── dev 포함 여부·VCS 값 ─────────────────────────────────

    [Test]
    public void dev_포함_종료코드()
    {
        Assert.AreEqual("yes", QANightlyTableSync.DevMerged(0));
        Assert.AreEqual("no", QANightlyTableSync.DevMerged(1));
        Assert.AreEqual("na", QANightlyTableSync.DevMerged(128));
        Assert.AreEqual("na", QANightlyTableSync.DevMerged(null));
    }

    [Test]
    public void dev_포함_한줄()
    {
        Assert.AreEqual("yes — origin/development f11f5d84 포함(로컬 ref, fetch 안 함)", QANightlyTableSync.DevLine("f11f5d84", "yes"));
        Assert.AreEqual("no — origin/development f11f5d84 미포함(로컬 ref, fetch 안 함) — development 병합 필요", QANightlyTableSync.DevLine("f11f5d84", "no"));
        Assert.AreEqual("na — 확인 실패(origin/development na)", QANightlyTableSync.DevLine("", ""));
    }

    [Test]
    public void 실패_출력은_na()
    {
        Assert.AreEqual("na", QANightlyTableSync.OrNa(null));
        Assert.AreEqual("na", QANightlyTableSync.OrNa("  "));
        Assert.AreEqual("na", QANightlyTableSync.OrNa("unknown"));
        Assert.AreEqual("555", QANightlyTableSync.OrNa(" 555\n"));
    }

    // ── 요약 줄 ──────────────────────────────────────────────

    [Test]
    public void 요약에_멈춤_경고와_VCS_줄()
    {
        string stop = QANightlyTableSync.StopMessage(new[] { "Boss.parryRetreatTime" }, 0, null);
        var batch = new Dictionary<string, string>
        {
            { QANightlyHeader.KeySeedName, "eunhee" }, { "xlsx_svn_rev", "549" },
            { QANightlyTableSync.KeyArtRev, "555" }, { QANightlyTableSync.KeyDevRef, "f11f5d84" }, { QANightlyTableSync.KeyDevMerged, "no" },
            { QANightlyTableSync.KeyTableSync, stop },
        };
        string md = QANightlyHeader.Summary(batch, null, null, "stopped", "");
        StringAssert.Contains("> ⚠ 배치 멈춤(Play 시도 안 함) — 테이블 싱크 불일치(필드 없음 1개: Boss.parryRetreatTime) — development 병합 또는 SVN 업데이트 필요", md);
        StringAssert.Contains("| Assets/50.Art SVN 리비전 | 555 |", md);
        StringAssert.Contains("| origin/development 포함 | no — origin/development f11f5d84 미포함", md);
        StringAssert.Contains("| 테이블 사전 검사 | 테이블 싱크 불일치(필드 없음 1개", md);
    }

    [Test]
    public void 요약_검사_OK면_경고_없음_빈키는_na()
    {
        var batch = new Dictionary<string, string> { { QANightlyHeader.KeySeedName, "eunhee" }, { QANightlyTableSync.KeyTableSync, "ok" } };
        string md = QANightlyHeader.Summary(batch, null, null, "done", "");
        StringAssert.DoesNotContain("배치 멈춤", md);
        StringAssert.Contains("| 테이블 사전 검사 | ok |", md);
        StringAssert.Contains("| Assets/50.Art SVN 리비전 | na |", md);
        StringAssert.Contains("| origin/development 포함 | na — 확인 실패(origin/development na) |", md);
    }
}
#endif
