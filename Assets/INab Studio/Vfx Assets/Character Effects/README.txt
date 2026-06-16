Hi!
Thanks for purchasing Character Effects! Here are some useful links to help you get started:

Discord Support Server
https://discord.com/invite/K88zmyuZFD

Online Documentation
https://inabstudios.gitbook.io/character-effects/


If you have questions, feedback, or run into issues, reach out on Discord. 

##Offline Instructions:


#Requirements

- Unity 6.0 or newer.​
- HDRP or URP render pipeline.​
- URP only: Visual Effect Graph package installed.​


#Before Importing (required)

- Install required packages via Window → Package Manager → Unity Registry.​
	- URP: Install Visual Effect Graph.​


#Before Importing (optional, for demo scenes)

- Install Cinemachine via Package Manager
- Project Settings → Player → Other Settings → Active Input Handling = Input Manager (Old) or Both.​
- Window → TextMeshPro → Import TMP Essential Resources.​


#Setup by Pipeline

URP

- No extra steps; import the asset and it should work out of the box.​
- Rebuild graphs: Edit → VFX → Rebuild and Save All VFX Graphs.​

HDRP

- Locate HDRP.unitypackage at: INab Studio/Vfx Assets/Weapon FX Series/Weapon Aura FX.​
- Double‑click HDRP.unitypackage to import all files.​
- Rebuild graphs after import: Edit → VFX → Rebuild and Save All VFX Graphs.​


#Demo Scenes
Path: INab Studio/Vfx Assets/Character Effects/Demo Scenes


#Quick Start

A) Add the Component
Add CharacterEffect.cs to your weapon GameObject. Ensure it has a Skinned Mesh Renderer / Mesh Renderer.

B) Choose the Mesh Renderer
Click Find Renderer to auto-assign.
Note: Enable Read/Write in mesh import settings (Model Importer → Read/Write Enabled).

C) Load Effect Prefab
Click Load New and select an aura effect prefab. It attaches automatically.

D) Test the Effect
Use Testing buttons to Start/Stop the aura. It should now appear on your weapon.

E) Done!
Effect is ready. Control via C# API.

If you experiancing issues with the effects, check the online documentation and the FAQ & Known Issues page.
