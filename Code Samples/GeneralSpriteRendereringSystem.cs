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

public class GeneralSpriteRenderingSystem : MonoBehaviour, ISystem
{
	Mesh mesh;
	[SerializeField] Material _BillboardMat;
	[SerializeField] int _MaxRenderEntities = 1000000;

	public void Tick(ParallelSpreadSheet sheet)
	{
		if (mesh == null)
		{
			mesh = new Mesh();
			var meshFilter = gameObject.AddComponent<MeshFilter>();
			meshFilter.mesh = mesh;
			var renderer = gameObject.AddComponent<MeshRenderer>();
			//renderer.material = new Material(_BillboardShader);
			renderer.material = _BillboardMat;
			//renderer.material.SetFloat("_SpriteSheetSize", _NumSpritesPerSide);
		}

		_BillboardMat.SetVector("_TexSize", new Vector2(_BillboardMat.mainTexture.width, _BillboardMat.mainTexture.height));
		
		// Get all the entities that are visible to camera (and have a position)
		//var visible = sheet.GetSparseColumn((int)ComponentCode.VisibleToCamera);
		NativeLimitedList<float3> positions = sheet.GetLimitedList<float3>((int)ComponentCode.Position);
		NativeArray<byte> sprite = sheet.GetArray<byte>((int)ComponentCode.Sprite);
		NativeArray<byte> size = sheet.GetArray<byte>((int)ComponentCode.Size);
		NativeArray<bool> rendered = sheet.GetArray<bool>((int)ComponentCode.AlreadyRendered);
		NativeSparseSet<short> health = sheet.GetSparseSet<short>((int)ComponentCode.Health);
		//NativeSparseSet despawn = sheet.GetSparseSet((int)ComponentCode.Despawn);

		// Create a quad for every visible entity (use its position)
		Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
		Mesh.MeshData meshData = meshDataArray[0];

		var job = new MeshJob<QuadGenerator, MultiStreamWithVertexId>();
		job.generator._Positions = positions;
		job.generator._Sprites = sprite;
		job.generator._Size = size;
		job.generator._Health = health;
		job.generator._AlreadyRendered = rendered;

		// Use positions.Count because we're indexing into positions.dense
		int nEntities = min(positions.Count, _MaxRenderEntities);
		//Debug.LogFormat("Rendering {0} entities", nEntities);

		if(nEntities > _MaxRenderEntities)
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

		Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

		// Now clear AlreadyRendered (it's instant)
		for (int i = 0; i < rendered.Length; i++)
		{
			rendered[i] = false;
		}
	}
}
