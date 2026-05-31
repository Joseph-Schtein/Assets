using UnityEngine;
using UnityEditor;

public class FixTrailMaterial
{
    [MenuItem("Tools/Fix Trail Material")]
    public static void Fix()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/tail.mat");
        if (mat != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("Fixed tail.mat to include _EMISSION keyword.");
        }
    }
}
