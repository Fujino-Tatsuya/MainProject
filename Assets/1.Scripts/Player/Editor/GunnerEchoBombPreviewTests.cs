using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 잔류 포격 예비 구체(할 일 149 후속 3) — Q 적중 순간 예비 생성·같은 대상 1개, 서버 부착이 같은 구체를 이어받음(표시 시각 유지), 미채택 만료.
/// EditMode 에는 스폰된 유닛이 없어 구체 인스턴스는 안 생긴다 — 같은 구체를 이어받았는지는 표시 시각(ShowAt)이 예비 생성 시각 그대로인지로 본다.
/// </summary>
public sealed class GunnerEchoBombPreviewTests
{
    const float Delay = 0.2f;
    const float Timeout = 1.5f;
    const ulong TargetA = 11;
    const ulong TargetB = 12;

    static GunnerEchoBombVisuals Make() => new GunnerEchoBombVisuals(null, Delay, default, Timeout);

    [Test]
    public void Preview_SameTargetTwice_IsOne()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.AddPreview(TargetA, 1.15f); // 집속 연사 다음 발
        Assert.That(v.Count, Is.EqualTo(1));
        Assert.That(v.PreviewCount, Is.EqualTo(1));
    }

    [Test]
    public void Preview_TwoTargets_AreTwo()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.AddPreview(TargetB, 1f);
        Assert.That(v.PreviewCount, Is.EqualTo(2));
    }

    [Test]
    public void Attach_AdoptsPreview_KeepsShowAt()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.Add(7, TargetA, 1.4f);
        Assert.That(v.Count, Is.EqualTo(1), "새 구체를 만들지 않는다");
        Assert.That(v.PreviewCount, Is.EqualTo(0));
        Assert.That(v.TryGetShowAt(7, out float showAt), Is.True);
        Assert.That(showAt, Is.EqualTo(1f + Delay).Within(1e-5f), "표시 지연 기준 = 적중 순간");
        Assert.That(v.TryGetTargetId(7, out ulong target) && target == TargetA, Is.True);
    }

    [Test]
    public void Attach_WithoutPreview_ShowsFromReceive()
    {
        var v = Make();
        v.Add(7, TargetA, 2f);
        Assert.That(v.TryGetShowAt(7, out float showAt), Is.True);
        Assert.That(showAt, Is.EqualTo(2f + Delay).Within(1e-5f));
    }

    [Test]
    public void Attach_OtherTarget_DoesNotAdopt()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.Add(7, TargetB, 1.4f);
        Assert.That(v.Count, Is.EqualTo(2));
        Assert.That(v.PreviewCount, Is.EqualTo(1));
    }

    [Test]
    public void Preview_NotAdopted_ExpiresSilently()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.Tick(1f + Timeout - 0.01f);
        Assert.That(v.Count, Is.EqualTo(1), "만료 전");
        v.Tick(1f + Timeout);
        Assert.That(v.Count, Is.EqualTo(0), "채택 안 된 예비 구체는 지운다");
    }

    [Test]
    public void Preview_ReHit_ExtendsExpiry()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.AddPreview(TargetA, 1.3f);
        v.Tick(1f + Timeout + 0.1f);
        Assert.That(v.Count, Is.EqualTo(1), "마지막 적중부터 센다");
        v.Tick(1.3f + Timeout);
        Assert.That(v.Count, Is.EqualTo(0));
    }

    [Test]
    public void Adopted_IsNotExpired()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.Add(7, TargetA, 1.4f);
        v.Tick(10f);
        Assert.That(v.Count, Is.EqualTo(1), "이어받은 구체는 폭발·Clear 로만 빠진다");
        v.Remove(7, out _);
        Assert.That(v.Count, Is.EqualTo(0));
    }

    [Test]
    public void PreviousCastBomb_SameTarget_NewPreviewAndAdoptSeparately()
    {
        var v = Make();
        v.Add(1, TargetA, 0f);      // 이전 시전 폭탄
        v.AddPreview(TargetA, 1f);  // 새 시전 적중
        Assert.That(v.Count, Is.EqualTo(2));
        v.Add(2, TargetA, 1.4f);
        Assert.That(v.Count, Is.EqualTo(2));
        Assert.That(v.PreviewCount, Is.EqualTo(0));
        Assert.That(v.TryGetShowAt(2, out float showAt) && UnityEngine.Mathf.Approximately(showAt, 1f + Delay), Is.True);
    }

    [Test]
    public void Sync_KeepsPreviews()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.Sync(new List<GunnerEchoBombState>());
        Assert.That(v.PreviewCount, Is.EqualTo(1));
    }

    [Test]
    public void Clear_RemovesPreviews()
    {
        var v = Make();
        v.AddPreview(TargetA, 1f);
        v.Clear();
        Assert.That(v.Count, Is.EqualTo(0));
    }

    [Test]
    public void IsPreviewExpired_Boundary()
    {
        Assert.That(GunnerEchoBombVisuals.IsPreviewExpired(1f, 1.5f), Is.False);
        Assert.That(GunnerEchoBombVisuals.IsPreviewExpired(1.5f, 1.5f), Is.True);
    }
}
