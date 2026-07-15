using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class ChargingObject : Unit
{
    [SerializeField] int maxHp;
    [SerializeField] int defense;
    [SerializeField] int maxshield;

    Collider _collider;

    float _maxY = 5f;
    float _minY = 0f;
    float _sign = 1f;
    bool _isMoving = false;
    bool _isReached = false;
    public bool IsReached { get { return _isReached; } }
    bool _isAlive = false;

    public event EventHandler DestroyEvent;
    public event EventHandler ReachEvent;

    void Awake()
    {
        _collider = GetComponent<Collider>();
        _minY = transform.position.y;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _collider.enabled = false;

        if (!IsServer) return;

        Initialize(0, 0, 0, maxHp, defense, maxshield);
    }

    public void SetMinMaxY(float max)
    {
        _maxY = max;
    }

    void Update()
    {
        if (!IsServer) return;

        CheckMoving();
        CheckHp();
    }
    public override void TakeDamage(AttackInfo attackInfo)
    { 
        if (!IsServer || !_isReached) return;

        base.TakeDamage(attackInfo);
    }

    void CheckHp()
    {
        if (!_isAlive) return;

        if (CurrentHealth <= 0)
        {
            EndCharge();
            DestroyEvent?.Invoke(this, EventArgs.Empty);
        }

    }

    void CheckMoving()
    {
        if (!_isMoving) return;

        Vector3 pos = transform.position;
        pos.y += _sign * Time.deltaTime;

        if (_sign > 0f)
        {
            if (pos.y >= _maxY)
            {
                pos.y = _maxY;
                _isMoving = false;
                _isReached = true;
                ReachEvent?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (_sign < 0f)
        {
            if (pos.y <= _minY)
            {
                pos.y = _minY;
                _isMoving = false;
            }
        }

        transform.position = pos;
    }

    public void StartCharge()
    {
        if (!IsServer) return;

        _isMoving = true;
        _isAlive = true;
        _isReached = false;
        Revive();
        _sign = 1f;

        _collider.enabled = true;
    }

    public void EndCharge()
    {
        if (!IsServer || !_isAlive) return;
        _isMoving = true;
        _isAlive = false;
        _isReached = false;
        _sign = -1f;

        _collider.enabled = false;
    }
}
