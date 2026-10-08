using System;
using System.Collections.Generic;
using UnityEngine;
using Ami.BroAudio;

/// <summary>
/// 프로젝트에서 사용하는 모든 사운드(SoundID)를 한 곳에 모아두는 데이터 허브.
/// 기존 FMOD의 FmodEvents(MonoBehaviour 싱글톤)를 대체하며,
/// 참조를 씬/프리팹 인스턴스가 아니라 프로젝트 에셋(SoundCatalog.asset)에 보관해
/// 씬 로드/프리팹 상태와 무관하게 참조가 유지되도록 한다.
///
/// SoundID는 BroAudio Library Manager에서 만든 엔티티를 인스펙터 드롭다운에서 선택해 연결한다.
/// 씬/기능이 늘어나면 [Header]로 구획을 나눠 정리한다.
/// </summary>
[CreateAssetMenu(fileName = "SoundCatalog", menuName = "Audio/Sound Catalog")]
[DataTableSheet("Sound")]
public class SoundCatalog : ScriptableObject
{
    [Header("Common")]
    [field: SerializeField, DataTableIgnore] public SoundID UIClick { get; private set; }

    [Header("BGM")]
    [field: SerializeField, DataTableIgnore] public SoundID TitleBGM { get; private set; }
    [field: SerializeField, DataTableIgnore] public SoundID LobbyBGM { get; private set; }
    [field: SerializeField, DataTableIgnore] public SoundID InGameBGM { get; private set; }

    [Header("Key Entries")]
    [Tooltip("문자열 키로 재생할 사운드와 키별 재생 수치. 배열 크기와 SoundID는 인스펙터에서, 나머지는 Sound 시트에서 관리한다.")]
    [SerializeField] private SoundEntry[] entries = Array.Empty<SoundEntry>();

    public IReadOnlyList<SoundEntry> Entries => entries;

    // [Header("Boss Scene (Wells / No.23)")]
    // [field: SerializeField] public SoundID BossBGM { get; private set; }
    // [field: SerializeField] public SoundID BossRoar { get; private set; }
}

/// <summary>
/// 문자열 키 하나의 사운드 연결과 재생 수치.
/// <para>
/// <see cref="Sound"/>는 에셋 참조라 인스펙터가 소유하고, 나머지 기획 값은
/// <c>GameData.xlsx</c>의 <c>Sound</c> 시트가 덮어쓸 수 있다.
/// </para>
/// </summary>
[Serializable]
public sealed class SoundEntry
{
    [SerializeField, DataTableText]
    [Tooltip("코드에서 AudioManager.Play에 넘기는 고유 키")]
    private string key = string.Empty;

    [SerializeField, DataTableIgnore]
    [Tooltip("BroAudio Library Manager에서 연결하는 사운드 엔티티(xlsx 대상 아님)")]
    private SoundID sound;

    [SerializeField, Min(0f)]
    [Tooltip("BroAudio 엔티티 볼륨에 곱하는 배율(1 = 그대로)")]
    private float volumeMultiplier = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("BroAudio 엔티티 피치에 곱하는 기본 배율(1 = 그대로)")]
    private float pitchMultiplier = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("재생마다 추가로 곱할 피치 랜덤 배율의 최솟값")]
    private float pitchRandomMin = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("재생마다 추가로 곱할 피치 랜덤 배율의 최댓값")]
    private float pitchRandomMax = 1f;

    [SerializeField, Range(0, 1)]
    [Tooltip("0 = 2D, 1 = 3D. 데이터 테이블 숫자 필드로 노출하기 위한 플래그")]
    private int is3D;

    [SerializeField, Min(0f)]
    [Tooltip("3D 사운드 최소 감쇠 거리")]
    private float minDistance = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("3D 사운드 최대 감쇠 거리")]
    private float maxDistance = 500f;

    [SerializeField, Min(0)]
    [Tooltip("같은 키의 최대 동시 재생 수(0 = 제한 없음)")]
    private int maxSimultaneous;

    [SerializeField, Min(0f)]
    [Tooltip("같은 키를 다시 재생할 수 있을 때까지의 시간(초, 0 = 제한 없음)")]
    private float retriggerCooldown;

    [SerializeField, DataTableText, TextArea]
    [Tooltip("기획용 설명")]
    private string description = string.Empty;

    public string Key => key;
    public SoundID Sound => sound;
    public float VolumeMultiplier => volumeMultiplier;
    public float PitchMultiplier => pitchMultiplier;
    public float PitchRandomMin => pitchRandomMin;
    public float PitchRandomMax => pitchRandomMax;
    public bool Is3D => is3D != 0;
    public float MinDistance => minDistance;
    public float MaxDistance => maxDistance;
    public int MaxSimultaneous => maxSimultaneous;
    public float RetriggerCooldown => retriggerCooldown;
    public string Description => description;

    public SoundEntry()
    {
    }

    /// <summary>EditMode 순수 로직 테스트용. 런타임 데이터는 인스펙터/xlsx에서 만든다.</summary>
    public SoundEntry(string key)
    {
        this.key = key;
    }
}
