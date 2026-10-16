# Factory assets

The photographic assets of BRIDGE's factory environment come from
[Poly Haven](https://polyhaven.com) and are released under
[CC0 1.0](https://polyhaven.com/license) (public domain; no attribution is
required, and it is given here anyway). Everything else in the factory (the hall,
the machines, the racking, the conveyor, signs, markings) is generated in code by
`frontend/src/viewport/factory/`.

| File | Source | What was done to it |
| --- | --- | --- |
| `factory_1k.hdr` | [industrial_workshop_foundry](https://polyhaven.com/a/industrial_workshop_foundry) | Downscaled from 2K to 1024 x 512; used for light and reflections only, never shown as a backdrop |
| `textures/floor_*.webp` | [painted_concrete_02](https://polyhaven.com/a/painted_concrete_02) | Colour, OpenGL normal and roughness at 2K, re-encoded as WebP |
| `textures/cladding_*.webp` | [box_profile_metal_sheet](https://polyhaven.com/a/box_profile_metal_sheet) | Normal and roughness at 1K, re-encoded as WebP; the colour is the wall paint |
| `textures/panel_*.webp` | [blue_metal_plate](https://polyhaven.com/a/blue_metal_plate) | Colour, normal and roughness at 1K, re-encoded as WebP |
| `models/cardboard_box.glb` | [cardboard_box_01](https://polyhaven.com/a/cardboard_box_01) | 1K glTF packed into one GLB, textures as WebP |
| `models/plastic_crate.glb` | [plastic_crate_02](https://polyhaven.com/a/plastic_crate_02) | As above |
| `models/drum.glb` | [Barrel_02](https://polyhaven.com/a/Barrel_02) | As above |
| `models/tool_chest.glb` | [metal_tool_chest](https://polyhaven.com/a/metal_tool_chest) | As above |
| `models/storage_cart.glb` | [industrial_storage_cart](https://polyhaven.com/a/industrial_storage_cart) | As above |
| `models/fire_extinguisher.glb` | [korean_fire_extinguisher_01](https://polyhaven.com/a/korean_fire_extinguisher_01) | As above, textures at 512 px |
| `models/floor_sign.glb` | [WetFloorSign_01](https://polyhaven.com/a/WetFloorSign_01) | As above, textures at 512 px |
