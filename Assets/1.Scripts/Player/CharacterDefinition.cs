using UnityEngine;

[CreateAssetMenu(menuName = "Characters/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    // ⚠️ 2026-09-21 정리 — 필드와 아래 CharacterId 프로퍼티 **둘 다 소비처 0**(Codex 4차 확인).
    //    에셋에 저장된 값도 없었다. 캐릭터 식별이 필요해지면 그때 되살릴 것.
    // [SerializeField] private string characterId;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private GameObject soulVisualPrefab;
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private DefaultAttackData defaultAttackData;

    [Header("Base Stats")]
    [SerializeField] private int attackDamage;
    [SerializeField] private float moveSpeed;
    [SerializeField] private float attackSpeed;
    [SerializeField] private int maxHp;
    [SerializeField] private int defense;

    // public string CharacterId => characterId;   // ↑ 같은 이유로 주석 처리
    public GameObject VisualPrefab => visualPrefab;
    public GameObject SoulVisualPrefab => soulVisualPrefab;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public DefaultAttackData DefaultAttackData => defaultAttackData;
    public int AttackDamage => attackDamage;
    public float MoveSpeed => moveSpeed;
    public float AttackSpeed => attackSpeed;
    public int MaxHp => maxHp;
    public int Defense => defense;
}
