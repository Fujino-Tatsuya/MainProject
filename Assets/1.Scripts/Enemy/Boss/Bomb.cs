using UnityEngine;
using System;

public enum CollidedWithBomb
{ 
    Wall,
    Player,
    Enemy,
    Ground
}

public class BombCollidedEventArgs : EventArgs
{
    public CollidedWithBomb _collidedWithBomb;
    public GameObject _gameObject;

    public BombCollidedEventArgs(CollidedWithBomb collidedWithBomb, GameObject gameObject = null)
    {
        _collidedWithBomb  = collidedWithBomb;
        _gameObject = gameObject;
    }
}
    
public class Bomb : MonoBehaviour
{
    [SerializeField] LayerMask Player;
    [SerializeField] LayerMask Enemy;
    [SerializeField] LayerMask Surface;
    [SerializeField] string WallTag;
    [SerializeField] string GroundTag;

    public EventHandler<BombCollidedEventArgs> OnCollided;

    void OnCollisionEnter(Collision collision)
    {
        int layer = collision.gameObject.layer;

        if ((Player & 1 << layer) != 0)
        {
            OnCollided?.Invoke(this, new BombCollidedEventArgs(CollidedWithBomb.Player, collision.transform.root.gameObject));
        }
        else if ((Enemy & 1 << layer) != 0)
        {
            OnCollided?.Invoke(this, new BombCollidedEventArgs(CollidedWithBomb.Enemy, collision.transform.root.gameObject));
        }
        else if ((Surface & 1 << layer) != 0)
        {
            string tag = collision.gameObject.tag;
            if(tag == WallTag)
                OnCollided?.Invoke(this, new BombCollidedEventArgs(CollidedWithBomb.Wall));

            else if (tag == GroundTag)
                OnCollided?.Invoke(this, new BombCollidedEventArgs(CollidedWithBomb.Ground));
        }
    }
}
