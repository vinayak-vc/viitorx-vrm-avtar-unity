using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// One LOOK of Human Echo: which layers are on and how each is tuned. The showcase ships three (one per
    /// reference clip) and cycles them live;
    /// every layer reads the active one every frame, so any value can be edited while playing. Do not put
    /// runtime toggles or quality choices on
    /// these assets (an asset edited in Play mode keeps the edit in the Editor, ADR-091): they belong to the
    /// controller.
    /// </summary>
    [CreateAssetMenu(fileName = "HumanEchoSettings", menuName = "Showcase/Human Echo Settings")]
    public sealed class HumanEchoSettings : ScriptableObject {
        [Tooltip("Shown in the HUD.")]
        public string displayName = "Look";
        [Tooltip("Free text: what this look is meant to resemble.")]
        [TextArea(2, 4)] public string notes = "";
        [Min(0.5f)] public float viewHeightMetres = 2.4f;
        public Color backgroundColor = new Color(0.01f, 0.01f, 0.02f, 1f);
        public PersonSettings person = new PersonSettings();
        public SmokeSettings smoke = new SmokeSettings();
        public EchoSettings echo = new EchoSettings();
        public OutlineSettings outline = new OutlineSettings();
        public ParticleSettings particles = new ParticleSettings();
    }
}
