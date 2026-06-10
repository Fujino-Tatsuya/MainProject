using UnityEngine;

public class BaseWeapon : MonoBehaviour
{
    [SerializeField] protected int _damage = 0;
    public int Damage { get { return _damage; } }

    [SerializeField] protected bool _isGroggyAttack = false;
    public bool IsGroggyAttack { get { return _isGroggyAttack; } }
}
