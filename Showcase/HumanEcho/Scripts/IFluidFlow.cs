using UnityEngine;

namespace HumanEcho {
    /// <summary>
    /// Where a layer can read the fluid's velocity from (FluXY keeps it in one atlas texture shared by every
    /// container of a solver). Null while the
    /// fluid is off; a layer that wants the flow then simply sees none.
    /// </summary>
    public interface IFluidFlow {
        /// <summary>FluXY's velocity buffer: rg = velocity in ATLAS uv units per second. Null while the fluid
        /// is off.</summary>
        Texture VelocityTexture { get; }

        /// <summary>Where the screen-filling container sits in that atlas: x, y offset and z, w size, atlas
        /// uv. w &lt;= 0 until the solver has run once.</summary>
        Vector4 TileRect { get; }
    }
}
