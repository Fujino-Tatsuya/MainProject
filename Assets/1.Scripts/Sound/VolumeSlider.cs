using UnityEngine;
using UnityEngine.UI;
using Ami.BroAudio;

/// <summary>
/// UI Slider를 AudioManager의 볼륨 제어에 잇는 바인딩 컴포넌트.
/// 슬라이더를 드래그해 값이 바뀔 때마다 AudioManager.SetMasterVolume() 또는 SetVolume(type)을 호출한다.
/// 슬라이더마다 하나씩 붙여 인스펙터에서 대상(마스터/카테고리)을 지정한다.
///
/// 슬라이더 값 0~1 = 볼륨 0~1(1 = 원음). 부스트가 필요하면 Slider의 Max Value를 올린다.
/// </summary>
[RequireComponent(typeof(Slider))]
public class VolumeSlider : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("체크 시 마스터 볼륨(SetMasterVolume). 해제 시 아래 Audio Type으로 SetVolume 호출")]
    [SerializeField] private bool isMaster = false;
    [Tooltip("isMaster가 꺼져 있을 때 제어할 카테고리")]
    [SerializeField] private BroAudioType audioType = BroAudioType.Music;

    private Slider _slider;

    private BroAudioType Target => isMaster ? BroAudioType.All : audioType;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
    }

    // 🔴 패널을 열 때 슬라이더를 **저장값**으로 맞춘다(리스너 없이 — 같은 값을 다시 쓰지 않게).
    //    예전엔 AudioManager.Instance 를 null 확인 없이 읽어 부트스트랩 없는 Play 에서 NRE 가 났고,
    //    저장 코드가 주석 처리돼 있어 볼륨이 실행마다 1 로 돌아갔다(PLAN-settings 오디오).
    private void OnEnable()
    {
        _slider.SetValueWithoutNotify(SoundConfig.GetVolume(Target));
        _slider.onValueChanged.AddListener(Apply);
    }

    private void OnDisable()
    {
        _slider.onValueChanged.RemoveListener(Apply);
    }

    /// <summary>슬라이더 값 변경 콜백 — 저장하고 즉시 반영(AudioManager 가 없으면 저장만).</summary>
    private void Apply(float value) => SoundConfig.SetVolume(Target, value);
}
