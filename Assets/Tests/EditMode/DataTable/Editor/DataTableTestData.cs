using System;
using UnityEngine;

// DataTableApplier 테스트 전용 SO. 에셋으로 만들지 않고 ScriptableObject.CreateInstance 로만 쓴다.
public sealed class DataTableTestData : ScriptableObject
{
    public enum Mode
    {
        Walk,
        Run,
    }

    [Serializable]
    public struct Charge
    {
        public float speed;
        public int damage;
    }

    [Tooltip("최대 체력")]
    public int maxHp = 100;
    [DataTableIgnore] public int bufferSize = 8;
    [DataTableIgnore] public Charge ignoredCharge;
    public float moveSpeed = 2.5f;
    public bool canDash = true;
    public string title = "base";
    public Mode mode = Mode.Walk;
    public byte smallCount = 1;
    public Charge charge = new Charge { speed = 3f, damage = 5 };
    public int[] phases = { 10, 20 };
    public GameObject prefab;

    [SerializeField] private float cooldown = 1f;
    public float Cooldown => cooldown;

    [Range(0f, 1f)] public float ratio = 0.5f;
    [Min(0)] public int count = 1;
    [Range(0f, 10f)] public float[] stages = { 1f, 2f };
}
