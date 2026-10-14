using NUnit.Framework;

using UnityEditor;

using UnityEngine;

namespace HumanEcho.Tests {
    /// <summary>
    /// Runs the particle compute shader on the real GPU with synthetic masks and a fake fluid, then reads the
    /// pool back: where particles are born,
    /// what the floor does, how the fluid's velocity maps onto the screen. Uses <see
    /// cref="ParticleLayer.Simulate"/>, so nothing is drawn.
    /// </summary>
    public sealed class ParticleLayerTests {
        private const int Width = 256;
        private const int Height = 192;
        private const string ComputePath = "Assets/Games/viitorx-vrm-avtar-unity/Showcase/HumanEcho/Shaders/HumanEcho_Particles.compute";

        // The struct in HumanEcho_Particles.compute, as floats: pos(2) vel(2) life maxLife size kind color(3)
        // state.
        private const int Floats = 12;

        private sealed class Rig {
            public PersonField Field;
            public ParticleLayer Layer;
            public ParticleSettings Settings;
            public Texture2D Mask;
            public Texture2D Color;
            public RenderTexture Atlas;
            public FakeFlow Flow;
        }

        private sealed class FakeFlow : IFluidFlow {
            public Texture Velocity;
            public Vector4 Rect = new Vector4(0f, 0f, 1f, 9f / 16f);

            public Texture VelocityTexture {
                get {
                    return Velocity;
                }
            }

            public Vector4 TileRect {
                get {
                    return Rect;
                }
            }
        }

        private static ParticleSettings PlainSettings() {
            ParticleSettings s = new ParticleSettings();
            s.enabled = true;
            s.maxParticles = 40000;
            s.spawnPerSecond = 60000f;
            s.motionBoost = 0f;
            s.emitFrom = ParticleEmitFrom.Interior;
            s.lifetimeMin = 5f;
            s.lifetimeMax = 5f;
            s.sizeMinThousandths = 4f;
            s.sizeMaxThousandths = 4f;
            s.sphereFraction = 0f;
            s.colorMode = ParticleColorMode.Solid;
            s.gravity = 0f;
            s.flowInfluence = 0f;
            s.motionKick = 0f;
            s.swirl = 0f;
            s.drag = 0f;
            s.floorEnabled = false;
            return s;
        }

        private static Rig NewRig(ParticleSettings settings, Texture2D mask, IFluidFlow flow) {
            Rig rig = new Rig();
            Shader fieldShader = Shader.Find("HumanEcho/PersonField");
            Shader drawShader = Shader.Find("HumanEcho/ParticleDraw");
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Assert.IsNotNull(fieldShader);
            Assert.IsNotNull(drawShader);
            Assert.IsNotNull(compute, "compute shader at " + ComputePath);
            rig.Field = new PersonField(fieldShader);
            rig.Mask = mask;
            rig.Color = GpuTestUtil.ColorTexture(Width, Height, new Color32(255, 255, 255, 255));
            rig.Field.Process(rig.Color, rig.Mask, Width, Height, false, 0f);
            rig.Layer = new ParticleLayer();
            Assert.IsTrue(rig.Layer.Build(compute, drawShader, flow), "particle layer must build");
            rig.Layer.Settings = settings;
            rig.Settings = settings;
            return rig;
        }

        private static void Dispose(Rig rig) {
            rig.Layer.Dispose();
            rig.Field.Dispose();
            GpuTestUtil.Destroy(rig.Mask);
            GpuTestUtil.Destroy(rig.Color);
            GpuTestUtil.Destroy(rig.Atlas);
        }

        private static FrameContext Frame(float dt, float time) {
            FrameContext frame = new FrameContext();
            frame.DeltaTime = dt;
            frame.Time = time;
            frame.ViewHeight = 2.4f;
            frame.ViewWidth = 2.4f * 4f / 3f;
            frame.NewMask = false;
            return frame;
        }

        private static float[] ReadPool(ParticleLayer layer) {
            float[] data = new float[layer.Capacity * Floats];
            layer.ParticleBuffer.GetData(data);
            return data;
        }

