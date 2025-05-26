using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace XNodeEditor
{
    /// <summary> Deals with modified assets </summary>
    class NodeEditorAssetModProcessor : UnityEditor.AssetModificationProcessor
    {

        /// <summary> Automatically delete Node sub-assets before deleting their script.
        /// This is important to do, because you can't delete null sub assets.
        /// <para/> For another workaround, see: https://gitlab.com/RotaryHeart-UnityShare/subassetmissingscriptdelete </summary> 
        private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            // Skip processing anything without the .cs extension
            if (Path.GetExtension(path) != ".cs") return AssetDeleteResult.DidNotDelete;

            try
            {
                // Get the object that is requested for deletion
                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

                // If we aren't deleting a script, return
                if (!(obj is UnityEditor.MonoScript)) return AssetDeleteResult.DidNotDelete;

                // Check script type. Return if deleting a non-node script
                UnityEditor.MonoScript script = obj as UnityEditor.MonoScript;
                System.Type scriptType = script.GetClass();

                // Null check for script type
                if (scriptType == null) return AssetDeleteResult.DidNotDelete;

                // Check if it's a Node type
                if (scriptType != typeof(XNode.Node) && !scriptType.IsSubclassOf(typeof(XNode.Node)))
                    return AssetDeleteResult.DidNotDelete;

                // Find all ScriptableObjects using this script
                string[] guids = AssetDatabase.FindAssets("t:" + scriptType.Name);

                for (int i = 0; i < guids.Length; i++)
                {
                    string assetpath = AssetDatabase.GUIDToAssetPath(guids[i]);

                    // Skip if asset path is invalid
                    if (string.IsNullOrEmpty(assetpath)) continue;

                    try
                    {
                        // Check if asset has missing scripts before loading
                        if (HasMissingScripts(assetpath))
                        {
                            Debug.LogWarning($"Skipping asset with missing scripts: {assetpath}");
                            continue;
                        }

                        Object[] objs = LoadAssetRepresentationsSafely(assetpath);

                        // Null check for loaded objects
                        if (objs == null) continue;

                        for (int k = 0; k < objs.Length; k++)
                        {
                            // Skip null objects
                            if (objs[k] == null) continue;

                            XNode.Node node = objs[k] as XNode.Node;

                            // Additional null checks
                            if (node == null || node.GetType() != scriptType) continue;

                            if (node.graph != null)
                            {
                                // Delete the node and notify the user
                                Debug.LogWarning(node.name + " of " + node.graph + " depended on deleted script and has been removed automatically.", node.graph);
                                node.graph.RemoveNode(node);
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Error processing asset at path {assetpath}: {e.Message}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in OnWillDeleteAsset for path {path}: {e.Message}");
            }

            // We didn't actually delete the script. Tell the internal system to carry on with normal deletion procedure
            return AssetDeleteResult.DidNotDelete;
        }

        /// <summary> Safely load asset representations, handling missing scripts </summary>
        private static Object[] LoadAssetRepresentationsSafely(string assetPath)
        {
            try
            {
                // First check if the main asset can be loaded
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset == null)
                {
                    Debug.LogWarning($"Could not load main asset at path: {assetPath}");
                    return null;
                }

                // Use SerializedObject to check for missing scripts
                var serializedObject = new SerializedObject(mainAsset);
                var script = serializedObject.FindProperty("m_Script");

                if (script != null && script.objectReferenceValue == null)
                {
                    Debug.LogWarning($"Asset has missing script reference: {assetPath}");
                    return null;
                }

                // If main asset is OK, try to load all representations
                return AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error loading asset representations for {assetPath}: {e.Message}");
                return null;
            }
        }

        /// <summary> Check if an asset has missing script references </summary>
        private static bool HasMissingScripts(string assetPath)
        {
            try
            {
                // Read asset file content to check for missing script references
                string assetContent = File.ReadAllText(assetPath);

                // Look for common indicators of missing scripts
                if (assetContent.Contains("m_Script: {fileID: 0}") ||
                    assetContent.Contains("m_Script: {instanceID: 0}"))
                {
                    return true;
                }

                // Try to load main asset and check its script reference
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset == null) return true;

                // For ScriptableObjects, check if the script reference is valid
                if (mainAsset is ScriptableObject)
                {
                    var serializedObject = new SerializedObject(mainAsset);
                    var scriptProperty = serializedObject.FindProperty("m_Script");

                    if (scriptProperty != null && scriptProperty.objectReferenceValue == null)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                // If we can't read the asset, assume it might have issues
                return true;
            }
        }

        /// <summary> Automatically re-add loose node assets to the Graph node list </summary>
        [InitializeOnLoadMethod]
        private static void OnReloadEditor()
        {
            // Use EditorApplication.delayCall to avoid timing issues during assembly reload
            EditorApplication.delayCall += PerformNodeGraphCleanup;
        }

        private static void PerformNodeGraphCleanup()
        {
            try
            {
                // Find all NodeGraph assets
                string[] guids = AssetDatabase.FindAssets("t:" + typeof(XNode.NodeGraph).Name);

                // Keep track of processed graphs to avoid circular references
                HashSet<string> processedPaths = new HashSet<string>();
                List<string> assetsToRefresh = new List<string>();

                for (int i = 0; i < guids.Length; i++)
                {
                    string assetpath = AssetDatabase.GUIDToAssetPath(guids[i]);

                    // Skip if asset path is invalid or already processed
                    if (string.IsNullOrEmpty(assetpath) || processedPaths.Contains(assetpath))
                        continue;

                    processedPaths.Add(assetpath);

                    try
                    {
                        // Check for missing scripts before processing
                        if (HasMissingScripts(assetpath))
                        {
                            Debug.LogWarning($"NodeGraph at {assetpath} has missing script references. Consider cleaning it manually.");
                            continue;
                        }

                        // Load the NodeGraph with proper type checking
                        XNode.NodeGraph graph = AssetDatabase.LoadAssetAtPath<XNode.NodeGraph>(assetpath);

                        // Skip if graph couldn't be loaded
                        if (graph == null)
                        {
                            Debug.LogWarning($"Could not load NodeGraph at path: {assetpath}");
                            continue;
                        }

                        // Initialize nodes list if null
                        if (graph.nodes == null)
                        {
                            graph.nodes = new System.Collections.Generic.List<XNode.Node>();
                        }

                        // Remove null items from the graph
                        int removedCount = graph.nodes.RemoveAll(x => x == null);
                        if (removedCount > 0)
                        {
                            Debug.Log($"Removed {removedCount} null node references from {graph.name}");
                        }

                        // Load all asset representations safely
                        Object[] objs = LoadAssetRepresentationsSafely(assetpath);

                        // Skip if no objects found
                        if (objs == null) continue;

                        // Track if any changes were made
                        bool changesMade = false;

                        // Ensure that all sub node assets are present in the graph node list
                        for (int u = 0; u < objs.Length; u++)
                        {
                            // Ignore null sub assets
                            if (objs[u] == null) continue;

                            // Try to cast to Node
                            XNode.Node node = objs[u] as XNode.Node;

                            // Skip if not a node or already in the list
                            if (node == null || graph.nodes.Contains(node)) continue;

                            // Validate the node before adding
                            if (IsValidNode(node))
                            {
                                // Add node to graph and mark changes
                                graph.nodes.Add(node);
                                changesMade = true;

                                Debug.Log($"Added loose node '{node.name}' to graph '{graph.name}'");
                            }
                        }

                        // Mark asset as dirty if changes were made
                        if (changesMade)
                        {
                            EditorUtility.SetDirty(graph);
                            assetsToRefresh.Add(assetpath);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Error processing NodeGraph at path {assetpath}: {e.Message}");
                    }
                }

                // Save and refresh assets if any changes were made
                if (assetsToRefresh.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                    foreach (string path in assetsToRefresh)
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in PerformNodeGraphCleanup: {e.Message}");
            }
        }

        /// <summary> Validate if a node is valid and safe to add </summary>
        private static bool IsValidNode(XNode.Node node)
        {
            try
            {
                // Check if node has a valid script reference
                var serializedObject = new SerializedObject(node);
                var scriptProperty = serializedObject.FindProperty("m_Script");

                if (scriptProperty != null && scriptProperty.objectReferenceValue == null)
                {
                    Debug.LogWarning($"Node {node.name} has missing script reference");
                    return false;
                }

                // Additional validation can be added here
                return node != null && !string.IsNullOrEmpty(node.name);
            }
            catch
            {
                return false;
            }
        }

        /// <summary> Manual cleanup method that can be called from menu </summary>
        [MenuItem("Tools/xNode/Clean Up Node Graphs")]
        private static void ManualNodeGraphCleanup()
        {
            Debug.Log("Starting manual NodeGraph cleanup...");
            PerformNodeGraphCleanup();
            Debug.Log("NodeGraph cleanup completed.");
        }

        /// <summary> Clean up missing script references in NodeGraphs </summary>
        [MenuItem("Tools/xNode/Clean Missing Script References")]
        private static void CleanMissingScriptReferences()
        {
            Debug.Log("Starting missing script reference cleanup...");

            string[] guids = AssetDatabase.FindAssets("t:" + typeof(XNode.NodeGraph).Name);
            int cleanedCount = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (HasMissingScripts(assetPath))
                {
                    Debug.LogWarning($"Found NodeGraph with missing scripts: {assetPath}");

                    // You can add automatic cleanup logic here if needed
                    // For now, just report the problematic assets
                    cleanedCount++;
                }
            }

            Debug.Log($"Found {cleanedCount} NodeGraphs with missing script references. Check console for details.");
        }
    }
}