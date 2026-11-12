using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필(할 일 267) — '캐릭터 → 프로필' 표 고르기·기본 폴백·정본 에셋 연결·복사.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryProfile).
/// </summary>
public sealed class BossVictoryProfileTests
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";

    static BossVictoryGlitchSettings.CharacterProfile Row(TutorialCharacter c, BossVictoryProfile p) =>
        new BossVictoryGlitchSettings.CharacterProfile { character = c, profile = p };

    [Test]
    public void 표에_있으면_그_프로필_없거나_비면_기본()
    {
        var def = ScriptableObject.CreateInstance<BossVictoryProfile>();
        var paladin = ScriptableObject.CreateInstance<BossVictoryProfile>();
        var gunner = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            var table = new List<BossVictoryGlitchSettings.CharacterProfile>
            {
                Row(TutorialCharacter.Paladin, paladin),
                Row(TutorialCharacter.Gunner, null),     // 빈 줄 = 기본
                Row(TutorialCharacter.Gunner, gunner),   // 빈 줄 뒤 같은 캐릭터 줄은 쓴다(비지 않은 첫 줄)
            };
            Assert.AreSame(paladin, BossVictoryProfileSelect.Select(table, TutorialCharacter.Paladin, def));
            Assert.AreSame(gunner, BossVictoryProfileSelect.Select(table, TutorialCharacter.Gunner, def));
            Assert.AreSame(def, BossVictoryProfileSelect.Select(table, TutorialCharacter.Assassin, def), "표에 없음 = 기본");
            Assert.AreSame(def, BossVictoryProfileSelect.Select(table, TutorialCharacter.None, def), "캐릭터 모름(처치자 없음) = 기본");
            Assert.AreSame(def, BossVictoryProfileSelect.Select(null, TutorialCharacter.Paladin, def));
            Assert.IsNull(BossVictoryProfileSelect.Select(table, TutorialCharacter.Assassin, null));
        }
        finally
        {
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(paladin);
            Object.DestroyImmediate(gunner);
        }
    }

    [Test]
    public void 같은_캐릭터_두_줄이면_앞_줄()
    {
        var a = ScriptableObject.CreateInstance<BossVictoryProfile>();
        var b = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            var table = new[] { Row(TutorialCharacter.Assassin, a), Row(TutorialCharacter.Assassin, b) };
            Assert.AreSame(a, BossVictoryProfileSelect.Select(table, TutorialCharacter.Assassin, null));
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void 설정_기본_프로필이_비면_코드_기본값_프로필()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            BossVictoryProfile p = s.ProfileFor(TutorialCharacter.Paladin);
            Assert.IsNotNull(p);
            Assert.IsNotNull(p.timeline);
            Assert.IsFalse(s.HasOwnProfile(TutorialCharacter.Paladin));
        }
        finally
        {
            Object.DestroyImmediate(s);
        }
    }

    [Test]
    public void 복사는_값을_통째로_덮고_목록을_공유하지_않는다()
    {
        var src = ScriptableObject.CreateInstance<BossVictoryProfile>();
        var dst = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            src.backDashDistance = 4f;
            src.finisherSlot = PlayerSkillSlot.Main;
            src.timeline.backDashStartTime = 0.42f;
            src.timeline.cameraCut.Add(BossVictoryClip.Make(1f, 1f));
            src.timeline.cameraCutShots.Add(BossVictoryCameraCuts.Cut.Default);
            dst.CopyFrom(src);
            Assert.AreEqual(4f, dst.backDashDistance);
            Assert.AreEqual(PlayerSkillSlot.Main, dst.finisherSlot);
            Assert.AreEqual(0.42f, dst.timeline.backDashStartTime, 1e-5f);
            Assert.AreEqual(1, dst.timeline.cameraCut.Count);
            Assert.AreNotSame(src.timeline, dst.timeline);
            dst.timeline.cameraCut.Clear();
            Assert.AreEqual(1, src.timeline.cameraCut.Count, "원본 그대로");
        }
        finally
        {
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
        }
    }

    [Test]
    public void 노드_복사_마커는_덮어쓰고_자르기도_따라간다()
    {
        var src = new BossVictoryTimeline { finisherStartTime = 1.2f, finisherTrimSeconds = 0.5f, bossSlowTime = 2f };
        var dst = BossVictoryTimeline.CreateDefault();
        dst.CopyMarkerFrom(src, BossVictoryMarker.FinisherStart);
        dst.CopyMarkerFrom(src, BossVictoryMarker.BossSlow);
        Assert.AreEqual(1.2f, dst.finisherStartTime, 1e-5f);
        Assert.AreEqual(0.5f, dst.finisherTrimSeconds, 1e-5f, "클립 노드 끝 자르기도");
        Assert.AreEqual(2f, dst.bossSlowTime, 1e-5f);
    }

    [Test]
    public void 노드_복사_클립은_추가_하나뿐인_트랙은_덮어쓰기()
    {
        var src = new BossVictoryTimeline();
        BossVictoryClip tear = BossVictoryClip.Make(1f, 0.2f, 0.1f, 0.1f);
        tear.MigrateFadesToKeys();
        src.tear.Add(tear);
        src.crtOff.Add(BossVictoryClip.Make(3f, 1f));
        src.cameraCut.Add(BossVictoryClip.Make(0.5f, 1f));
        BossVictoryCameraCuts.Cut shot = BossVictoryCameraCuts.Cut.Default;
        shot.hideWalls = !shot.hideWalls;
        src.cameraCutShots.Add(shot);

        var dst = BossVictoryTimeline.CreateDefault();
        int tears = dst.tear.Count;
        Assert.AreEqual(1, dst.crtOff.Count);
        dst.CopyClipFrom(src, BossVictoryTrack.Tear, 0);
        dst.CopyClipFrom(src, BossVictoryTrack.CrtOff, 0);
        int cut = dst.CopyClipFrom(src, BossVictoryTrack.CameraCut, 0);

        Assert.AreEqual(tears + 1, dst.tear.Count, "효과 클립 = 새로 추가");
        Assert.AreEqual(1f, dst.tear[tears].start, 1e-5f, "같은 시각");
        Assert.AreNotSame(src.tear[0].keys, dst.tear[tears].keys, "키 목록 공유 안 함");
        Assert.AreEqual(1, dst.crtOff.Count, "TV 꺼짐 = 덮어쓰기");
        Assert.AreEqual(3f, dst.crtOff[0].start, 1e-5f);
        Assert.AreEqual(dst.cameraCut.Count, dst.cameraCutShots.Count, "컷 값 개수 = 컷 클립 개수");
        Assert.AreEqual(shot.hideWalls, dst.CameraCutShot(cut).hideWalls, "컷 값도 복사");
    }

    [Test]
    public void 정본_설정은_세_캐릭터_프로필만_있고_기본은_코드_기본값()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        Assert.IsNotNull(s, SettingsPath);
        // 기본 프로필 에셋은 11-12 삭제 — 칸이 비어 표에 없는 캐릭터·처치자 없음은 코드 기본값(저장 안 되는 인스턴스).
        Assert.IsNull(s.defaultProfile, "기본 프로필 칸 = 비어 있음");
        Assert.IsNull(AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(BossVictoryProfileAuthoring.Folder + "/BossVictoryProfile_Default.asset"), "기본 프로필 에셋 없음");
        BossVictoryProfile code = s.ProfileFor(TutorialCharacter.None);
        Assert.IsNotNull(code, "처치자 없음 = 코드 기본값");
        Assert.IsFalse(EditorUtility.IsPersistent(code), "코드 기본값 = 에셋 아님");
        Assert.IsNotNull(code.timeline);
        Assert.IsFalse(s.HasOwnProfile(TutorialCharacter.None));
        foreach (TutorialCharacter c in new[] { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin })
        {
            Assert.IsTrue(s.HasOwnProfile(c), $"{c} 프로필 줄");
            Assert.IsTrue(EditorUtility.IsPersistent(s.ProfileFor(c)), $"{c} = 에셋");
            Assert.AreNotSame(code, s.ProfileFor(c), $"{c} 는 코드 기본값과 다른 에셋");
        }
    }

    [Test]
    public void 표가_비고_기본_칸도_비면_모든_캐릭터가_코드_기본값()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            BossVictoryProfile none = s.ProfileFor(TutorialCharacter.None);
            Assert.IsNotNull(none);
            foreach (TutorialCharacter c in new[] { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin })
                Assert.AreSame(none, s.ProfileFor(c), $"{c} = 같은 코드 기본값");
        }
        finally { Object.DestroyImmediate(s); }
    }

    // 처치자 손 검 번개·낙하 검 2배(269) — 팔라딘 프로필만 켬, 나머지는 안 씀.
    [Test]
    public void 팔라딘만_손_검_번개와_낙하_검_2배()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        Assert.IsNotNull(s, SettingsPath);
        BossVictoryProfile paladin = s.ProfileFor(TutorialCharacter.Paladin);
        Assert.IsTrue(paladin.handSwordLightning, "팔라딘 = 손 검 번개");
        Assert.AreEqual(1.25f, paladin.fallingSwordScale, 1e-5f, "팔라딘 = 낙하 검 1.25배");
        Assert.IsTrue(BossVictoryGlitchPresentation.UsesKillerSword(paladin));
        Assert.IsFalse(BossVictoryGlitchPresentation.UsesKillerSword(s.ProfileFor(TutorialCharacter.None)), "코드 기본값 = 안 씀");
        Assert.IsFalse(BossVictoryGlitchPresentation.UsesKillerSword(s.ProfileFor(TutorialCharacter.Gunner)), "거너 = 안 씀");
        Assert.IsFalse(BossVictoryGlitchPresentation.UsesKillerSword(s.ProfileFor(TutorialCharacter.Assassin)), "어쌔신 = 안 씀");
    }

    [Test]
    public void 검_연출_사용_판단()
    {
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            Assert.IsFalse(BossVictoryGlitchPresentation.UsesKillerSword(null));
            Assert.IsFalse(BossVictoryGlitchPresentation.UsesKillerSword(p), "코드 기본값 = 끔·배율 1");
            p.fallingSwordScale = 2f;
            Assert.IsTrue(BossVictoryGlitchPresentation.UsesKillerSword(p), "낙하 검 배율만");
            p.fallingSwordScale = 1f;
            p.handSwordLightning = true;
            Assert.IsTrue(BossVictoryGlitchPresentation.UsesKillerSword(p), "손 검 번개만");
            p.handSwordLightning = false;
            p.fallingSwordOffset = new Vector3(2f, 0f, 1f);
            Assert.IsTrue(BossVictoryGlitchPresentation.UsesKillerSword(p), "낙하 자리 오프셋만");
            p.fallingSwordOffset = Vector3.zero;
            p.fallingSwordLife = 1.5f;
            Assert.IsTrue(BossVictoryGlitchPresentation.UsesKillerSword(p), "낙하 검 수명만");
            p.fallingSwordLife = 0f;
            Assert.IsFalse(BossVictoryGlitchPresentation.UsesKillerSword(p), "다 기본 = 끔");
        }
        finally { Object.DestroyImmediate(p); }
    }
}
