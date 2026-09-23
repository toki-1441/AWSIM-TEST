> [!IMPORTANT]
> AWSIM has been transferred from [TIER IV](https://github.com/tier4) to [Autoware Foundation](https://github.com/autowarefoundation/) GitHub Organization at 2026-05-07.

> [!NOTE]
> AWSIM won the Grand Prize in the Innovation Award category of the Industry at the 17th Unity Awards. Click [here](https://unity.com/blog/17th-unity-awards-wrap-up-2025-winners-revealed) for more details.

![](/README_img/AWSIM-Autoware.png)

    
AWSIM is the best digital twin simulator for [Autoware](https://github.com/autowarefoundation/autoware).

## Linux batch build

Physical AI の起動・手動走行・RGB／深度／BEV の記録・データ確認・強化学習については、[日本語の利用マニュアル](docs/physical-ai-user-guide.md) を参照してください。

Build a local Linux 64-bit player with:

```bash
# --clean preserves Build/training and Build/evaluations artifacts.
scripts/build_linux.sh --clean
```

The script uses the Unity version pinned in `ProjectSettings/ProjectVersion.txt` and creates `Build/AWSIM.x86_64`. See [SKILLS.md](SKILLS.md) for options and workflow details.

## Features

- Optimally integrated simulation with Autoware
- Distribution of demo simulations highly optimized for Autoware
- ROS2 native communications and environments
- Use the same ROS2 topics and messages as the actual vehicle
- Vehicle dynamics optimized for Autoware
- Support for Autoware's vehicle control mode
- Support Logitech G29 steering wheels
- Ray tracing lidar sensor simulation
- Open CV camera sensor simulation
- IMU, GNSS sensor simulation
- Controllable time scale
- Support V2I development
- Random traffic simulation. Seed values can also be fixed
- point cloud generation
- Switchable between HDRP and URP
- Focus on customizability through coding
- Support ASAM OpenSCENARIO (connecting scenario simulator v2)
- Simple and highly expandable layered architecture
- Controlling the execution order of C# code independent of Unity's script execution order
- Unity version 6000.0.61f1

## Tutorial

First, try the tutorial !  
[AWSIM Document - Quick Start Demo](https://autowarefoundation.github.io/AWSIM/GettingStarted/QuickStartDemo/)

## Documentation

https://autowarefoundation.github.io/AWSIM/

## License

AWSIM License
Applies to `autowarefoundation/AWSIM` repositories and all content contained in the [Releases](https://github.com/autowarefoundation/AWSIM/releases).

- code : Apache 2.0
- assets : CC BY-NC
- docs : Prohibit public hosting of documents under `/docs`

See also [LICENSE](./LICENSE)

(c) 2025 TIER IV, inc
