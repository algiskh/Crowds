# See-through occluders (hole around the player)

When the player is behind something (a car, crate, house or wall), the occluder gets a soft round **hole** around
the player, so you can always see yourself. The surface stays opaque: there is no transparency sorting and no
extra pass. The rim is dithered (interleaved gradient noise), which reads as a soft edge.

| Part | File | Role |
|---|---|---|
| Shader | `Assets/Shader/CrowdsLitSeeThrough.shader` (`Crowds/Lit See-Through`) | A copy of URP 17.3 `Lit`. ForwardLit, GBuffer, DepthOnly and DepthNormals call `CrowdsSeeThroughClip(positionCS)` before the stock fragment. ShadowCaster is untouched, so cut parts still cast shadows. It has the same properties and inspector as Lit, so swapping the shader keeps every setting. Generated from the package `Lit.shader`. When URP is updated, regenerate it the same way: rename the shader, swap 4 `#pragma fragment` lines for wrappers, and include the hlsl. |
| Clip function | `Assets/Shader/CrowdsSeeThrough.hlsl` | Cuts a fragment only if it is **inside the circle** around the player's screen position, **closer** to the camera than the player (eye depth − margin, with a fade), and **above the player's feet** (world Y reconstructed from depth, with a fade). The floor in front of the player therefore survives, so floors can share the shader. Strength 0 makes it plain Lit (menus, edit mode). |
| Globals | `Assets/_Scripts/Utils/SeeThroughShader.cs` | Shader name, property ids (`_CrowdsSeeThrough`, `_CrowdsSeeThroughParams`, `_CrowdsSeeThroughHeight`), `FindLit()` (falls back to URP Lit) and `Disable()`. |
| System | `Assets/_Scripts/ECS/SeeThroughSystem.cs` | Registered **last**, after the final `CameraShakeSystem`. Each frame it projects the player's chest (`WorldToViewportPoint`) and sets 3 global vectors. On destroy (scene change) it calls `Disable()`. |
| Settings | `PlayerConfig.SeeThrough` (`SeeThroughSettings`) | Enabled, Radius (share of screen height), EdgeSoftness, FocusHeight, DepthMargin/DepthFade, HeightOffset/HeightFade, FadeSpeed. |

**Which materials.** These use the see-through shader: every environment material in `_Materials/Level`, `_Materials/Suburb` and `_Materials/Vehicles`, plus `Crate`, `Floor` and `Ground01` (the ProBuilder walls of `LevelSwarms`). New materials made by `SuburbBuilder.SMat`, `VehicleBuilder.VMat` and `KeyLevelBuilder.Mat` get it too.

Not converted:

- characters, because a mob in front of the player would get a hole: `Model/*`, `GunnerBody`, `Headwear`, `Npc`;
- weapons: `Weapons/*`, `Lit.mat` (on `762Piercer`);
- `GrenadeTrace`;
- the unlit `Level_TunnelBlack`.

**Cost.** 3 `SetGlobalVector` calls per frame, plus about 20 ALU per fragment on these materials. That includes one matrix multiply for the world-Y reconstruction, and the early-outs skip pixels outside the circle.

**Limits.**

- The Scene view uses the same globals during Play, so it shows the hole at the Game camera's player position.
- The pause blur snapshot includes the hole.
- Mobs standing in front of the player are not cut.
