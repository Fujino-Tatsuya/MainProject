using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [모든 피어] 메인 1~4구역 일반 무전 표시(PLAN-main-radio-zone1-4 §4.1) — 디렉터 복제 값 <see cref="MapSceneDirector.Radio"/>(id·시작 ServerTime)과
/// 무전 데이터(<see cref="MapSceneRadioData"/>)만으로 그린다. 4구역 소개와 같은 무전 캡슐(<see cref="MapSceneRadioCaptionDriver"/>).
/// · 플레이와 병행 — 조작·이동·HUD 를 건드리지 않는다.
/// · 2·4구역 소개 연출 중엔 숨기고, 줄이 시작된 뒤 소개가 시작됐으면 그 줄은 버린다(<see cref="MapSceneRadioRules.ShouldShow"/>).
/// 붙는 곳: 디렉터 OnNetworkSpawn(모든 피어, 씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneRadioPresentation : MonoBehaviour
{
    private MapSceneDirector _director;
    private MapSceneRadioCaptionDriver _caption;
    private readonly List<double> _introStarts = new List<double>(2);
    private bool _showing;

    private void Awake()
    {
        _director = GetComponent<MapSceneDirector>();
    }

    private void OnDestroy()
    {
        _caption?.Dispose();
    }

    private void Update()
    {
        MapSceneDirector.RadioState state = _director != null && _director.IsSpawned ? _director.Radio : default;
        string id = state.LineId.ToString();
        NetworkManager nm = NetworkManager.Singleton;
        if (string.IsNullOrEmpty(id) || nm == null)
        {
            Hide();
            return;
        }

        double now = nm.ServerTime.Time;
        MapSceneRadioLineTiming timing = MapSceneRadioData.Load().TimingOf(id);
        _caption ??= new MapSceneRadioCaptionDriver("MapSceneRadio");
        float total = _caption.TotalSeconds(id, timing.chunkSeconds, timing.gapSeconds);
        if (total < 0f)
        {
            Hide();   // 안내 UI·글꼴 전 — 다음 프레임에 다시
            return;
        }

        bool introRunning = CollectIntros(out _);
        if (!MapSceneRadioRules.ShouldShow(state.StartServerTime, total, now, introRunning, _introStarts))
        {
            Hide();
            return;
        }

        _showing = _caption.Show(id, (float)(now - state.StartServerTime), timing.chunkSeconds, timing.gapSeconds);
    }

    // 2·4구역 소개 — 지금 도는가 + 시작한 적 있는 소개의 시작 시각.
    private bool CollectIntros(out int count)
    {
        _introStarts.Clear();
        bool running = false;
        MapSceneDirector.Zone2IntroState z2 = _director.Zone2Intro;
        if (z2.Phase != MapSceneDirector.IntroPhase.None)
        {
            _introStarts.Add(z2.StartServerTime);
            running |= z2.Phase == MapSceneDirector.IntroPhase.Running;
        }
        MapSceneDirector.Zone4IntroState z4 = _director.Zone4Intro;
        if (z4.Phase != MapSceneDirector.IntroPhase.None)
        {
            _introStarts.Add(z4.StartServerTime);
            running |= z4.Phase == MapSceneDirector.IntroPhase.Running;
        }
        count = _introStarts.Count;
        return running;
    }

    private void Hide()
    {
        if (!_showing) return;
        _showing = false;
        _caption?.Hide();
    }
}
