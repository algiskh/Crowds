# Weapon models (primitive style) & left-hand IK

Visual gun models for the player and mobs, built from Unity primitives in a flat-colour style
(wood / blued steel / black polymer…), plus a two-bone IK that keeps the left hand on the forend.
Pure view: no gameplay data lives here.

## Pieces

| What | Where | Role |
|---|---|---|
| `WeaponModel` | `_Scripts/WeaponModel.cs` | View component on a gun prefab: `Grip` (root, right hand), `Muzzle` (+Z = shot direction), `LeftHandTarget` (where the **left wrist bone** goes; empty = one-handed), `Magazines` (`GameObject[]`, hidden while reloading), `MuzzleEffectId`. `SetMagazineVisible(bool)`. |
| `MobWeapon : WeaponModel` | `_Scripts/MobWeapon.cs` | Empty subclass, kept for old references (same script guid / field names). `Mob.Weapon`, `RangedMobConfig.WeaponOverride`, `MobModelSetupPreset.WeaponPrefab` are now typed `WeaponModel`, so **any** gun prefab can go into a mob's hand. |
| `WeaponHandIK` | `_Scripts/Animation/WeaponHandIK.cs` | Two-bone IK (LeftArm → LeftForeArm → LeftHand, bones found by name) in `LateUpdate`, target = `Mob.Weapon.LeftHandTarget` (or an explicit weapon via `SetWeapon`). Works on Generic rigs. Animated elbow = bend hint. Off in `_disabledStates` (default `die`; checked on `_stateLayer` — player: `UpperBody` — and always on the base layer), blended by `_blendSpeed`. `_rotationWeight` (default 0) also aligns the wrist to the target rotation. |
| `GunConfig.Model` | `_Scripts/SO/GunConfig.cs` | Player gun model (optional). |
| `PlayerAnimator` | `_Scripts/Animation/PlayerAnimator.cs` | `SetWeapon(GunConfig)` (called by `PlayerAnimationSystem` instead of `SetWeaponSet`): instantiates `GunConfig.Model` once per config under `_weaponBone` with the shared pose `_weaponLocalPosition/_weaponLocalEuler`, compensates bone scale (`_weaponScale` = world scale), toggles models on switch; `SetReloading` hides the magazine for the whole reload. Optional `_leftHandIK` gets the current model. Legacy `_rifleModel/_pistolModel` are hidden while a per-gun model is shown. |
| `WeaponModelBuilder` | `_Scripts/Editor/Weapons/WeaponModelBuilder.cs` | **Tools ▸ Crowds ▸ Build Weapon Models**: creates materials (`_Materials/Weapons/Weapon_*.mat`, only if missing), (re)builds the prefabs in `_Prefabs/Weapons/` from the part specs in code and assigns `GunConfig._model`. |

## Convention

Root = right-hand grip point, barrel along **+Z**, **+Y** up, shooter's left = **−X**, metres.
Because every gun shares it, one hand pose fits all (`Mob.ReplaceWeapon` keeps the old pose,
`PlayerAnimator` uses one pose for all guns).

## Models (refs in `Assets/REFs`)

| Prefab | Ref | Config | Materials | Magazine (hidden on reload) | Left hand |
|---|---|---|---|---|---|
| `Mini14` (`MobWeapon`) | mini14.jpg (Ranch) | `RifleConfig` + Gunner zombie | wood stock, blued steel | short 5-rd box | under the forend |
| `AR` | ar.jpeg (M4, carry handle) | `ARConfig` | black steel/polymer | gray aluminium 30-rd | handguard |
| `SMG` | smg.jpg (M3 grease gun) | `SMGConfig` | parkerized gray | long stick mag | mag housing |
| `Pistol` | pistol.jpg (Beretta 92) | `PistolConfig` | black | — (inside the grip) | below-left of the grip (two-handed cup grip, as in the Mixamo pistol clips) |
| `Shotgun` | shotgun.png (Remington 870) | `ShotgunConfig` | polymer + blued | — (tube) | pump |
| `SawedOff` | sawedoff shotgun.png | `SawedOffConfig` | wood + blued | — (break action) | forend |
| `MachineGun` | machinegun m60.jpg (M60) | `MachineGunConfig` | polymer + blued, olive ammo box, brass belt | ammo box + belt | handguard |

