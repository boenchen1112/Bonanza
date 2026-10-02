using System.Linq;
using UnityEditor;
using UnityEngine;

// Import settings for the batter exported by Tools/blender/export_fbx.py.
// Generic rig: the clips key Root squash/stretch, tail, ears, the wrist chain and the bat,
// which a Humanoid avatar would drop. Keyframe reduction is off so the snappy
// pose-to-pose timing (3-frame swing to contact) is kept exactly.
public class BatterImport : AssetPostprocessor
{
    const string ModelPath = "Assets/Art/Character/Batter.fbx";

    void OnPreprocessModel()
    {
        if (assetPath != ModelPath) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.animationCompression = ModelImporterAnimationCompression.Off;
        mi.resampleCurves = true;
    }

    void OnPreprocessAnimation()
    {
        if (assetPath != ModelPath) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            // Blender names takes "<armature>|<action>"
            var bar = c.name.LastIndexOf('|');
            if (bar >= 0) c.name = c.name.Substring(bar + 1);
            c.loopTime = c.name == "Idle";
            c.loopPose = false;
        }
        mi.clipAnimations = clips;
    }

    // Unity.exe -batchmode -quit -projectPath . -executeMethod BatterImport.Report
    public static void Report()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Debug.Log($"[BATTER] animationType={mi.animationType}");
        var assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
        foreach (var clip in assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            Debug.Log($"[BATTER] clip {clip.name} length={clip.length:F3}s fps={clip.frameRate} loop={clip.isLooping} curves={AnimationUtility.GetCurveBindings(clip).Length}");
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            Debug.Log($"[BATTER] mesh {smr.name} tris={smr.sharedMesh.triangles.Length / 3} bones={smr.bones.Length} bounds={smr.bounds.size}");
        var r = root.GetComponentsInChildren<Renderer>().Select(x => x.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        Debug.Log($"[BATTER] bounds size={r.size} center={r.center} rootRot={root.transform.GetChild(0).localEulerAngles}");
    }
}
