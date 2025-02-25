using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

using static Unity.Mathematics.math;

/// <summary>
/// A sparse set implementation
/// </summary>
public unsafe class SparseColumn : IColumn
{

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
    // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
    // The job cannot dispose the container, and no one else can dispose it until the job has run, so it is ok to not pass it along
    // This attribute is required, without it this NativeContainer cannot be passed to a job; since that would give the job access to a managed object
    [NativeSetClassTypeToNullOnSchedule]
    private DisposeSentinel m_DisposeSentinel;
#endif

    public SparseColumn(Allocator label = Allocator.Persistent)
    {
        _AlignOfInt = UnsafeUtility.AlignOf<int>();
        dense = new IntArray(null, 0, 0);
        sparse = new IntArray(null, 0, 0);
        count = null;
        _Capacity = 0;
        _nEntities = 0;
#if Component_Tracking
        version = 0;
#endif
        // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, label);
#endif
        count = MallocTracked(1);
        *count = 0;
    }

    public static SparseColumn Copy(SparseColumn source)
    {
        var dest = new SparseColumn();
        dest.PushEntities(source._nEntities);
        UnsafeUtility.MemCpy(dest.dense.buffer, source.dense.buffer, sizeof(int) * source.Count);
        UnsafeUtility.MemCpy(dest.sparse.buffer, source.sparse.buffer, sizeof(int) * source._nEntities);
        *dest.count = *source.count;
        return dest;
    }

    // This is where we allocate memory
    protected virtual bool EnsureAdditionalCapacity(int nEntities)
    {
        // Check current capacity first
        if (_nEntities + nEntities > _Capacity)
        {
            if (NeedToReallocate(nEntities))
            {
                int new_capacity = ColumnUtility.CalculateNewCapacity(_Capacity, _nEntities, nEntities);
                //Debug.LogFormat("Resized sparse column: <b>{0}</b> -> <b>{1}</b>", _Capacity, new_capacity);
                // Set Sparse
                {
                    int* newSparse = MallocTracked(new_capacity);
                    // Need to use capacity here because entityId=n-1 might be the only one with a component
                    UnsafeUtility.MemCpy(newSparse, sparse.buffer, _Capacity * sizeof(int));
                    UnsafeUtility.FreeTracked(sparse.buffer, Allocator.Persistent);
                    sparse = new IntArray(newSparse, Count, new_capacity); // Swap the buffer
                }
                // Set Dense
                {
                    int* newDense = MallocTracked(new_capacity);
                    UnsafeUtility.MemCpy(newDense, dense.buffer, Count * sizeof(int));
                    UnsafeUtility.FreeTracked(dense.buffer, Allocator.Persistent);
                    dense = new IntArray(newDense, Count, new_capacity); // Swap the buffer
                }
                _Capacity = new_capacity;
                return true;
            }
        }
        return false;
    }

    public virtual void Dispose()
    {
        UnsafeUtility.FreeTracked(dense.buffer, Allocator.Persistent);
        UnsafeUtility.FreeTracked(sparse.buffer, Allocator.Persistent);
        UnsafeUtility.FreeTracked(count, Allocator.Persistent);
        // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
        _Capacity = 0;
        *count = 0;
    }

    /// <param name="nEntities">Number of entities that we're planning on adding</param>
    public bool NeedToReallocate(int nEntities) => _nEntities + nEntities >= _Capacity;

    /// <summary>
    /// Allocate space without incrementing '_NumEntities'. Use if you know how many entities you'll be adding before the next
    /// GetColumn call (The pointer will be gauranteed to not go stale until then, assuming you did your math right). This gaurantees
    /// you'll only have one "if statement" and "array retreival" at the beginning of your operation.
    /// </summary>
    // Just increase the capacity (don't increase count)
    public void PrePushEntities(int nEntities) => EnsureAdditionalCapacity(nEntities);

    /// <summary>
    /// Adds space to each column
    /// </summary>
    /// <returns>EntityID of the first added entity (aka an "offset". The rest are after)</returns>
    public void PushEntities(int nEntities)
    {
        // Just make more room (Imagine we're adding room for more vertices to be selected)
        PrePushEntities(nEntities);
        _nEntities += nEntities;
    }

    /// <summary>
    /// The inverse of Push; removes the top N entities (mainly here for undoing)
    /// </summary>
    /// <param name="nEntities"></param>
    public void PopEntities(int nEntities)
    {
        _nEntities -= nEntities;
    }

