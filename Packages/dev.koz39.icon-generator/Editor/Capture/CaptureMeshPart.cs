using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KOZ39.IconGenerator
{
    internal sealed class CaptureMeshPart
    {
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly HashSet<(string Name, float Threshold)> Shapes = new();

        internal CaptureMeshPart(SkinnedMeshRenderer renderer) => Renderer = renderer;

        internal Mesh CreateMesh()
        {
            var sourceMesh = Renderer.sharedMesh;
            var selectedVertices = new bool[sourceMesh.vertexCount];
            var blendShapeDeltas = new Vector3[sourceMesh.vertexCount];

            foreach (var (name, threshold) in Shapes)
            {
                var shape = sourceMesh.GetBlendShapeIndex(name);
                var squaredThreshold = threshold * threshold;

                for (var frame = 0; frame < sourceMesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    sourceMesh.GetBlendShapeFrameVertices(
                        shape,
                        frame,
                        blendShapeDeltas,
                        null,
                        null
                    );

                    for (var vertex = 0; vertex < blendShapeDeltas.Length; vertex++)
                    {
                        selectedVertices[vertex] |=
                            blendShapeDeltas[vertex].sqrMagnitude > squaredThreshold;
                    }
                }
            }

            var submeshes = new List<int[]>();

            for (var submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
            {
                var indices = sourceMesh.GetIndices(submesh);
                var indicesPerPrimitive = sourceMesh.GetTopology(submesh) switch
                {
                    MeshTopology.Triangles => 3,
                    MeshTopology.Quads => 4,
                    _ => 1,
                };
                var keptIndices = new List<int>();

                for (var start = 0; start < indices.Length; start += indicesPerPrimitive)
                {
                    var affected = false;

                    for (var offset = 0; offset < indicesPerPrimitive; offset++)
                    {
                        affected |= selectedVertices[indices[start + offset]];
                    }

                    if (affected)
                    {
                        for (var offset = 0; offset < indicesPerPrimitive; offset++)
                        {
                            keptIndices.Add(indices[start + offset]);
                        }
                    }
                }

                submeshes.Add(keptIndices.ToArray());
            }

            if (submeshes.All(indices => indices.Length == 0))
            {
                return null;
            }

            var result = Object.Instantiate(sourceMesh);

            try
            {
                result.hideFlags = HideFlags.HideAndDontSave;

                for (var submesh = 0; submesh < submeshes.Count; submesh++)
                {
                    var indices = submeshes[submesh];
                    var baseVertex = indices.Length > 0 ? indices.Min() : 0;

                    for (var index = 0; index < indices.Length; index++)
                    {
                        indices[index] -= baseVertex;
                    }

                    result.SetIndices(
                        indices,
                        sourceMesh.GetTopology(submesh),
                        submesh,
                        false,
                        baseVertex
                    );
                }

                result.bounds = CalculateIndexedBounds(result);

                return result;
            }
            catch
            {
                Object.DestroyImmediate(result);
                throw;
            }
        }

        internal void ResetDeletionWeights(SkinnedMeshRenderer copy)
        {
            foreach (var (name, _) in Shapes)
            {
                copy.SetBlendShapeWeight(copy.sharedMesh.GetBlendShapeIndex(name), 0);
            }
        }

        internal static Bounds CalculateIndexedBounds(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var bounds = new Bounds();
            var isFirstVertex = true;

            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                foreach (var index in mesh.GetIndices(submesh))
                {
                    if (isFirstVertex)
                    {
                        bounds = new Bounds(vertices[index], Vector3.zero);
                        isFirstVertex = false;
                    }
                    else
                    {
                        bounds.Encapsulate(vertices[index]);
                    }
                }
            }

            return bounds;
        }
    }
}
