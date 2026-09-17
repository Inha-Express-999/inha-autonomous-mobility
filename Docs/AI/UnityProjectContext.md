# InhaExpress project context

- Unity 6000.3.21f1, Universal Render Pipeline 17.3.0; PC_RPAsset is active.
- Unity Pipeline 0.7.0-exp.1 is installed and its live MCP connection was verified against this project.
- Initial project contained only the URP SampleScene and tutorial scripts. No first-party gameplay architecture or tests existed.
- Input System 1.20.0 and AI Navigation 2.0.14 are installed, but this map does not use input, players, AI, networking, or runtime behaviours.
- Authoring scripts live in AgentScripts outside Assets and execute through Pipeline `run_script`, keeping UnityEditor dependencies out of runtime assemblies.
- Generated map: Assets/InhaCampus/InhaCampus.unity. Reusable geometry-only prefab: Assets/InhaCampus/InhaCampusMap.prefab.
- The original SampleScene and build-scene list were preserved. Open InhaCampus.unity to inspect or play the map.
- Asset creation and scene/prefab serialization were performed through the live Unity Editor, not handwritten YAML.
- Read InhaCampus.md for sources, model limitations and reproduction steps.
