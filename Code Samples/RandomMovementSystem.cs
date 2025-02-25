using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using SpreadSheetNS.Parallel;

/// <summary>
/// Desired destination
/// </summary>
public struct MoveIntention
{
    /// <summary>
    /// Where to move to
    /// </summary>
    public float3 desiredPosition;

    public MoveIntention(float3 desired)
    {
        desiredPosition = desired;
    }
}

/// <summary>
/// Recommended that you put this on a 1-10 second timer
/// </summary>
public unsafe class RandomMovementSystem : MonoBehaviour, ISystem
{
    [SerializeField] float _DistanceRange = 10;
    int seed = 0;

    public void Tick(ParallelSpreadSheet sheet)
    {
        var moveIntention = sheet.GetSparseSet<MoveIntention>((int)ComponentCode.MoveIntention);

        for (int i = 0; i < moveIntention.Count; i++)
        {
            float3 newPos = Rand.UnitCircle(seed) * _DistanceRange;
            moveIntention.data[i] = new MoveIntention(newPos);
            seed++;
        }
    }
}
