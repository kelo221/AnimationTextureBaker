# Animation Texture Baker

Bake skeletal animation data into 2D textures using GPU compute shaders. These textures allow you to play back animations using a specialized vertex shader, enabling thousands of animated characters on screen with minimal CPU overhead.


## Features
- **GPU Baking**: Rapid animation sampling using Compute Shaders.
- **High Performance**: Play animations via Vertex Texture Fetch (VTF) for massive crowds.
- **Combined Baking**: Pack multiple animations into a single texture atlas.
- **Mesh Obfuscation**: Collapse meshes into bounding boxes to protect your assets.
- **SOLID/MVC Architecture**: Clean, modular code structure for easy extension.

## Installation 

Add package from git:

`https://github.com/kelo221/AnimationTextureBaker.git`

## How to Bake

1. **Setup**: Drag your FBX model into the scene.
2. **Components**:
   - Ensure the GameObject has an `Animation` or `Animator` component.
   - Add the `AnimationBaker` component. **Default shaders are auto-assigned** (`MeshInfoTextureGen` compute shader and `VAT_MotionVectors` play shader).
3. **Configuration**:
   - Press **Scan** to automatically detect clips, or add them manually to the `Clips` list.
   - For multi-clip projects, enable **Combine Textures** in the settings.
   - Enable **Bake Velocity** for motion blur and velocity smear effects.
4. **Bake**: Press the **Bake Textures** button.
5. **Output**: 
   - Baked assets will be created in the folder specified in the **Save To Folder** setting.
   - A material using the `VAT_MotionVectors` shader will be generated automatically.

### Shader Options

| Shader | Description |
|--------|-------------|
| **VAT_MotionVectors** (Default) | Full-featured HLSL shader with PBR lighting, motion vectors, and velocity smear support. Recommended for production. |
| **AnimationBaker_Combined** | ShaderGraph-based shader. Compatible with combined bakes and easier to customize visually. |

## Settings & Info 

- **Frame Rate**: Defines the sampling frequency (frames per second). The final frame count is always rounded up to the nearest power of two for GPU efficiency.
- **Combined Baking**: If enabled, all clips will be packed into a single texture atlas. A `ScriptableObject` containing frame timing data and an `AnimationFramePlayer` script will be automatically generated.
- **Play On Demand**: The `AnimationFramePlayer` component includes a `Play On Demand` toggle. 
    - When **Enabled**: Animations are controlled by the script timer, allowing you to restart animations instantly (e.g., via the inspector buttons or `Play(index)` API) and preventing unwanted global looping.
    - When **Disabled**: Animations loop globally based on the shader's internal clock (pure GPU-driven).
- **Collapse Mesh**: Collapses the mesh into a single bounding box during export. This protects your model geometry while preserving animation vertex data.
- **Namespace**: All logic is organized under the `Kelo.AnimationTextureBaker` namespace.

## Architecture & Extensibility

The tool has been refactored to follow **MVC** and **SOLID** principles:
- **Model**: `BakerSettings` handles configuration state.
- **View**: `AnimationBakerEditor` manages the Inspector UI and orchestration.
- **Controller**: `BakerEngine` contains the core logic for sampling and texture conversion.