        private static Texture2D CentreBox() {
            return GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                return GpuTestUtil.Box(x, y, 90f, 60f, 166f, 132f, 3f);
            });
        }

        [Test]
        public void ParticlesAreBornInsideTheMask() {
            Rig rig = NewRig(PlainSettings(), CentreBox(), null);
            try {
                Assert.IsTrue(rig.Layer.Simulate(rig.Field, Frame(0.1f, 0f)));
                float[] pool = ReadPool(rig.Layer);
                // The picture is as wide as the (4:3) screen, so the picture rectangle is the whole screen:
                // 4/3 x 1 screen heights.
                float pictureWidth = 4f / 3f;
                int live = 0;
                int outside = 0;
                for (int i = 0; i < rig.Layer.Capacity; i++) {
                    int o = i * Floats;
                    if (pool[o + 4] <= 0f) {
                        continue;
                    }
                    live++;
                    float u = pool[o] / pictureWidth + 0.5f;
                    float v = pool[o + 1] + 0.5f;
                    float column = u * Width;
                    float rowFromTop = (1f - v) * Height;
                    bool inside = column > 90f - 2f && column < 166f + 2f && rowFromTop > 60f - 2f && rowFromTop < 132f + 2f;
                    if (!inside) {
                        outside++;
                    }
                }
                Assert.Greater(live, 3000, "most of the 6000 requested particles must find a spot in a box that is a fifth of the picture");
                Assert.AreEqual(0, outside, "no particle may be born outside the mask");
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void NothingIsBornWhereThereIsNoPerson() {
            Texture2D empty = GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                return 0f;
            });
            Rig rig = NewRig(PlainSettings(), empty, null);
            try {
                Assert.IsTrue(rig.Layer.Simulate(rig.Field, Frame(0.1f, 0f)));
                float[] pool = ReadPool(rig.Layer);
                int live = 0;
                for (int i = 0; i < rig.Layer.Capacity; i++) {
                    if (pool[i * Floats + 4] > 0f) {
                        live++;
                    }
                }
                Assert.AreEqual(0, live);
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void ADisabledLayerSpawnsAndMovesNothing() {
            ParticleSettings s = PlainSettings();
            s.enabled = false;
            Rig rig = NewRig(s, CentreBox(), null);
            try {
                Assert.IsFalse(rig.Layer.Simulate(rig.Field, Frame(0.1f, 0f)));
                Assert.AreEqual(0, rig.Layer.LastSpawnCount);
                Assert.AreEqual(0, rig.Layer.Capacity, "a layer that never ran allocates nothing");
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void TheSpawnRateIsExactAcrossFrames() {
            ParticleSettings s = PlainSettings();
            s.spawnPerSecond = 1200f;
            Rig rig = NewRig(s, CentreBox(), null);
            try {
                int total = 0;
                for (int i = 0; i < 100; i++) {
                    rig.Layer.Simulate(rig.Field, Frame(1f / 100f, i / 100f));
                    total += rig.Layer.LastSpawnCount;
                }
                // The remainder carries over, so the total is the rate times the time, give or take the one
                // particle still owed.
                Assert.GreaterOrEqual(total, 1199, "100 frames of 10 ms at 1200 per second must owe 1200 particles");
                Assert.LessOrEqual(total, 1200);
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void TheFloorPileIsCappedAndParticlesSettleOnIt() {
            ParticleSettings s = PlainSettings();
            s.gravity = -1.5f;
            s.floorEnabled = true;
            s.floorHeight = 0.1f;
            s.pileMaxHeight = 0.04f;
            s.pileGrowthThousandths = 4f;
            s.pileSeconds = 30f;
            s.spawnPerSecond = 20000f;
            Rig rig = NewRig(s, CentreBox(), null);
            try {
                for (int i = 0; i < 180; i++) {
                    rig.Layer.Simulate(rig.Field, Frame(1f / 60f, i / 60f));
                }
                float[] pool = ReadPool(rig.Layer);
                float floorY = s.floorHeight - 0.5f;
                int settled = 0;
                float highest = float.NegativeInfinity;
                for (int i = 0; i < rig.Layer.Capacity; i++) {
                    int o = i * Floats;
                    if (pool[o + 4] > 0f && pool[o + 11] > 0.5f) {
                        settled++;
                        highest = Mathf.Max(highest, pool[o + 1]);
                    }
                }
                Assert.Greater(settled, 200, "falling particles must land on the floor and stay");
                Assert.LessOrEqual(highest, floorY + s.pileMaxHeight + 0.0035f, "the pile must not grow past pileMaxHeight");
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void TheFluidVelocityMovesParticlesTheRightWayOnTheScreen() {
            // An atlas whose left half pushes +x and whose right half pushes -x (atlas uv per second), mapped
            // by a container rect over the whole atlas.
            // A particle on the left half of the screen must go right, one on the right half must go left.
            FakeFlow flow = new FakeFlow();
            ParticleSettings s = PlainSettings();
            s.flowInfluence = 1f;
            s.spawnPerSecond = 20000f;
            Rig rig = NewRig(s, GpuTestUtil.MaskTexture(Width, Height, delegate (int x, int y) {
                // Two boxes, one in each half of the picture.
                float left = GpuTestUtil.Box(x, y, 30f, 60f, 100f, 132f, 3f);
                float right = GpuTestUtil.Box(x, y, 156f, 60f, 226f, 132f, 3f);
                return Mathf.Max(left, right);
            }), flow);
            rig.Atlas = GpuTestUtil.MakeAtlas(64, 64, delegate (float u, float v) {
                return u < 0.5f ? new Vector2(0.2f, 0f) : new Vector2(-0.2f, 0f);
            });
            flow.Velocity = rig.Atlas;
            flow.Rect = new Vector4(0f, 0f, 1f, 0.5f);
            try {
                for (int i = 0; i < 6; i++) {
                    rig.Layer.Simulate(rig.Field, Frame(1f / 60f, i / 60f));
                }
                float[] pool = ReadPool(rig.Layer);
                double leftSum = 0;
                double rightSum = 0;
                int leftCount = 0;
                int rightCount = 0;
                for (int i = 0; i < rig.Layer.Capacity; i++) {
                    int o = i * Floats;
                    if (pool[o + 4] <= 0f) {
                        continue;
                    }
                    if (pool[o] < 0f) {
                        leftSum += pool[o + 2];
                        leftCount++;
                    } else {
                        rightSum += pool[o + 2];
                        rightCount++;
                    }
                }
                Assert.Greater(leftCount, 50);
                Assert.Greater(rightCount, 50);
                Assert.Greater(leftSum / leftCount, 0.0, "left of the screen: the fluid pushes +x");
                Assert.Less(rightSum / rightCount, 0.0, "right of the screen: the fluid pushes -x");
            } finally {
                Dispose(rig);
            }
        }

        [Test]
        public void TheFluidVelocityIsScaledByTheContainersShareOfTheAtlas() {
            // atlas velocity v over a container of height h is v / h screen heights per second; the follow
            // rate is 4 per second, so after
            // about 1 s a particle has reached 1 - e^-4 = 98 % of it.
            FakeFlow flow = new FakeFlow();
            ParticleSettings s = PlainSettings();
            s.flowInfluence = 1f;
            s.spawnPerSecond = 4000f;
            Rig rig = NewRig(s, CentreBox(), flow);
            rig.Atlas = GpuTestUtil.MakeAtlas(32, 32, delegate (float u, float v) {
                return new Vector2(0f, 0.1f);
            });
            flow.Velocity = rig.Atlas;
            flow.Rect = new Vector4(0f, 0f, 1f, 0.25f);
            try {
                for (int i = 0; i < 90; i++) {
                    rig.Layer.Simulate(rig.Field, Frame(1f / 60f, i / 60f));
                }
                float[] pool = ReadPool(rig.Layer);
                double sum = 0;
                int count = 0;
                for (int i = 0; i < rig.Layer.Capacity; i++) {
                    int o = i * Floats;
                    // Only particles old enough to have reached the flow speed.
                    if (pool[o + 4] > 0f && pool[o + 5] - pool[o + 4] > 1.0f) {
                        sum += pool[o + 3];
                        count++;
                    }
                }
                Assert.Greater(count, 100);
                // 0.1 atlas uv per second over a tile 0.25 high = 0.4 screen heights per second.
                Assert.AreEqual(0.4, sum / count, 0.06, "velocity must be divided by the tile height");
            } finally {
                Dispose(rig);
            }
        }
    }
}
