using System;
using System.Collections.Generic;
using UnityEngine;
using Ami.BroAudio;

/// <summary>
/// 프로젝트에서 사용하는 모든 사운드를 문자열 키로 모아두는 데이터 허브.
/// 기존 FMOD의 FmodEvents(MonoBehaviour 싱글톤)를 대체하며,
/// 참조를 씬/프리팹 인스턴스가 아니라 프로젝트 에셋(SoundCatalog.asset)에 보관해
/// 씬 로드/프리팹 상태와 무관하게 참조가 유지되도록 한다.
///
/// 엔트리 구성(키·라이브러리·엔티티·클립·재생 모드)은 <c>GameData.xlsx</c>의 <c>Sound</c> 시트가 원본이고,
/// <c>Tools/Data/사운드 동기화</c>가 BroAudio 라이브러리·엔티티 생성과 SoundID 연결까지 디스크에 반영한다(PLAN-sound-sync.md).
/// </summary>
[CreateAssetMenu(fileName = "SoundCatalog", menuName = "Audio/Sound Catalog")]
[DataTableSheet("Sound")]
public class SoundCatalog : ScriptableObject
{
    [Header("Key Entries")]
    [Tooltip("문자열 키로 재생할 사운드와 키별 재생 수치. 구성은 Sound 시트 + 사운드 동기화로, 수치는 Sound 시트로 관리한다.")]
    [SerializeField] private SoundEntry[] entries = Array.Empty<SoundEntry>();

    public IReadOnlyList<SoundEntry> Entries => entries;
}

/// <summary>
/// 문자열 키 하나의 사운드 연결과 재생 수치.
/// <para>
/// 구성 칸(<c>library</c>~<c>loop</c>)과 <see cref="Sound"/>는 사운드 동기화가 쓴다 — 이 SO 에 남은 값은 마지막으로 동기화한 값이다.
/// 재생 수치는 <c>GameData.xlsx</c>의 <c>Sound</c> 시트가 테이블 모드 Play·빌드에서 덮어쓴다.
/// </para>
/// </summary>
[Serializable]
public sealed class SoundEntry
{
    [SerializeField, DataTableText(Localize = false)]
    [Tooltip("코드에서 AudioManager.Play에 넘기는 고유 키")]
    private string key = string.Empty;

    [SerializeField, DataTableText]
    [Tooltip("엔티티가 속할 BroAudio 라이브러리 이름. 없으면 사운드 동기화가 만든다")]
    private string library = string.Empty;

    [SerializeField, DataTableText]
    [Tooltip("연결할 BroAudio 엔티티 이름(프로젝트 전체에서 고유). 없으면 사운드 동기화가 만든다")]
    private string entity = string.Empty;

    [SerializeField, DataTableText]
    [Tooltip("엔티티 AudioType: Music·UI·Ambience·SFX·VoiceOver")]
    private string audioType = string.Empty;

    [SerializeField, DataTableText, TextArea]
    [Tooltip("엔티티 클립 경로(Assets/51.Audio/...). 여러 개면 셀 안 줄바꿈(Alt+Enter). - = 엔티티 클립 유지")]
    private string clipPaths = "-";

    [SerializeField, DataTableText]
    [Tooltip("엔티티 재생 모드: Single·Sequence·Random·Shuffle·Velocity·Chained. - = 유지")]
    private string playMode = "-";

    [SerializeField, Range(0, 2)]
    [Tooltip("0 = 반복 없음, 1 = Loop, 2 = SeamlessLoop(끊김 없는 반복)")]
    private int loop;

    [SerializeField, DataTableIgnore]
    [Tooltip("사운드 동기화가 entity 칸으로 연결하는 엔티티(xlsx 대상 아님)")]
    private SoundID sound;

    [SerializeField, Min(0f)]
    [Tooltip("BroAudio 엔티티 볼륨에 곱하는 배율(1 = 그대로)")]
    private float volumeMultiplier = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("재생마다 추가로 곱할 볼륨 랜덤 배율의 최솟값")]
    private float volumeRandomMin = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("재생마다 추가로 곱할 볼륨 랜덤 배율의 최댓값")]
    private float volumeRandomMax = 1f;

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

    [SerializeField, DataTableText(Localize = false), TextArea]
    [Tooltip("기획용 설명")]
    private string description = string.Empty;

    public string Key => key;
    public SoundID Sound => sound;
    public string Entity => entity;
    public float VolumeMultiplier => volumeMultiplier;
    public float VolumeRandomMin => volumeRandomMin;
    public float VolumeRandomMax => volumeRandomMax;
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
