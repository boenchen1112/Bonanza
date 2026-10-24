using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Bonanza.Shell;

// The cast: Assets/Art/Cast/cast-<id>.fbx (Mixamo-rigged bodies from the Blender pipeline, 52 bones) -> one prefab each in
// Resources/Cast with house-toon materials (colours from the staged cast-<id>.json) and the idle clip. Run:
//   Tools/unity-batch.sh exec CastSetup.Run
public sealed class CastImport : AssetPostprocessor
{
    public const string Dir = "Assets/Art/Cast";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Dir + "/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = false;      // the face keys are not used on the roster; a third of the vertex data
        mi.meshCompression = ModelImporterMeshCompression.Medium;
        mi.optimizeMeshPolygons = true; mi.optimizeMeshVertices = true;
        mi.animationCompression = ModelImporterAnimationCompression.Optimal;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(Dir + "/")) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            int bar = c.name.LastIndexOf('|');
            if (bar >= 0) c.name = c.name.Substring(bar + 1);
            c.loopTime = c.name == "idle";
        }
        mi.clipAnimations = clips;
    }
}

public static class CastSetup
{
    static readonly string[] Ids = { "bopp", "zizz", "kwark", "tuff", "mimo", "nibb", "glub", "fizz" };
    const string Out = "Assets/Game/Resources/Cast";

    public static void Run()
    {
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Out + "/Materials");
        var toon = Resources.Load<Material>("Look/Toon");
        foreach (var id in Ids)
        {
            string fbx = $"{CastImport.Dir}/cast-{id}.fbx";
            AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            Debug.Log($"[CAST] {id} clips: " + string.Join(", ", clips.Select(c => $"{c.name}={c.length:F2}s")));
            var idle = clips.FirstOrDefault(c => c.name == "idle");

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            var mats = smr.sharedMaterials;
            var names = mats.Select(m => m ? m.name : "").ToArray();
            var newMats = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = new Material(toon);
                var c = Pick(File.ReadAllText($"Art/cast-staging/fbx/cast-{id}.json"), names[i]);
                m.SetColor("_BaseColor", c);
                bool eye = names[i] == "eye";
                m.SetFloat("_Rim", eye ? 0.2f : 0.9f);
                m.SetFloat("_Softness", 0.3f);
                string path = $"{Out}/Materials/cast-{id}-{names[i]}.mat";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                newMats[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            smr.sharedMaterials = newMats;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.updateWhenOffscreen = true;
            var cm = inst.AddComponent<CastModel>();
            cm.Idle = idle; cm.Body = smr;
            var b = smr.bounds;
            cm.Height = b.max.y - Mathf.Min(0, b.min.y);
            string prefab = $"{Out}/cast-{id}.prefab";
            int tris = smr.sharedMesh.triangles.Length / 3;
            PrefabUtility.SaveAsPrefabAsset(inst, prefab);
            Object.DestroyImmediate(inst);
            Debug.Log($"[CAST] {id} prefab ok slots={string.Join("/", names)} tris={tris} height={cm.Height:F2} bounds={b}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[CAST] done");
    }

    /// "material_colors_linear": { "body": [r,g,b], ... } -> a linear colour (the toon material takes linear light).
    static Color Pick(string json, string slot)
    {
        int at = json.IndexOf("\"material_colors_linear\"");
        int k = json.IndexOf("\"" + slot + "\"", at);
        int a = json.IndexOf('[', k), z = json.IndexOf(']', k);
        var p = json.Substring(a + 1, z - a - 1).Split(',').Select(x => float.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new Color(p[0], p[1], p[2], 1);
    }
}
