using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 구도 프리셋 목록(할 일 244 후속 A — 디버그 랩 <see cref="DebugBossVictoryLab"/> 패널에서 저장·불러오기·삭제).
/// 한 항목 = <see cref="BossVictoryConfig"/> 의 카메라 구도 규칙 값 묶음. 불러오면 Config 에 덮어써 <b>다음 재생부터</b> 반영된다(구도는 처치 순간 서버가 고른다).
/// git 추적 에셋이라 팀과 공유된다(<c>Assets/1.Scripts/Dev/BossVictoryLab/BossVictoryCameraPresets.asset</c>). 런타임 게임 코드는 이 SO 를 읽지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Camera Presets", fileName = "BossVictoryCameraPresets")]
public sealed class BossVictoryCameraPresets : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string name = "새 프리셋";
        public float finalPitch = 24f;
        public float endPitch = 32f;
        public float fov = 34f;
        public float referenceAspect = 16f / 9f;
        public float minDistance = 4f;
        public float maxDistance = 22f;
        public float distanceStep = 0.5f;
        public float endDistanceScale = 1.25f;
        public float characterHeight = 1.8f;
        public float[] yawCandidates = { -25f, -35f, -15f, -45f, -55f, -5f, 15f, 30f };

        /// <summary>Config 의 지금 구도 값을 담는다.</summary>
        public static Entry Capture(string name, BossVictoryConfig cfg) => new Entry
        {
            name = name,
            finalPitch = cfg.finalPitch,
            endPitch = cfg.endPitch,
            fov = cfg.fov,
            referenceAspect = cfg.referenceAspect,
            minDistance = cfg.minDistance,
            maxDistance = cfg.maxDistance,
            distanceStep = cfg.distanceStep,
            endDistanceScale = cfg.endDistanceScale,
            characterHeight = cfg.characterHeight,
            yawCandidates = cfg.yawCandidates != null ? (float[])cfg.yawCandidates.Clone() : Array.Empty<float>(),
        };

        /// <summary>이 값들을 Config 에 덮어쓴다(다음 재생부터).</summary>
        public void ApplyTo(BossVictoryConfig cfg)
        {
            cfg.finalPitch = finalPitch;
            cfg.endPitch = endPitch;
            cfg.fov = fov;
            cfg.referenceAspect = referenceAspect;
            cfg.minDistance = minDistance;
            cfg.maxDistance = maxDistance;
            cfg.distanceStep = distanceStep;
            cfg.endDistanceScale = endDistanceScale;
            cfg.characterHeight = characterHeight;
            cfg.yawCandidates = yawCandidates != null ? (float[])yawCandidates.Clone() : Array.Empty<float>();
        }
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>같은 이름이 있으면 덮어쓰고, 없으면 끝에 더한다. 이름은 앞뒤 공백을 뺀다(빈 이름 = 무시, false).</summary>
    public bool Save(string name, BossVictoryConfig cfg)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || cfg == null) return false;
        Entry captured = Entry.Capture(name, cfg);
        int i = IndexOf(name);
        if (i >= 0) entries[i] = captured;
        else entries.Add(captured);
        return true;
    }

    public int IndexOf(string name)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && entries[i].name == name) return i;
        return -1;
    }
}
