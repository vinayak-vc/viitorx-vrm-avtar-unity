using System.Runtime.CompilerServices;

// The tests drive the GPU stages with synthetic textures through the internal entry points
// (PersonField.Process, ParticleLayer.Simulate).
[assembly: InternalsVisibleTo("HumanEcho.Tests")]