`Mini14` **is** the former `MobRifle.prefab` (renamed by the builder with the same guid, so `Gunner.prefab` and
`Gunner_ModelSetup` keep the reference and the pose override; the root and its component survive a rebuild).

## Tuning

- Shape: edit the `Spec` chain in `WeaponModelBuilder.Specs()` and re-run the menu item. Don't hand-edit parts in the
  prefabs — the next build replaces all children. `Box(name, mat, center, size, pitch)` (pitch + = front down / bottom
  back), `Cyl(name, mat, center, diameter, length)` along Z, `Ball`. `magazine: true` puts the part under `Magazine`.
- Colours: edit `_Materials/Weapons/Weapon_*.mat` directly (the builder never overwrites existing materials).
- Left hand on the mob: move `LeftHand` in the spec (it is the wrist position, slightly behind/below the palm),
  or tweak `WeaponHandIK` on `Gunner.prefab` (`_disabledStates` e.g. add `aim`/`shoot` if the aim clip already holds
  the forend well, `_rotationWeight`).

## Player integration (when the body model exists)

On the player's `PlayerAnimator`: `_weaponBone` = `mixamorig:RightHand`, tune `_weaponLocalPosition/_weaponLocalEuler`
once in the Aim pose (all guns share it), optionally add `WeaponHandIK` on the body (no `Mob` → it uses the weapon
passed by `PlayerAnimator`) and reference it in `_leftHandIK`. Bullets still come from the root `Weapon.Muzzle`
(see `Docs/PlayerAnimation.md`), the model is visual only.

Muzzle flash: `WeaponFireSystem` requests `MuzzleEffectId` (default `muzzle_flash`, `Resources/FxHolder.asset`) at
`PlayerAnimator.CurrentGunModel.Muzzle`, parented to it; without a body/gun model — at the gameplay `Weapon.Muzzle`.

## Projectiles and the grenade (`ProjectileModelBuilder`)

**Tools ▸ Crowds ▸ Build Projectile Models** (`_Scripts/Editor/Weapons/ProjectileModelBuilder.cs`, sizes in code).
Prefabs are edited in place (guids kept): the root keeps `Bullet`/`Grenade`, `TrailRenderer` and its collider (world
radius preserved), the legacy root ball mesh is removed (root scale → 1), visuals are collider-less child primitives.

| Prefab | AmmoConfig | Shape | Diameter × length, m |
|---|---|---|---|
| `Bullet.prefab` | 556 (+ `MainHolder.BulletPrefab` fallback) | cylinder + hemispherical nose, nose = +Z | 0.07 × 0.20 |
| `Bullet9mm.prefab` (new, copy of Bullet) | 9mm | same | 0.07 × 0.14 |
| `Bullet762.prefab` (new) | 762 | same | 0.09 × 0.26 |
| `Shell.prefab` | 12g | sphere (pellet) | 0.12 |
| `Grenade.prefab` | `MainHolder._grenadePrefab` | M67: olive body + dark seam, neck, copper ring, aluminium fuze, olive spoon; no pin | body 0.24 |

Gun barrels are 14–22 mm, the old balls were 0.5 m (pellets 0.25 m). Trail head width = 0.9 × diameter, tapering to 0.
Bullets keep their old material and are tinted by `GunConfig.BulletColor` (`Bullet.SetColor` now tints every child
`MeshRenderer`); the grenade uses `Weapon_Olive/Aluminum/Brass/Polymer` (run Build Weapon Models first).
`762Piercer.prefab` is unreferenced and left as is.

### Tracers

`ProjectileModelBuilder` makes bullets and pellets tracers: head material `Assets/_Materials/Projectiles/Tracer_Head.mat`
(URP Unlit, HDR warm color → glows with the scene Bloom) and trail `Tracer_Trail.mat` (URP Particles/Unlit, additive
SrcAlpha/One, multiplies the TrailRenderer vertex colors). Trail gradient: warm hue, alpha 1 → 0.6 → 0; shadows off.
`GunConfig.BulletColor` (alpha > 0) tints both: the head via MaterialPropertyBlock × `Bullet._tintIntensity` (2.5, HDR),
the trail via its gradient hue — so the gunner's `ZombieGun` (red) shoots red tracers. The old trail used the opaque
`Lit.mat`, which ignores vertex colors: that's why the red tint never reached the trail before.
