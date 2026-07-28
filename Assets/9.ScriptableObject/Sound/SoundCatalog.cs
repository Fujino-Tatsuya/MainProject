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
public class SoundCatalog : ScriptableObject
{
    [Header("Common")]
    [field: SerializeField] public SoundID UIClick { get; private set; }

    [Header("BGM")]
    [field: SerializeField] public SoundID LobbyBGM { get; private set; }
    [field: SerializeField] public SoundID InGameBGM { get; private set; }

    // [Header("Boss Scene (Wells / No.23)")]
    // [field: SerializeField] public SoundID BossBGM { get; private set; }
    // [field: SerializeField] public SoundID BossRoar { get; private set; }
}
