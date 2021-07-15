using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]
public class WorldTopography : ScriptableObject {

    public Texture2D elevationTexture;
    public float elevationScale = 1;
    public float elevationOffset;
    public float horizontalScale = 5;
}
