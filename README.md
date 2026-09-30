# Icon Generator

English | [한국어](README_KO.md) | [日本語](README_JA.md)

> A Unity editor tool for creating icons of VRChat avatars and accessories.

## Requirements

- Unity 2022.3
- Modular Avatar (optional)

## Installation

Click "Add to VCC" on the [VPM listing](https://koz39.github.io/vpm-listing/), then add Icon Generator in VCC or ALCOM.

## How to Use

1. Open Tools > Icon Generator from the top menu.
2. Drag and drop an avatar or accessory into Sources.
3. Adjust the camera and icon settings while checking the preview.
4. Click Generate Icon.

## Features

- Generate combined and individual icons at once
- Choose what to capture by checking or selecting items in the source tree
- Move, rotate and zoom the camera with the mouse in the preview
- Per-item camera settings
- Framing based on visible parts, crop prevention, background color and outline
- File name templates and duplicate file handling
- Reflects Modular Avatar Shape Changer, Material Setter and Material Swap
- Automatically assigns saved icons to MA menu icons

## Updating from v1

- The menu has moved from Tools > 3D Obj to Icon to Tools > Icon Generator.
- Output folder, icon size, zoom, camera angle and language settings are carried over automatically on first launch.
- The default file name is now `{name}.png`, so icons made with v1 are not overwritten.
