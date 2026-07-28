using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

/// <summary>
/// FMOD 재생·BGM·볼륨·파라미터의 중앙 창구(싱글톤).
/// "재생"과 "믹싱"만 담당하며, 씬↔뱅크 로드/언로드는 SceneAudioLoader가 맡는다.
/// 위치에 묶인 3D 사운드는 각 오브젝트가 EventInstance/컴포넌트로 직접 제어하고,
/// 여기서는 위치 무관 사운드(UI·BGM)와 전역 제어를 담당한다.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    private EventInstance _bgm;
    private bool _hasBgm;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Edit.LogWarning("[SoundManager] 중복 생성이 감지되어 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            StopBGM(fadeout: false);
    }

    #region 재생

    /// <summary>
    /// 지정한 FMOD 이벤트를 한 번 재생한다. (fire-and-forget, 인스턴스 관리 불필요)
    /// </summary>
    /// <param name="sound">재생할 FMOD 이벤트 참조</param>
    /// <param name="worldPos">3D 사운드 재생 위치. 생략 시 원점(0,0,0)</param>
    public void PlayOneShot(EventReference sound, Vector3 worldPos = default)
    {
        RuntimeManager.PlayOneShot(sound, worldPos);
    }

    /// <summary>
    /// 재생 중 제어(정지·파라미터·위치 추적)가 필요한 이벤트 인스턴스를 생성해 반환한다.
    /// 호출자가 다 쓴 뒤 반드시 stop() 후 release() 해야 한다.
    /// </summary>
    public EventInstance CreateInstance(EventReference sound)
    {
        return RuntimeManager.CreateInstance(sound);
    }

    #endregion

    #region BGM

    /// <summary>
    /// 기존 BGM을 정지하고 새 BGM을 재생한다. BGM 인스턴스는 SoundManager가 하나만 유지한다.
    /// </summary>
    public void PlayBGM(EventReference bgm)
    {
        StopBGM(fadeout: true);

        _bgm = RuntimeManager.CreateInstance(bgm);
        _bgm.start();
        _hasBgm = true;
    }

    /// <summary>현재 BGM을 정지하고 해제한다.</summary>
    public void StopBGM(bool fadeout = true)
    {
        if (!_hasBgm) return;

        _bgm.stop(fadeout ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);
        _bgm.release();
        _hasBgm = false;
    }

    /// <summary>현재 BGM의 로컬 파라미터 값을 변경한다. (예: 전투 강도 전환)</summary>
    public void SetBGMParameter(string paramName, float value)
    {
        if (_hasBgm)
            _bgm.setParameterByName(paramName, value);
    }

    #endregion

    #region 파라미터

    /// <summary>
    /// 이벤트의 파라미터 ID를 조회해 반환한다. (매 프레임 갱신하는 파라미터의 캐싱용)
    /// 주의: 해당 이벤트의 "뱅크가 로드된 뒤"에만 성공한다.
    /// </summary>
    /// <returns>조회 성공 시 true. 실패 시 id는 default.</returns>
    public bool TryGetParameterId(EventReference sound, string paramName, out PARAMETER_ID id)
    {
        id = default;

        EventDescription desc = RuntimeManager.GetEventDescription(sound);
        if (!desc.isValid())
        {
            Edit.LogWarning($"[SoundManager] EventDescription이 유효하지 않습니다. 뱅크가 로드되었는지 확인하세요. param={paramName}");
            return false;
        }

        FMOD.RESULT result = desc.getParameterDescriptionByName(paramName, out PARAMETER_DESCRIPTION pDesc);
        if (result != FMOD.RESULT.OK)
        {
            Edit.LogWarning($"[SoundManager] 파라미터 '{paramName}' 조회 실패: {result}");
            return false;
        }

        id = pDesc.id;
        return true;
    }

    /// <summary>
    /// 글로벌 파라미터를 이름으로 설정한다. (시스템 전체 모든 이벤트에 영향)
    /// 저빈도 변경(환경 전환 등)에 적합 — 가독성 우선.
    /// </summary>
    public void SetGlobalParameter(string paramName, float value)
    {
        RuntimeManager.StudioSystem.setParameterByName(paramName, value);
    }

    /// <summary>
    /// 글로벌 파라미터를 ID로 설정한다. (매 프레임 등 고빈도 갱신용 — 문자열 룩업 회피)
    /// 미리 TryGetGlobalParameterId로 얻은 id를 캐싱해 사용한다.
    /// </summary>
    public void SetGlobalParameter(PARAMETER_ID id, float value)
    {
        RuntimeManager.StudioSystem.setParameterByID(id, value);
    }

    /// <summary>
    /// 글로벌 파라미터 ID를 조회해 반환한다. (고빈도 갱신 파라미터의 캐싱용)
    /// 주의: 해당 파라미터가 포함된 뱅크/Strings가 로드된 뒤에만 성공한다.
    /// </summary>
    /// <returns>조회 성공 시 true. 실패 시 id는 default.</returns>
    public bool TryGetGlobalParameterId(string paramName, out PARAMETER_ID id)
    {
        id = default;

        FMOD.RESULT result = RuntimeManager.StudioSystem.getParameterDescriptionByName(paramName, out PARAMETER_DESCRIPTION pDesc);
        if (result != FMOD.RESULT.OK)
        {
            Edit.LogWarning($"[SoundManager] 글로벌 파라미터 '{paramName}' 조회 실패: {result}");
            return false;
        }

        id = pDesc.id;
        return true;
    }

    #endregion

    #region 볼륨 / 전역 제어

    /// <summary>VCA(볼륨 그룹) 볼륨을 설정한다. path 예: "vca:/BGM", "vca:/SFX"</summary>
    public void SetVolume(string vcaPath, float volume)
    {
        VCA vca = RuntimeManager.GetVCA(vcaPath);
        if (vca.isValid())
            vca.setVolume(Mathf.Clamp01(volume));
        else
            Edit.LogWarning($"[SoundManager] VCA를 찾을 수 없습니다: {vcaPath}");
    }

    /// <summary>지정한 Bus를 일시정지/해제한다. path 예: "bus:/"(마스터)</summary>
    public void SetBusPaused(string busPath, bool paused)
    {
        Bus bus = RuntimeManager.GetBus(busPath);
        if (bus.isValid())
            bus.setPaused(paused);
        else
            Edit.LogWarning($"[SoundManager] Bus를 찾을 수 없습니다: {busPath}");
    }

    #endregion
}
