# PlasmaSoundAPI
A library for playing AudioClips in Plasma

# Usage

`EventInstance PlasmaSoundAPI.PlaySound2D(AudioClip audioclip)` - play 2D sound

`EventInstance PlasmaSoundAPI.PlaySound3D(AudioClip audioclip, Vector3 position)` - play 3D sound at given position

---
Dont forget to add 
```c#
[BepInDependency("Azim.PlasmaSoundAPI", BepInDependency.DependencyFlags.HardDependency)]
``` 
annotation to your mod to ensure that the library is loaded before your mod.

# Releases

### 1.0.1
* Renamed assembly file and namespace to fix import issues
* Fixed sound banks not loading

### 1.0.0
* Initial release