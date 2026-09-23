using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전체 화면 CRT 꺼짐(레퍼런스 0:23~0:25). 프레임 끝에 화면을 캡처해 최상단 RawImage 에서 <c>Title/CRTOff</c> 로 재생한다.
/// 끝나면 <b>검은 화면을 유지한 채</b> 콜백 — 씬이 바뀔 때까지 덮개를 걷지 않는다(1프레임 재노출 방지).
/// </summary>
/// <remarks>
/// 공유 PC_Renderer 를 건드리지 않으려고 Renderer Feature 대신 캡처 방식(Codex 검토 09-23).
/// 🔴 캡처는 클릭 콜백에서 바로 하지 않고 <c>WaitForEndOfFrame</c> 뒤에 한다. 덮개 RawImage 는 캡처 전까지 꺼 둔다.
/// 🔴 세로 뒤집힘은 API 이름으로 단정하지 않는다 — <see cref="_flipY"/> 로 실측 보정.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitlePowerOff : MonoBehaviour
{
    [SerializeField] private RawImage _cover;
    [SerializeField] private Material _offMaterialSource;
    [SerializeField, Min(0.1f)] private float _duration = 1.1f;
    [Tooltip("캡처 텍스처가 위아래로 뒤집혀 들어온다(DX12 · 09-23 팀장 실측: 꺼질 때 화면이 180° 돌아가 보였다).")]
    [SerializeField] private bool _flipY = true;

    private static readonly int IdProgress = Shader.PropertyToID("_Progress");
    private static readonly int IdFlip = Shader.PropertyToID("_FlipY");
    private static readonly int IdTime = Shader.PropertyToID("_FxTime");

    private RenderTexture _capture;
    private Material _material;
    private bool _playing;

    public float Duration => _duration;

    private void Awake()
    {
        if (_cover != null) _cover.gameObject.SetActive(false);
    }

    public void Play(Action done)
    {
        if (_playing) return;
        _playing = true;
        StartCoroutine(Run(done));
    }

    private IEnumerator Run(Action done)
    {
        if (_cover == null || _offMaterialSource == null)
        {
            done?.Invoke();
            yield break;
        }

        yield return new WaitForEndOfFrame();

        _capture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "TitlePowerOffCapture" };
        _capture.Create();
        ScreenCapture.CaptureScreenshotIntoRenderTexture(_capture);

        _material = new Material(_offMaterialSource) { name = "TitleCRTOff (Instance)" };
        _material.SetFloat(IdFlip, _flipY ? 1f : 0f);
        _material.SetFloat(IdProgress, 0f);
        _cover.texture = _capture;
        _cover.material = _material;
        _cover.transform.SetAsLastSibling();
        _cover.gameObject.SetActive(true);

        float t = 0f;
        while (t < _duration)
        {
            t += Time.unscaledDeltaTime;
            _material.SetFloat(IdProgress, Mathf.Clamp01(t / _duration));
            _material.SetFloat(IdTime, t);
            yield return null;
        }

        _material.SetFloat(IdProgress, 1f);
        yield return null; // 완전 검정이 한 프레임 그려진 뒤 전환
        done?.Invoke();
    }

    private void OnDestroy()
    {
        if (_capture != null) { _capture.Release(); Destroy(_capture); }
        if (_material != null) Destroy(_material);
    }
}
