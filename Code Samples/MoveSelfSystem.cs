using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using SpreadSheetNS.Parallel;

using static Unity.Mathematics.math;

public unsafe class MoveSelfSystem : MonoBehaviour, ISystem
{
    NativeSparseSet _Intersection;

    void OnDisable()
    {
        if (_Intersection.IsCreated) _Intersection.Dispose();
    }

    public void Tick(ParallelSpreadSheet sheet)
    {
        //MultiThreadedMoveSelf(sheet);
        SingleThreadedMoveSelf(sheet);
    }

    void SingleThreadedMoveSelf(ParallelSpreadSheet sheet)
    {
        var _Organism = sheet.GetSparseSet<int>((int)ComponentCode.Organism); // It also has to be alive!
        var _MoveIntention = sheet.GetSparseSet<MoveIntention>((int)ComponentCode.MoveIntention);
        var _Position = sheet.GetArray<float3>((int)ComponentCode.Position);
        var _Velocity = sheet.GetSparseSet<float3>((int)ComponentCode.Velocity);
        var _Size = sheet.GetArray<byte>((int)ComponentCode.Size);

        if (_Intersection.IsCreated) _Intersection.Dispose();

        _Intersection = NativeSparseSet.Intersection(_Velocity, _MoveIntention, _Organism);
        int count = _Intersection.Count;

        for (int i = 0; i < count; i++)
        {
            EntityId id = _Intersection.dense[i];

            float3 position = _Position[id];
            MoveIntention intention = _MoveIntention[id];

            float3 desired = intention.desiredPosition;

            float distance = math.distance(desired, position);

            // Distance tolerance (stop moving if within this distance)
            if (distance > _Size[id] / 2.0f)
            {
                // Calculate heading
                float3 heading = (desired - position) / distance; // normalize
                const float speed = 0.1f * Space.PersonSize;
                heading *= speed;

                // Now add velocity (read and write to Velocity component)
                float3 velocity = _Velocity[id];    // current velocity
                _Velocity[id] = velocity + heading;
            }
        }
    }

    void MultiThreadedMoveSelf(ParallelSpreadSheet sheet)
    {
        // Everything with a MoveTarget and a Position, Velocity
        var organism = sheet.GetSparseSet<int>((int)ComponentCode.Organism); // It also has to be alive!
        var moveIntention = sheet.GetSparseSet<MoveIntention>((int)ComponentCode.MoveIntention);
        var position = sheet.GetArray<float3>((int)ComponentCode.Position);
        var velocity = sheet.GetSparseSet<float3>((int)ComponentCode.Velocity);
        var size = sheet.GetArray<byte>((int)ComponentCode.Size);

        if (_Intersection.IsCreated) _Intersection.Dispose();

        _Intersection = NativeSparseSet.Intersection(velocity, moveIntention, organism);
        int count = _Intersection.Count;

        // Single-threaded for now
        new MoveSelfJob()
        {
            _Intersection = _Intersection,
            _MoveIntention = moveIntention,
            _Position = position,
            _Velocity = velocity,
            _Size = size,
            
        // This .Schedule call is an extension method I wrote that integrates with my automatic scheduler that completes jobs in order based on dependencies.
        }.Schedule(sheet,
            columnIdsReadOnly: new int[]
            {
                (int)ComponentCode.Position,
                (int)ComponentCode.MoveIntention,
                (int)ComponentCode.Size
            },
            columnIdsReadWrite: new int[] { (int)ComponentCode.Velocity, },
            nJobs: count);
    }

    // Add velocity towards desired destination
    [BurstCompile(CompileSynchronously = true)]
    unsafe struct MoveSelfJob : IJobFor
    {
        public NativeSparseSet _Intersection;

        // Read
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float3> _Position;
        public NativeSparseSet<MoveIntention> _MoveIntention;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> _Size;

        // Write
        public NativeSparseSet<float3> _Velocity;

        public void Execute(int index)
        {
            EntityId id = _Intersection.dense[index];

            float3 position = _Position[id];
            MoveIntention intention = _MoveIntention[id];

            float3 desired = intention.desiredPosition;

            float distance = math.distance(desired, position);

            // Distance tolerance (stop moving if within this distance). The tolerance is relative to the entity's size.
            if (distance > _Size[id] / 2.0f)
            {
                // Calculate heading
                float3 heading = (desired - position) / distance; // normalize
                const float speed = 0.1f * Space.PersonSize;
                heading *= speed;

                // Now add velocity (read and write to Velocity component)
                float3 velocity = _Velocity[id];    // current velocity
                _Velocity[id] = velocity + heading;
            }
        }
    }
}