    // In essence we're creating a new buffer, by "zippering" together the existing data and input data
    public void Insert_Entities_Sorted(NativeArray<int> ids)
    {
        _nEntities += ids.Length;
        throw new NotImplementedException();
    }

    /// <summary> Remove entities at random </summary>
    /// <param name="garbage_ids">The set of garbage EntityIds to be removed</param>
    public virtual void Extract_Entities_Sorted(NativeArray<int> garbage_ids)
    {
        // Need to recreate the arrays
        int capacity = _Capacity - garbage_ids.Length;
        int n = 0; // new count
        IntArray sparse2 = new IntArray(capacity);
        IntArray dense2 = new IntArray(capacity);
        EntityId gi = 0; // garbageId indexer
        EntityId id2 = 0;
        for (EntityId id = 0; id < _nEntities; id++)
        {
            if (gi < garbage_ids.Length &&   // Range check
                garbage_ids[gi] == id)       // If this index is being removed
            {
                // (we're checking each garbageId for a match to this index)
                gi++;
            }
            else
            {
                // This index isn't being removed, so just copy this location
                // If it has the component
                if (HasComponent(id))
                {
                    // Add component
                    dense2[n] = id2;
                    sparse2[id2] = n;
                    n++;
                }
                id2++;
            }
        }
        // Dispose old buffer and swap
        sparse.Dispose();
        dense.Dispose();
        // Set new values
        sparse = sparse2;
        dense = dense2;
        *count = n;
        _Capacity = capacity;
        _nEntities -= garbage_ids.Length;
    }

    public void AddComponent(EntityId id)
    {
        if (id >= _Capacity)
        {
            Debug.LogErrorFormat("AddComponent: id ({0}) exceeded capacity ({1})", id, _Capacity);
            return;
        }
        if (Count >= _Capacity)
        {
            Debug.LogErrorFormat("AddComponent: n ({0}) exceeded capacity ({1})", Count, _Capacity);
            return;
        }
        if (Search(id) != -1)
        {
            Debug.LogErrorFormat("AddComponent: id <b>{0}</b> already had component", id);
            return;
        }
        dense[Count] = id;
        sparse[id] = Count;
        //Debug.LogFormat("Added component to {0} (Capacity: {1}, Count: {2})", id, _Capacity, Count);

        (*count)++;
#if Component_Tracking
        version++;
#endif
    }

    public virtual void RemoveComponent(EntityId id)
    {
        // If id is not present
        if (Search(id) == -1)
            return;

        int temp = dense[Count - 1]; // Take an element from end
        dense[sparse[id]] = temp;    // Overwrite
        sparse[temp] = sparse[id];   // Overwrite   
        (*count)--;
#if Component_Tracking
        version++;
#endif
    }

    public bool HasComponent(EntityId id) => Search(id) != -1;

    public int Search(EntityId id)
    {
        // Both dense/sparse should have size _Capacity right now.
        // Check range to avoid indexing with garbage values, accessing out-of-range memory
        if (id >= _Capacity)
        {
            //Debug.LogErrorFormat("Search: id ({0}) exceeded capacity ({1})", id, _Capacity);
            return -1;
        }
        int s = sparse[id];

        if (s >= _Capacity)
        {
            //Debug.LogErrorFormat("Search: s {0} exceeded capacity {1}", s, _Capacity);
            return -1;
        }
        if (s < 0)
        {
            //Debug.LogErrorFormat("Search: s {0} subverts 0", s);
            return -1;
        }

        if (s >= Count)
        {
            //Debug.LogFormat("Search: s ({0}) was greater than n ({1})", s, _Capacity);
            return -1;
        }

        // Must be contained in the set, and both dense and sparse must point to eachother
        if (s < Count && dense[s] == id)
            return s;

        // Not found
        return -1;
    }

    public IEntityIter GetEntities() => new SparseEntityIter(this);

    public struct SparseEntityIter : IEntityIter
    {
        public SparseEntityIter(SparseColumn column)
        {
            _Dense = column.dense;
            _Count = column.Count;
            _Index = 0;
        }
        public bool Next(out EntityId entityId)
        {
            entityId = _Dense[_Index];
            _Index++;
            return _Index < _Count;
        }
        private IntArray _Dense;
        private int _Count;
        private int _Index;
        public int Count => _Count;
    }

