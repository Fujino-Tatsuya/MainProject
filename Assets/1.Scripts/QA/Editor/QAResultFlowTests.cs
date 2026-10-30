#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 169 2차 — 로비 결과 패널 행 이름(PlayerRow_P{슬롯}) → 슬롯 번호. 패널은 순위 순으로 그리므로 행 순서가 아니라 이름으로 맞춘다.</summary>
public sealed class QAResultFlowTests
{
    [Test]
    public void 행_이름에서_슬롯_번호를_읽는다()
    {
        Assert.AreEqual(0, QAResultFlow.ParseRowIndex("PlayerRow_P1"));
        Assert.AreEqual(2, QAResultFlow.ParseRowIndex("PlayerRow_P3"));
        Assert.AreEqual(11, QAResultFlow.ParseRowIndex("PlayerRow_P12"));
    }

    [Test]
    public void 형식이_아니면_마이너스1()
    {
        Assert.AreEqual(-1, QAResultFlow.ParseRowIndex(null));
        Assert.AreEqual(-1, QAResultFlow.ParseRowIndex("PlayerRow_P0"));
        Assert.AreEqual(-1, QAResultFlow.ParseRowIndex("PlayerRow_Px"));
        Assert.AreEqual(-1, QAResultFlow.ParseRowIndex("PlayerRowsHeader"));
    }
}
#endif
