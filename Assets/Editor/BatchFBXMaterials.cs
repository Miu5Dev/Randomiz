using UnityEditor;
using UnityEngine;

public class BatchFBXMaterials
{
    [MenuItem("Assets/Set External Materials (Legacy) to Selected FBX")]
    static void SetExternalMaterials()
    {
        int count = 0;
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
            count++;
        }

        Debug.Log($"[BatchFBX] External Materials aplicado a {count} FBX.");
    }
}