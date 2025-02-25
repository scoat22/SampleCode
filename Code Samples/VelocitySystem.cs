using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using SpreadSheetNS.Parallel;

using static Unity.Mathematics.math;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

/// <summary>
/// For every entity with a Velocity component (float3) and Position component (float3), add the velocity to the position.
/// This should go last in the Physics system list
/// </summary>
public unsafe class VelocitySystem : MonoBehaviour, ISystem
{
    int[] _ReadWriteColumns = { (int)ComponentCode.Position, (int)ComponentCode.Velocity };
    int[] _ReadColumns = { };

    public void Tick(ParallelSpreadSheet sheet)
    {
        NativeArray    <float3> position = sheet.GetArray    <float3>((int)ComponentCode.Position);
        NativeSparseSet<float3> velocity = sheet.GetSparseSet<float3>((int)ComponentCode.Velocity);
        
        // Single-threaded for now
        new VelocityJob()
        {
            _Position = position,
            _Velocity = velocity,

        }.Schedule(sheet, _ReadColumns, _ReadWriteColumns, nJobs: velocity.Count);
    }

    [BurstCompile(CompileSynchronously = true)]
    struct VelocityJob : IJobFor
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float3> _Position;
        // ReadOnly
        public NativeSparseSet<float3> _Velocity;

        public void Execute(int index)
        {
            EntityId id = _Velocity.dense[index];
            //Debug.Log(string.Format("Id of velocity entity: {0}", id.value));

            float3 position = _Position[id];
            float3 velocity = _Velocity[id];
#if UNITY_EDITOR
            if(length(velocity) > 2.0f) 
                Debug.LogWarning(string.Format("[{0}] Velocity > 2 ({1}, value {2}) ", id.value, length(velocity), velocity));
#endif
            // Now apply the velocity to position
            _Position[id] = position + velocity;

            // Reset velocity
            _Velocity[id] = float3(0);
        }
    }
}
