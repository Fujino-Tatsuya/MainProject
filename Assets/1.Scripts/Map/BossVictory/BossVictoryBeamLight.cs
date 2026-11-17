using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 보스 처치 연출 거너 레이저 = 광원(할 일 301) — [이 피어] 표시 전용.
/// ① 씬 조명(태양·환경광) 어둡게 = 공유 배율 <see cref="SceneLightDim"/>(간파 암순응·23호 차징과 곱으로 겹침, 끝에 Release)
/// ② 화면 대비 올림 = 런타임 전역 Volume 하나(ColorAdjustments.contrast, 가중치 = 효과 비율)
/// ③ 레이저를 따라 포인트 라이트 N개(레이저 색). 전부 <see cref="Tick"/> 의 비율(0~1)로 올리고 내린다 — 0 이면 다 끔.
/// 글리치 흑백·호박색(BossVictoryGlitchRender)은 그대로 — 그 위에 겹친다. 정리 = <see cref="Release"/>(라이트·Volume 파괴, 배율 반납).
/// </summary>
public sealed class BossVictoryBeamLight
{
    const float VolumePriority = 125f;   // 처치 연출 볼륨(120) 위

    readonly List<Light> _lights = new List<Light>();
    GameObject _volumeGo;
    VolumeProfile _volumeProfile;
    Volume _volume;
    ColorAdjustments _color;
    bool _dimmed;

    public bool Active => _dimmed || _volumeGo != null || _lights.Count > 0;

    /// <param name="amount">효과 비율 0~1(발사부터 차오름 → 레이저 유지 끝부터 돌아옴).</param>
    public void Tick(BossVictoryGunnerQ q, float amount, Vector3 start, Vector3 end, Color color)
    {
        if (q == null || amount <= 0f)
        {
            Release();
            return;
        }

        // ① 씬 조명
        float dim = Mathf.Clamp01(q.lightSceneDim) * amount;
        if (dim > 0f)
        {
            SceneLightDim.Shared.SetSun(this, 1f - dim);
            SceneLightDim.Shared.SetAmbient(this, 1f - dim);
            _dimmed = true;
        }
        else if (_dimmed)
        {
            SceneLightDim.Shared.Release(this);
            _dimmed = false;
        }

        // ② 대비
        if (q.lightContrast > 0f)
        {
            EnsureVolume();
            _color.contrast.Override(Mathf.Clamp(q.lightContrast, 0f, 100f));
            _volume.weight = amount;
        }
        else DestroyVolume();

        // ③ 레이저 위 포인트 라이트
        int count = q.beamLightIntensity > 0f && q.beamLightRange > 0f ? Mathf.Clamp(q.beamLightCount, 0, 8) : 0;
        while (_lights.Count > count) DestroyLast();
        while (_lights.Count < count) _lights.Add(CreateLight(_lights.Count));
        for (int i = 0; i < count; i++)
        {
            Light l = _lights[i];
            if (l == null) continue;
            l.intensity = q.beamLightIntensity * amount;
            l.range = q.beamLightRange;
            l.color = color;
            l.transform.position = BossVictoryGunnerCharge.BeamLightPoint(start, end, i, count);
        }
    }

    public void Release()
    {
        if (_dimmed) SceneLightDim.Shared.Release(this);
        _dimmed = false;
        DestroyVolume();
        while (_lights.Count > 0) DestroyLast();
    }

    static Light CreateLight(int index)
    {
        var go = new GameObject($"BossVictoryBeamLight{index}") { hideFlags = HideFlags.DontSave };
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;
        return l;
    }

    void DestroyLast()
    {
        int last = _lights.Count - 1;
        if (_lights[last] != null) Object.Destroy(_lights[last].gameObject);
        _lights.RemoveAt(last);
    }

    void EnsureVolume()
    {
        if (_volumeGo != null) return;
        _volumeGo = new GameObject("BossVictoryBeamLightVolume") { hideFlags = HideFlags.DontSave };
        Camera cam = Camera.main;
        if (cam != null) _volumeGo.layer = cam.gameObject.layer;   // 카메라 볼륨 레이어 마스크 안(처치 연출 볼륨과 같은 방식)
        _volume = _volumeGo.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight = 0f;
        _volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volumeProfile.name = "BossVictory BeamLight Volume";
        _volumeProfile.hideFlags = HideFlags.DontSave;
        _volume.profile = _volumeProfile;
        _color = _volumeProfile.Add<ColorAdjustments>();
    }

    void DestroyVolume()
    {
        if (_volumeGo != null) CoreUtils.Destroy(_volumeGo);
        if (_volumeProfile != null) CoreUtils.Destroy(_volumeProfile);
        _volumeGo = null;
        _volumeProfile = null;
        _volume = null;
        _color = null;
    }
}
