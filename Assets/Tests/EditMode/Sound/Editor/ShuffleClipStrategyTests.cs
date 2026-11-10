using System.Collections.Generic;
using System.Reflection;
using Ami.BroAudio.Data;
using Ami.BroAudio.Runtime;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// PLAN-sound-spec-apply 2단계 — BroAudio Shuffle 이 "직전 파일 제외 랜덤"(사운드 명세 원칙 5)을 지키는지.
/// 패키지 원본은 직전 클립을 다시 고를 수 있었다(<c>ShuffleClipStrategy</c> 의 MainProject 수정 주석).
/// </summary>
public sealed class ShuffleClipStrategyTests
{
    private static readonly FieldInfo ClipField =
        typeof(BroAudioClip).GetField("AudioClip", BindingFlags.Instance | BindingFlags.NonPublic);

    // Addressables 가 켜진 프로젝트에선 IsSet 이 이 참조의 GUID 도 본다. 직렬화된 실제 에셋은 Unity 가 빈 참조로 채우지만
    // new 로 만든 클립은 null 이라 NRE — 테스트도 빈 GUID 참조를 넣어 실제 에셋과 같게 만든다.
    private static readonly FieldInfo AddressableField =
        typeof(BroAudioClip).GetField("AudioClipAssetReference", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<AudioClip> _created = new List<AudioClip>();

    [TearDown]
    public void TearDown()
    {
        foreach (AudioClip clip in _created)
            Object.DestroyImmediate(clip);
        _created.Clear();
    }

    private BroAudioClip[] MakeClips(params bool[] set)
    {
        var clips = new BroAudioClip[set.Length];
        for (int i = 0; i < set.Length; i++)
        {
            clips[i] = new BroAudioClip();
            if (AddressableField != null)
                AddressableField.SetValue(clips[i], System.Activator.CreateInstance(AddressableField.FieldType, string.Empty));
            if (!set[i]) continue;
            AudioClip audio = AudioClip.Create("shuffle_test_" + i, 16, 1, 44100, false);
            _created.Add(audio);
            ClipField.SetValue(clips[i], audio);
        }
        return clips;
    }

    private static bool[] AllSet(int count)
    {
        var set = new bool[count];
        for (int i = 0; i < count; i++) set[i] = true;
        return set;
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(12)]
    public void NeverRepeatsPreviousClip(int count)
    {
        BroAudioClip[] clips = MakeClips(AllSet(count));
        var strategy = new ShuffleClipStrategy();
        IBroAudioClip previous = null;

        for (int n = 0; n < 1000; n++)
        {
            IBroAudioClip picked = strategy.SelectClip(clips, default, out int index);
            Assert.That(picked, Is.Not.Null);
            Assert.That(picked, Is.SameAs(clips[index]), "반환 클립과 index 가 달라졌다");
            Assert.That(picked, Is.Not.SameAs(previous), $"{n}번째 선택이 직전과 같다");
            previous = picked;
        }
    }

    [Test]
    public void UsesEveryClipWithinOneRound()
    {
        BroAudioClip[] clips = MakeClips(AllSet(5));
        var strategy = new ShuffleClipStrategy();
        var seen = new HashSet<IBroAudioClip>();

        for (int n = 0; n < 5; n++)
            seen.Add(strategy.SelectClip(clips, default, out _));

        Assert.That(seen.Count, Is.EqualTo(5));
    }

    [Test]
    public void SingleValidClip_RepeatsIt()
    {
        BroAudioClip[] clips = MakeClips(false, true, false);
        var strategy = new ShuffleClipStrategy();

        for (int n = 0; n < 10; n++)
        {
            IBroAudioClip picked = strategy.SelectClip(clips, default, out int index);
            Assert.That(index, Is.EqualTo(1));
            Assert.That(picked, Is.SameAs(clips[1]));
        }
    }

    [Test]
    public void SkipsUnsetClips()
    {
        BroAudioClip[] clips = MakeClips(true, false, true, false);
        var strategy = new ShuffleClipStrategy();
        IBroAudioClip previous = null;

        for (int n = 0; n < 200; n++)
        {
            IBroAudioClip picked = strategy.SelectClip(clips, default, out int index);
            Assert.That(index == 0 || index == 2, Is.True, $"미설정 클립을 골랐다: {index}");
            Assert.That(picked, Is.Not.SameAs(previous));
            previous = picked;
        }
    }

    [Test]
    public void NoValidClip_ReturnsNull()
    {
        BroAudioClip[] clips = MakeClips(false, false);
        var strategy = new ShuffleClipStrategy();

        Assert.That(strategy.SelectClip(clips, default, out _), Is.Null);
    }
}
