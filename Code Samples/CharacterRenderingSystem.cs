// The code for this project is written in C# in conjunction with the Unity engine.
// Although, most Unity features are not used. I mainly stuck with the engine to have a useable graphics API. Future projects are using Vulkan API.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using ProceduralMeshes;
using ProceduralMeshes.Generators;
using ProceduralMeshes.Streams;
using Unity.Jobs;
using SpreadSheetNS.Parallel;

using static Unity.Mathematics.math;

// If you're not familiar with Unity, "MonoBehaviour" is just the base class that lets you have an entry-point into
// the application's main loop. And it lets you rearrange scripts visually, which is helpful for design. On a real project,
// I'd develop a custom system visualization solution that renders a UI element per ISystem instance.
public class CharacterRenderingSystem : MonoBehaviour, ISystem
{
    [SerializeField] List<CharacterSpritesheetScriptableAsset> _Characters = new List<CharacterSpritesheetScriptableAsset>();

    // Need a mesh for each character type
    Dictionary<CharacterSpritesheetScriptableAsset, GameObject> _MeshGameObjects = new Dictionary<CharacterSpritesheetScriptableAsset, GameObject>();
    [SerializeField] Shader _BillboardShader;
    [SerializeField] int _MaxRenderEntities = 1000000;

    public void Tick(ParallelSpreadSheet sheet)
    {
        // Todo: order by trait count (make it start with more specific characters first)
        foreach (CharacterSpritesheetScriptableAsset character in _Characters)
        {
            Mesh mesh;

            // Get the mesh for this character
            if (_MeshGameObjects.TryGetValue(character, out GameObject go))
            {
                mesh = go.GetComponent<MeshFilter>().mesh;
            }
            else
            {
                if (_BillboardShader == null) Debug.LogErrorFormat("{0}: Please assign a shader", nameof(CharacterRenderingSystem));

                character.characterName = character.GenerateName();
                go = new GameObject(character.characterName); // "batched mesh"
                go.transform.SetParent(gameObject.transform);
                _MeshGameObjects.Add(character, go);

                //Debug.Log("Added " + character.characterName, go);

                mesh = new Mesh();
                var meshFilter = go.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                var material = new Material(_BillboardShader);
                renderer.material = material;

                // Set texture
                material.mainTexture = character.spriteSheetTexture;
                material.mainTexture.filterMode = FilterMode.Point;

                // Set texture dimensions
                material.SetVector("_TexSize", new Vector2(character.spriteSheetTexture.width, character.spriteSheetTexture.height));

                // We already know the texture dimensions. So just calculate and provide the sprite size
                int nColumns = character.spriteSheetTexture.width / character.spritePixelWidth;
                int nRows = character.spriteSheetTexture.height / character.spritePixelHeight;
                //Debug.LogFormat("nColumns: {0}", nColumns);
                material.SetFloat("_SpriteSheetNumColumns", nColumns);
                material.SetFloat("_SpriteSheetNumRows", nRows);
            }

            // Get all the entities that are visible to camera (and have a position)
            //var visible = sheet.GetSparseColumn((int)ComponentCode.VisibleToCamera);
            NativeLimitedList<float3> positions = sheet.GetLimitedList<float3>((int)ComponentCode.Position);
            NativeArray<byte> size = sheet.GetArray<byte>((int)ComponentCode.Size);
            NativeSparseSet<int> alive = sheet.GetSparseSet<int>((int)ComponentCode.Organism);
            NativeArray<bool> rendered  = sheet.GetArray<bool>((int)ComponentCode.AlreadyRendered);

            // Do an intersection on the character's traits
            var columns = new List<SparseColumn>(4);
            foreach (var trait in character.componentTraits)
            {
                columns.Add(sheet.sheet.GetSparseColumn((int)trait));
            }
            // Needs to be alive (for now), We'll just handle the death rendering on the item level. Kinda messy, idk. 
            columns.Add(sheet.sheet.GetSparseColumnWithData((int)ComponentCode.Organism));
            var intersection = SparseColumn.Intersection(columns);

            // Create a quad for every visible entity (use its position)
            Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData = meshDataArray[0];

            var job = new MeshJob<SpriteQuadGenerator, MultiStreamWithVertexId>();
            job.generator._EntityIds = intersection.ToNativeSparseSet();
            job.generator._Positions = positions;
            job.generator._Size = size;
            job.generator._AlreadyRendered = rendered;

            //Debug.LogFormat("Intersection found {0} entities", intersection.Count);

            // Use positions.Count because we're indexing into positions.dense
            int nEntities = min(intersection.Count, _MaxRenderEntities);

#if UNITY_EDITOR
            // Track number of characters in this batch being rendered
            go.name = string.Format("{0} ({1})", character.characterName, nEntities);
#endif

            if (nEntities > _MaxRenderEntities)
                Debug.LogWarningFormat("Warning! Max renderable entities reached! Only rendering {0} entities", $"{_MaxRenderEntities:n0}");

            job.generator.nEntities = nEntities;
            job.streams.Setup(
                meshData,
                mesh.bounds = job.generator.Bounds,
                job.generator.VertexCount,
                job.generator.IndexCount
            );
            job
            .ScheduleParallel(nEntities, 1, default)
            .Complete();
            
            intersection.Dispose();

            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
        }
    }
}