    /// <summary>
    /// Converts the dense array to a NativeArray (so that it can be used with jobs)
    /// </summary>
    /// <returns></returns>
    public NativeArray<int> GetDenseArray()
    {
        //Debug.LogFormat("Got NativeArray<<b>{0}</b>> with capacity <b>{1}</b>", typeof(T), data.capacity);
        NativeArray<int> array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(dense.buffer, Count, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
        return array;
    }

    public NativeArray<int> GetSparseArray(Allocator allocator = Allocator.None)
    {
        //Debug.LogFormat("Got NativeArray<<b>{0}</b>> with capacity <b>{1}</b>", typeof(T), data.capacity);
        NativeArray<int> array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(sparse.buffer, _nEntities, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
        return array;
    }

    public NativeSparseSet ToNativeSparseSet() => new NativeSparseSet(this);

    /// <summary>
    /// Pass 1-4 sparse columns
    /// </summary>
    /// <returns>The EntityId intersection between each column</returns>
    public static SparseColumn Intersection(List<SparseColumn> columns)
    {
        switch (columns.Count)
        {
            case 0: return new SparseColumn();  // return an empty set
            case 1: return Copy(columns[0]);    // make a copy of the first column
            case 2: return Intersection(columns[0], columns[1]);
            case 3: return Intersection(columns[0], columns[1], columns[2]);
            case 4: return Intersection(columns[0], columns[1], columns[2], columns[3]);
            default:
                Debug.LogError("SparseColumn.Intersection Error! Passed column count outside of 1-4 range");
                break;
        }
        return null;
    }

    public static SparseColumn Intersection(SparseColumn a, SparseColumn b)
    {
        // Capacity and max value of result set
        int iMaxVal = max(b._Capacity, a._Capacity);

        // Create result set
        SparseColumn result = new SparseColumn();
        result.PrePushEntities(iMaxVal);

        // Find the smaller of two sets
        // If this set is smaller
        if (a.Count < b.Count)
        {
            // Search every element of this set in 's'.
            // If found, add it to result
            for (int i = 0; i < a.Count; i++)
                if (b.Search(a.dense[i]) != -1)
                    result.AddComponent(a.dense[i]);
        }
        else
        {
            // Search every element of 's' in this set.
            // If found, add it to result
            for (int i = 0; i < b.Count; i++)
                if (a.Search(b.dense[i]) != -1)
                    result.AddComponent(b.dense[i]);
        }
        return result;
    }

    public static SparseColumn Intersection(SparseColumn a, SparseColumn b, SparseColumn c)
    {
        SparseColumn ab = Intersection(a, b);
        SparseColumn abc = Intersection(ab, c);
        ab.Dispose();
        return abc;
    }

    public static SparseColumn Intersection(SparseColumn a, SparseColumn b, SparseColumn c, SparseColumn d)
    {
        SparseColumn ab = Intersection(a, b);
        SparseColumn cd = Intersection(c, d);
        SparseColumn abcd = Intersection(ab, cd);
        ab.Dispose();
        cd.Dispose();
        return abcd;
    }

    public void Clear()
    {
        // Just set the count to 0
        *count = 0;
        //count = 0;
    }

    public override string ToString()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _nEntities; i++)
            {
                if (HasComponent(i)) sb.Append("x");
                if (i != _nEntities - 1) sb.Append("\n");
            }
            return sb.ToString();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        return "Error";
    }

    private int* MallocTracked(int length) => (int*)UnsafeUtility.MallocTracked(length * sizeof(int), _AlignOfInt, Allocator.Persistent, 1);

    public virtual ulong SizeOf()
    {
        return (ulong)(sizeof(int) * 3 + sizeof(int) * _Capacity);
    }

    public int Count => *count;

    /// <summary>
    /// packed entityIds (dense[i] = entityId (or sparseIndex))
    /// </summary>
    public IntArray dense; // Dense array
    /// <summary>
    /// Indexes into dense array (sparse[entityId] = denseIndex)
    /// </summary>
    public IntArray sparse; // Sparse array
    /// <summary>
    /// The number of entities with components
    /// </summary>
    internal int* count;
    /// <summary>
    /// The total number of entities
    /// </summary>
    internal int _nEntities;
    /// <summary>
    /// Capacity of both arrays (max entities)
    /// </summary>
    public int _Capacity;
    protected static int _AlignOfInt;
#if Component_Tracking
    public uint version;
#endif
}
