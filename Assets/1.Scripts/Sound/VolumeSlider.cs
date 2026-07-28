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

    [Header("동작")]
    [Tooltip("활성화 시 슬라이더 현재 값을 볼륨에 즉시 반영(패널 열 때 동기화)")]
    [SerializeField] private bool applyOnEnable = true;
    [Tooltip("PlayerPrefs로 볼륨을 저장/복원(옵션 메뉴 표준). 필요 없으면 해제")]
    [SerializeField] private bool persist = true;

    private Slider _slider;

    private string PrefsKey => isMaster ? "vol_master" : $"vol_{audioType}";

    private void Awake()
    {
        _slider = GetComponent<Slider>();
    }

    private void OnEnable()
    {
        _slider.onValueChanged.AddListener(Apply);

        if (persist && PlayerPrefs.HasKey(PrefsKey))
        {
            // 저장값을 슬라이더에 반영 → onValueChanged가 발생해 Apply까지 이어짐
            _slider.value = PlayerPrefs.GetFloat(PrefsKey);
        }
        else if (applyOnEnable)
        {
            Apply(_slider.value);
        }
    }

    private void OnDisable()
    {
        _slider.onValueChanged.RemoveListener(Apply);
    }

    /// <summary>슬라이더 값 변경 콜백. 볼륨 반영(+ 저장).</summary>
    private void Apply(float value)
    {
        if (AudioManager.Instance == null)
        {
            Edit.LogWarning("[VolumeSlider] AudioManager.Instance가 없습니다. 부트스트랩 씬에 AudioManager가 있는지 확인하세요.");
            return;
        }

        if (isMaster)
        {
            AudioManager.Instance.SetMasterVolume(value);
        }
        else
        {
            AudioManager.Instance.SetVolume(audioType, value);
        }

        if (persist)
        {
            PlayerPrefs.SetFloat(PrefsKey, value);
        }
    }
}
