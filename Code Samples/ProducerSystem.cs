using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SpreadSheetNS.Parallel;

public unsafe class ProducerSystem : MonoBehaviour, ISystem
{
    [SerializeField] private ComponentCode producerComponent;
    [SerializeField] private ComponentCode productComponent;
    [SerializeField] private SpriteCode spriteId;

    public void Tick(ParallelSpreadSheet sheet)
    {
        // Check how many producers there are
        NativeSparseSet producers = sheet.GetSparseSet((int)producerComponent);
        int nEntities = producers.Count;

        // For every producer, potentially spawn 1 product.
        // So make sure we have enough room in the database.
        // (add entities *before* getting component arrays; they could become stale)
        sheet.PreAddEntities(nEntities);

        // Need to get producers again, since the pointer may have changed. 
        producers = sheet.GetSparseSet((int)producerComponent);
        NativeSparseSet           products    = sheet.GetSparseSet((int)productComponent);
        NativeArray<float3>       position    = sheet.GetArray<float3>((int)ComponentCode.Position);
        NativeSparseSet<float3>   velocity    = sheet.GetSparseSet<float3>((int)ComponentCode.Velocity);
        NativeArray<byte>         sprite      = sheet.GetArray<byte>((int)ComponentCode.Sprite);
        NativeArray<byte>         size        = sheet.GetArray<byte>((int)ComponentCode.Size);
        NativeSparseSet<short>    health      = sheet.GetSparseSet<short>((int)ComponentCode.Health);
        NativeSparseSet<EntityId> pullsEntity = sheet.GetSparseSet<EntityId>((int)ComponentCode.PullsEntity);

        int prev_nEntities = sheet.nEntities;
        for (int i = 0; i < nEntities; i++)
        {
            EntityId producerId = producers.dense[i];
            AddItem(sheet, products, spriteId, producerId, position, velocity, health, pullsEntity, sprite, size);
        }
    }

    /// <summary>
    /// Adds an item to the owner (adds to the end of the chain)
    /// </summary>
    /// <returns> 
    /// 0 = success,
    /// -1 = max depth reached (chain too long)
    /// </returns>
    int AddItem(ParallelSpreadSheet sheet, NativeSparseSet itemComponent, SpriteCode spriteId, EntityId ownerId,
        NativeArray<float3> position, NativeSparseSet<float3> velocity, NativeSparseSet<short> health,
        NativeSparseSet<EntityId> pullsEntity,
        NativeArray<byte> sprite, NativeArray<byte> size, int maxDepth = Ownership.MaxDepth)
    {
        // Make the producer pull this product
        EntityId currentId = ownerId;
        bool produce = false;
        // Attach to the final item on the chain
        for (int depth = 0; depth < maxDepth; depth++)
        {
            if (pullsEntity.HasComponent(currentId))
            {
                // Go to the next link on the chain
                currentId = pullsEntity[currentId];
                //Debug.LogFormat("next Id is {0}", currentId);
            }
            else
            {
                // Found the last link in the chain
                produce = true;
                break;
            }
        }

        if (produce)
        {
            sheet.AddEntities(1, out EntityId productId);
            // currentId represents the last item in the chain
            pullsEntity.AddComponent(currentId);
            pullsEntity[currentId] = productId;

            sprite[productId] = (byte)spriteId;
            size  [productId] = (byte)Space.ItemSize;

            // Add velocity
            velocity.AddComponent(productId);
            // Add the specified item component
            itemComponent.AddComponent(productId);
            health.AddComponent(productId, Health.DefaultItemHealth);

            // Make the item right on top of the last item in the chain
            float3 newPos = position[currentId];

            // Set the position close to the producer
            position[productId] = newPos;
            return 0;
        }
        return -1;
    }
}
