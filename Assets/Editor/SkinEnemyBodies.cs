using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SkinEnemyBodies
{
    private static readonly string[] EnemyRoots = {
        "TORITO_1", "TORITO_2", "MARIMONDA_1_MORADA", "MARIMONDA_2_CAFE"
    };

    [MenuItem("Tools/Skin Enemy Bodies")]
    public static void SkinAll()
    {
        int done = 0;
        foreach (var rootName in EnemyRoots)
        {
            var root = GameObject.Find(rootName);
            if (root == null) { Debug.LogWarning("No root: " + rootName); continue; }
            if (SkinOne(root.transform)) done++;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Skinned " + done + " bodies.");
    }

    private static bool SkinOne(Transform root)
    {
        var cuerpo = root.Find("cuerpo");
        var armature = root.Find("Armature");
        if (cuerpo == null || armature == null) return false;
        var mf = cuerpo.GetComponent<MeshFilter>();
        var mr = cuerpo.GetComponent<MeshRenderer>();
        if (mf == null || mr == null || mf.sharedMesh == null) return false;

        var srcMesh = mf.sharedMesh;
        float boneScale = armature.lossyScale.x;

        var boneList = new List<Transform>();
        foreach (Transform t in armature.GetComponentsInChildren<Transform>(true))
            if (t != armature) boneList.Add(t);
        var bones = boneList.ToArray();

        Transform hips = null;
        foreach (var b in bones)
        {
            if (b.name == "mixamorig:Hips") { hips = b; break; }
        }

        int vcount = srcMesh.vertexCount;
        var srcVerts = srcMesh.vertices;
        var weights = new BoneWeight[vcount];

        var segStart = new Vector3[bones.Length];
        var segEnd = new Vector3[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            segStart[i] = b.position;
            if (b.childCount > 0)
            {
                var avg = Vector3.zero;
                for (int c = 0; c < b.childCount; c++) avg += b.GetChild(c).position;
                avg /= b.childCount;
                segEnd[i] = avg;
            }
            else
            {
                segEnd[i] = b.position + b.TransformDirection(Vector3.up) * (0.05f * boneScale);
            }
        }

        const int K = 4;
        var idx = new int[K];
        var dst = new float[K];
        float eps = 0.01f * boneScale;

        for (int vi = 0; vi < vcount; vi++)
        {
            Vector3 p = cuerpo.TransformPoint(srcVerts[vi]);

            for (int k = 0; k < K; k++) { idx[k] = -1; dst[k] = float.MaxValue; }

            for (int i = 0; i < bones.Length; i++)
            {
                float d = DistToSegment(p, segStart[i], segEnd[i]);
                for (int k = 0; k < K; k++)
                {
                    if (d < dst[k])
                    {
                        for (int j = K - 1; j > k; j--) { idx[j] = idx[j - 1]; dst[j] = dst[j - 1]; }
                        idx[k] = i; dst[k] = d;
                        break;
                    }
                }
            }

            float[] w = new float[K];
            float sum = 0f;
            for (int k = 0; k < K; k++)
            {
                if (idx[k] < 0) continue;
                w[k] = 1f / (dst[k] + eps);
                sum += w[k];
            }
            if (sum <= 0f) continue;

            int[] bones4 = new int[K];
            float[] w4 = new float[K];
            for (int k = 0; k < K; k++)
            {
                w4[k] = (idx[k] < 0) ? 0f : w[k] / sum;
                bones4[k] = (idx[k] < 0) ? 0 : idx[k];
            }

            var bw = new BoneWeight();
            int c0 = 0;
            for (int k = 0; k < K; k++)
            {
                if (w4[k] <= 0f) continue;
                if (c0 == 0) { bw.boneIndex0 = bones4[k]; bw.weight0 = w4[k]; }
                else if (c0 == 1) { bw.boneIndex1 = bones4[k]; bw.weight1 = w4[k]; }
                else if (c0 == 2) { bw.boneIndex2 = bones4[k]; bw.weight2 = w4[k]; }
                else { bw.boneIndex3 = bones4[k]; bw.weight3 = w4[k]; }
                c0++;
            }
            weights[vi] = bw;
        }

        var newMesh = Object.Instantiate(srcMesh);
        newMesh.name = srcMesh.name + "_skinned";
        newMesh.bindposes = ComputeBindposes(bones, cuerpo);
        newMesh.boneWeights = weights;
        newMesh.RecalculateBounds();

        string dir = "Assets/Characters/Enemys/EnemysAnimations/Meshes";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            string parentA = "Assets/Characters/Enemys";
            string parentB = "Assets/Characters/Enemys/EnemysAnimations";
            if (AssetDatabase.IsValidFolder(dir)) { }
            else if (AssetDatabase.IsValidFolder(parentB))
            {
                AssetDatabase.CreateFolder(parentB, "Meshes");
            }
            else if (AssetDatabase.IsValidFolder(parentA))
            {
                AssetDatabase.CreateFolder(parentA, "EnemysAnimations");
                AssetDatabase.CreateFolder(parentB, "Meshes");
            }
            else
            {
                return false;
            }
        }
        string mpath = dir + "/" + root.name + "_" + newMesh.name + ".asset";
        AssetDatabase.CreateAsset(newMesh, mpath);

        Undo.RegisterCompleteObjectUndo(cuerpo.gameObject, "Skin body: " + root.name);

        var mats = mr.sharedMaterials;

        UnityEngine.Object.DestroyImmediate(mf);
        UnityEngine.Object.DestroyImmediate(mr);

        var smr = cuerpo.gameObject.AddComponent<SkinnedMeshRenderer>();
        smr.rootBone = hips;
        smr.bones = bones;
        smr.sharedMesh = newMesh;
        smr.sharedMaterials = (Material[])mats.Clone();

        EditorUtility.SetDirty(cuerpo.gameObject);
        return true;
    }

    private static Matrix4x4[] ComputeBindposes(Transform[] bones, Transform skinRoot)
    {
        var arr = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            arr[i] = bones[i].worldToLocalMatrix * skinRoot.localToWorldMatrix;
        }
        return arr;
    }

    private static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float l2 = ab.sqrMagnitude;
        if (l2 < 1e-6f) return (p - a).magnitude;
        float t = Vector3.Dot(p - a, ab) / l2;
        t = Mathf.Clamp01(t);
        return (p - (a + ab * t)).magnitude;
    }
}