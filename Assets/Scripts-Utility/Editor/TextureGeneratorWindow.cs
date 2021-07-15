using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class TextureGeneratorWindow : EditorWindow
{
    #region Generators
    [System.Serializable]
    internal class Generator : ScriptableObject
    {
        internal virtual Color[] Generate(GlobalSettings settings)
        {
            return null;
        }

        internal virtual void SetDefaults()
        {
        }
    }

    internal enum GeneratorType
    {
        Gradient,
        Noise
    }

    [System.Serializable]
    internal class GradientGenerator : Generator
    {
        public enum Style
        {
            Linear,
            Radial
        }

        public Gradient gradient;
        public Style style;

        internal override Color[] Generate(GlobalSettings settings)
        {
            return null;
        }

        internal override void SetDefaults()
        {

        }
    }

    [System.Serializable]
    internal class NoiseGenerator : Generator
    {
        public enum Style
        {
            Mono,
            Colour,
            Vector3D,
            Vector2D,
            Normal
        }

        public Style style;

        internal override Color[] Generate(GlobalSettings settings)
        {
            var pixels = new Color[settings.width * settings.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                Vector3 pixel = new Vector3(Random.value, Random.value, Random.value);
                switch (style)
                {
                    case Style.Mono:
                        pixel.y = pixel.x;
                        pixel.z = pixel.x;
                        break;
                    case Style.Colour:
                        break;
                    case Style.Normal:
                    case Style.Vector2D:
                    case Style.Vector3D:
                        if (style == Style.Normal)
                        {
                            pixel.z = Mathf.Abs(pixel.z);
                        }
                        if (style == Style.Vector2D)
                        {
                            pixel.z = 0;
                        }
                        pixel.Normalize();
                        break;
                }
                pixels[i] = new Color(pixel.x, pixel.y, pixel.z, 1);
            }
            return pixels;
        }

        internal override void SetDefaults()
        {
            style = Style.Mono;
        }
    }
    #endregion

    [System.Serializable]
    internal struct GlobalSettings
    {
        public int width;
        public int height;
    }

    [SerializeField]
    GlobalSettings settings;

    [SerializeField]
    GeneratorType generatorType;

    [SerializeField]
    Generator generator;

    // Property references
    SerializedObject serializedObject;
    SerializedProperty settingsProperty;
    Editor generatorEditor;

    [MenuItem("Window/Texture Generator")]
    static void ShowWindow()
    {
        GetWindow<TextureGeneratorWindow>(true);
    }

    void OnEnable()
    {
        // Defaults
        settings.width = 512;
        settings.height = 512;
        MakeGenerator(generatorType);

        serializedObject = new SerializedObject(this);
        settingsProperty = serializedObject.FindProperty("settings");
    }

    void OnGUI()
    {
        serializedObject.Update();

        settingsProperty.isExpanded = true;
        EditorGUILayout.PropertyField(settingsProperty, true);

        var newGenerator = (GeneratorType)EditorGUILayout.EnumPopup(generatorType);
        if (newGenerator != generatorType)
        {
            MakeGenerator(newGenerator);
        }
        generatorType = newGenerator;

        if (generator != null)
        {
            Editor.CreateCachedEditor(generator, null, ref generatorEditor);
            generatorEditor.DrawDefaultInspector();
        }

        serializedObject.ApplyModifiedProperties();

        if (GUILayout.Button("Generate"))
        {
            if (generator != null)
            {
                Generate();
            }
        }
    }

    void MakeGenerator(GeneratorType type)
    {
        switch (type)
        {
            case GeneratorType.Gradient:
                generator = CreateInstance<GradientGenerator>();
                break;

            case GeneratorType.Noise:
                generator = CreateInstance<NoiseGenerator>();
                break;
        }

        if (generator != null)
        {
            generator.SetDefaults();
        }
    }

    void Generate()
    {
        var texture = new Texture2D(settings.width, settings.height, TextureFormat.ARGB32, false);
        var pixels = generator.Generate(settings);
        texture.SetPixels(pixels);
        var bytes = texture.EncodeToPNG();
        File.WriteAllBytes("Assets/Generated.png", bytes);
        AssetDatabase.Refresh();
    }
}
