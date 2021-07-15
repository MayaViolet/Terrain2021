using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class ComputeTextureBakeWindow : EditorWindow {

    [MenuItem("Window/Compute Baker")]
    static void Pop()
    {
        GetWindow<ComputeTextureBakeWindow>();
    }

    ComputeShader shader;
    Texture2D source;

    bool validSetup
    {
        get
        {
            if (shader == null || source == null)
            {
                return false;
            }

            return true;
        }
    }

	void OnGUI()
	{
        shader = EditorGUILayout.ObjectField("Filter Kernel", shader, typeof(ComputeShader), false) as ComputeShader;
        source = EditorGUILayout.ObjectField("Source Texture", source, typeof(Texture2D), false) as Texture2D;

        if (!validSetup)
        {
            GUI.color = Color.grey;
        }
        if (GUILayout.Button("Bake"))
        {
            if (validSetup)
            {
                Bake();
            }
        }
        GUI.color = Color.white;
	}

    void Bake()
    {
        
    }
}
