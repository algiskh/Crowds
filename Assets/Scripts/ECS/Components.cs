using System.Collections.Generic;
using UnityEngine;

#region Singleton_entities
/// <summary>
/// Singleton component for holding main configuration data
/// </summary>
public struct  MainHolderComponent
{
	public MainHolder Value;
}

public struct SoundHolderComponent
{
	public SoundHolder Value;
}

public struct EffectsHolderComponent
{
	public EffectsHolder Value;
}

public struct DecalsHolderComponent
{
	public DecalsConfigHolder Value;
}

/// <summary>
/// Singleton for collection of spawnpoints for mobs
/// </summary>
public struct SpawnPointsComponent
{
	public Transform[] Value;
}

/// <summary>
/// Singleton for holding a pool of mobs
/// </summary>
public struct MobPoolComponent
{
	public List<Mob> Value;
	public Transform Parent;
}

public struct BulletPoolComponent
{
	public Stack<Bullet> Value;
	public Transform Parent;
}

public struct EffectPoolComponent
{
	public List<SceneEffect> Value;
	public Transform Parent;
}
#endregion

public struct  MobComponent
{
	public Mob Value;
	public MobConfig Config;
}

public struct MoveComponent
{
	public Transform Transform;
	public Vector3 Direction;
	public float Speed;
}

public struct MovePath
{
	public List<Vector3> Waypoints;
	public int CurrentIndex;
}

public struct PathRecalculation
{
	public float LastTime;
	public float Interval;
}

public struct HealthComponent
{
	public float CurrentHealth;
	public float MaxHealth;
}

public struct RequestDamageComponent
{
	public float Damage;
	public int TargetEntity;
}

public struct ColliderComponent
{
	public Collider Value;
	public CollisionType CollisionType;
}

/// <summary>
/// Singleton component for managing spawn requests
/// </summary>
public struct SpawnRequestComponent
{
	// Presets
	public float MinCoolDown;
	public float MaxCoolDown;
	// Runtime values
	public float LastSpawnTime;
	public float CurrentCoolDown;
	public bool IsBlocked;
}

public struct SpawnPoint
{
	public Transform Value;
}

public struct SpawnTimer
{
	public float LastSpawnTime;
}

public struct SpawnRequest
{
	public Mob Prefab;
	public Transform SpawnPoint;
}
#region Player

public struct PlayerComponent
{
	public Player Value;
	public PlayerState State;
}

public struct PlayerInputComponent
{
	public Vector3 Move;
	public Vector3 PreviousMove;
	public bool IsFiring;
}

public struct MuzzleComponent
{
	public Weapon Weapon;
	public GunConfig GunConfig;
	public float PrevFireTime;
	public bool IsFiring;
	public int Count;
}

public struct RequestFireComponent
{
}

public struct BulletComponent
{
	public Bullet Bullet;
	public float Damage;
	public float LifeTime;
	public float Radius;
	public BulletCheckType CheckType;
}

public struct BulletOverlapComponent
{
	public Collider[] colliders;
}
#endregion


public struct RequestLootSpawn
{
	public MobConfig.PossibleLoot[] PossibleLoots;
	public Vector3 Position;
}

public struct LootComponent
{
	public Loot Loot;
	public LootType LootType;
	public float Radius;
	public int Count;
}

public struct LootPoolComponent
{
	public Stack<Loot> Value;
	public Transform Parent;
}

public struct AmmoCounterComponent
{
	public AmmoCounter Value;
}

public struct RequestOpenWindowComponent
{
	public WindowType WindowType;
}

public struct FailWindowComponent
{
	public FailWindow Value;
}

public struct BorderComponent
{
	public Transform Transform;
	public bool IsPlayerNearBy;
}

public struct EffectComponent
{
	public SceneEffect Effect;
	public float LifeTime;
}

public struct RequestEffectComponent
{
	public string EffectId;
	public Vector3 Position;
}

public struct EndGameComponent
{

}

public struct FollowTarget
{
	public Transform Target;
	public float Threshold;
	public bool IsAcceleratable;
	public float AccelerationMultiplier;
	public float MaxAccelerationMultiplier;
}

public struct FollowerComponent
{
	public Transform Value;
}

public struct FollowerOffset
{
	public Vector3 Value;
}

public struct Looker
{
	public Transform Value;
	public bool FlatBillboard;
}

public struct LookerAtCamera
{
	public Transform Transform;
	public bool FlatBillboard;
}

public struct LookAtCursor
{
	public Transform Transform;
	public bool Mode3D;
}

public struct CameraComponent
{
	public Camera Value;
}

public struct DisposableComponent
{
	public bool IsDisposed;
}

public struct LifeTimeComponent
{
	public float Value;
}

#region Decals
public struct DecalPoolComponent
{
	public List<Decal> Value;
	public Transform Parent;
}

public struct DecalComponent
{
	public Decal Value;
	public float Lifetime;
	public bool IsDisposed;
}

public struct RequestDecalComponent
{
	public string Id;
	public Vector3 Direction;
	public Vector3 Position;
}
#endregion