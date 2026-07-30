using UnityEngine;

// 생성된 존 클론에 부착 — 어떤 슬롯/프리팹 조합으로 스폰됐는지 보존.
// Save Placements 툴이 이 정보로 (SlotID, 원본 프리팹) → 슬롯의 위치·회전 저작에 사용한다(근접매칭 아님).
public class GeneratedZoneIdentity : MonoBehaviour
{
    public int SlotID;
    public GameObject SourcePrefab;   // 인스턴스가 아니라 카탈로그의 원본 프리팹 에셋
}
