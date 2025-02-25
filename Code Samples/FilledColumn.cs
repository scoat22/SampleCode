using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text; // for StringBuilder

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public unsafe class FilledColumn : IColumn
{
    public FilledColumn(TypeInfo ti)
    {
        data = null;
        _nEntities = null;
        _Capacity = 0;

        //Debug.LogFormat("Initializing filled column <b>{0}</b>, size: {1}, align: {2}", ti.type.Name, ti.size, ti.alignment);
        this.ti = ti;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = AtomicSafetyHandle.Create();
        // Set the safety ID on the AtomicSafetyHandle so that error messages describe this container type properly.
        AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId);
#endif 
        _nEntities = (int*)UnsafeUtility.MallocTracked(1 * sizeof(int), UnsafeUtility.AlignOf<int>(), Allocator.Persistent, 1);
        *_nEntities = 0;
    }

    public void Dispose()
    {
        UnsafeUtility.FreeTracked(data, Allocator.Persistent);
        UnsafeUtility.FreeTracked(_nEntities, Allocator.Persistent);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
        AtomicSafetyHandle.Release(m_Safety);
#endif
    }

    /// <summary>
    /// Adds space to each column
    /// </summary>
    /// <returns>EntityID of the first added entity (aka an "offset". The rest are after)</returns>
    public void PushEntities(int nEntities)
    {
        PrePushEntities(nEntities);
        *_nEntities += nEntities; // Increase count
    }

    /// <summary>
    /// The inverse of Push; removes the top N entities (mainly here for undoing)
    /// </summary>
    /// <param name="nEntities"></param>
    public void PopEntities(int nEntities)
    {
        *_nEntities -= nEntities; // Subtract from count
    }

    // In essence we're creating a new buffer, by "zippering" together the existing data and input data
    public void Insert_Entities_Sorted(NativeArray<int> ids)
    {
        ColumnUtility.Insert(ids, ref data, ref _Capacity, ref *_nEntities, ti);
    }

    /// <summary> Remove entities at random </summary>
    /// <param name="garbage_ids">The set of garbage EntityIds to be removed</param>
    public void Extract_Entities_Sorted(NativeArray<int> garbage_ids)
    {
        if(ti.type == typeof(EntityId))
        {
            throw new NotImplementedException("Need to implement FilledColumn.Extract for <EntityIds>");
        }
        //Debug.LogFormat("Extracting {0} entities", garbage_ids.Length); // this is 0
        _Capacity -= garbage_ids.Length;
        byte* dest = ColumnUtility.MallocTracked(_Capacity, ti);
        if (ti.ClearMemory) ColumnUtility.ClearMemory(dest, 0, _Capacity, ti);
        int size = ti.size;
        int n = 0;
        int gi = 0; // garbage indexer
        int max = *_nEntities;
        for (EntityId id = 0; id < max; id++)
        {
            if (gi < garbage_ids.Length &&  // Range check
                garbage_ids[gi] == id)      // Is this id being removed?
            {
                //Debug.Log("FilledColumn skipping entity");
                gi++; // id being removed; skip
            }
            else
            {
                // Note: For debugging the value (since we don't have access to an actual debugger).
                /*IntPtr ptr = (IntPtr)data + id * ti.size;
                object o = Marshal.PtrToStructure(ptr, ti.type);
                Debug.LogFormat("Copying data src: {0} -> {1} :dest, Data: {2}", id, n, o.ToString());*/

                UnsafeUtility.MemCpy(
                    dest + (size * n),
                    data + (size * id),
                    size);
                n++;
            }
        }
        UnsafeUtility.FreeTracked(data, Allocator.Persistent); // Dispose old buffer and swap
        data = dest;
        *_nEntities -= garbage_ids.Length;
    }
    
    // Just for reference; this is half fixed
    /*public override void Extract_Entities_Sorted(NativeArray<int> garbage_ids)
    {
        // Why do we have a dictionary? We can just use an array int[].
        var oldToNew = new NativeArray<int>(data.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

        var temp = new NativeArray<EntityId>(_Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory); //- garbageIDs.Count()
        //Debug.Log("Old length: " + _Count + ", new length: " + temp.Length);
        // Add everything to temp, except the garbage
        for (EntityId id = 0, id2 = 0; id < _Count; id++)
        {
            // keep track of the old and new ID
            oldToNew[id] = id2;
            if (!garbage_ids.Contains(new EntityId(id)))
            {
                temp[id2] = _Data[id];
                id2++;
            }
        }
        *count -= garbage_ids.Length;

        // Now we update all values (since things were shifted)
        for (int i = 0; i < *count; i++)
        {
            EntityId id = temp[i];
            temp[i] = new EntityId(oldToNew[id]);
            //Debug.Log(ID + "->" + oldToNew[ID]);
        }

        oldToNew.Dispose();
        data.Dispose();
        _Data = temp;
    }*/

    /*public void Extract_Entities_Sorted_ShiftIds(NativeArray<int> garbage_ids)
    {
        // Need to recreate the arrays
        int capacity = _Capacity - garbage_ids.Length;
        IntArray sparse2 = new IntArray(capacity);    // Allocate new buffers
        IntArray dense2 = new IntArray(capacity);
        EntityId* dest = (EntityId*)ColumnUtility.MallocTracked(capacity, ti);
        EntityId* src = (EntityId*)data;
        // Clear the memory
        if (ti.ClearMemory) ColumnUtility.ClearMemory((byte*)dest, 0, capacity, ti);
        int size = ti.size;
        int gi = 0; // garbageId indexer
        EntityId id2 = 0; // new entity Ids

        // Type is EntityId
        // Keep a mapping of old ids to new ids (doesn't need to be a dictionary)
        var oldToNew = new NativeArray<int>(nEntities, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        
        // Add everything to temp, except the garbage
        for (EntityId id = 0; id < nEntities; id++) 
        {
            // keep track of the old and new ID
            oldToNew[id] = id2;

            if (gi < garbage_ids.Length &&
                id == garbage_ids[gi])
            {
                gi++; // Removing id
            }
            else
            {
                // Keeping id
                dest[id2] = data[id]; // copy data
                id2++;
            }
        }
        *_nEntities -= garbage_ids.Length;

        // Now we update all values (since things were shifted)
        for (i = 0; i < nEntities; i++)
        {
            EntityId id = dest[i];
            dest[i] = new EntityId(oldToNew[id]); // old working code

            UnsafeUtility.MemCpy(
                        dest + (size * i),            // destination
                        data + (size * oldToNew[id]), // source
                        size);

            Debug.LogFormat("{0} -> {1}", id, oldToNew[id]);
        }
        oldToNew.Dispose();

        // Dispose old buffers and swap
        _Capacity = capacity;

        // Data swap
        UnsafeUtility.FreeTracked(data, Allocator.Persistent);
        data = (byte*)dest;
    }*/

    /// <summary>
    /// Allocate space without incrementing '_NumEntities'. Use if you know how many entities you'll be adding before the next
    /// GetColumn call (The pointer will be gauranteed to not go stale until then, assuming you did your math right). This gaurantees
    /// you'll only have one "if statement" and "array retreival" at the beginning of your operation.
    /// </summary>
    // Just increase the capacity (don't increase count)
    public void PrePushEntities(int nEntities)
    {
        int old_capacity = _Capacity;
        if (ColumnUtility.EnsureAdditionalCapacity(
            buf: ref data,
            capacity: ref _Capacity,
            nEntities: Count,
            nAdd: nEntities,
            ti))
        {
/*#if UNITY_EDITOR
            if (_DebugName == "Position")
            {
                Debug.LogFormat("<color=lime>Resizing Position {0} -> {1}</color>", old_capacity, _Capacity);
            }
#endif*/
        }
    }

    public bool NeedToReallocate(int nEntities) => Count + nEntities >= _Capacity;

    /// <summary>
    /// Clears the memory
    /// </summary>
    /*public void ResetColumn()
    {
        ClearMemory(_Buffer, 0, _Capacity);
    }*/

    public IEntityIter GetEntities() => new FilledColumnEntityIter(this);

    public unsafe struct FilledColumnEntityIter : IEntityIter
    {
        public FilledColumnEntityIter(FilledColumn column)
        {
            _Index = 0;
            _Count = column.Count;
        }
        public bool Next(out EntityId id)
        {
            id = _Index;
            return _Index < _Count;
        }
        private int _Index;
        private int _Count;
        public int Count => _Count;
    }

    public IComponentIter<T> GetIter<T>() where T : unmanaged
    {
        return new FilledColumnIter<T>(this);
    }

    public unsafe struct FilledColumnIter<T> : IComponentIter<T> where T : unmanaged
    {
        public FilledColumnIter(FilledColumn column)
        {
            _Index = 0;
            _Iter = (T*)column.data;
            _Count = column.Count;
        }
        public bool Next(out int id, out T* value)
        {
            id = _Index;
            value = _Iter;
            _Index++;
            _Iter++;
            return _Index < _Count;
        }
        private int _Index;
        private T* _Iter;
        private int _Count;
    }

    public T Get<T>(int entityId) where T : unmanaged
    {
#if UNITY_EDITOR
        if (entityId < 0 || entityId >= _Capacity)
        {
            Debug.LogErrorFormat("Get() failed! entityId was out of bounds of capacity <b>{0}</b>", _Capacity);
            return default;
        }
#endif
        return ((T*)data)[entityId];
    }

    /// <summary>
    /// Remember to always cast! Eg. Set(id, (byte)value) (In general just cast). It could lead to freezing if you pass the wrong type.
    /// The problem with C# is that it always wants to implicitely cast everything. So just remember to always explicitly cast.
    /// </summary>
    public void Set<T>(int entityId, T value) where T : unmanaged
    {
#if UNITY_EDITOR
        if (entityId < 0 || entityId >= _Capacity)
        {
            Debug.LogErrorFormat("Set() failed! entityId was out of bounds of capacity <b>{0}</b>", _Capacity);
            return;
        }
#endif
        ((T*)data)[entityId] = value;
    }

    public void Clear()
    {
        *_nEntities = 0; // Just set the count to 0
    }

    public Span<byte> GetColumnRawData()
    {
        return new Span<byte>(data, Count * ti.size);
    }

    public override string ToString()
    {
        try
        {
            const int maxChars = 19;
            StringBuilder sb = new StringBuilder();
            int start = 0;
            Span<byte> bytes = GetColumnRawData();  // Get column data
            var toString = ti.type.GetMethods()     // Get ToString method
                .Where(x => x.Name == "ToString")
                .FirstOrDefault();

            int byteIter = start * ti.size;
            int max = Count;
            for (int i = start; i < max; i++)
            {
                // Get the data for each cell and print it (call ToString())
                fixed (byte* p = bytes.Slice(byteIter, ti.size))
                {
                    object cell_instance = Marshal.PtrToStructure((IntPtr)p, ti.type);  // Marshal to managed memory (copies)
                    string str = (string)toString.Invoke(cell_instance, null);          // Convert to string
                    if (str.Length > maxChars) str = str.Substring(0, maxChars) + "...";// Cutoff if too long
                    sb.Append(str);
                    if (i != Count - 1) sb.Append("\n");
                }
                byteIter += ti.size;
            }
            /*for (int i = 0; i < _Count; i++)
            {
                string str = _Buffer[i].ToString();
                if (str.Length > maxChars) str = str.Substring(0, maxChars) + "...";// Cutoff if too long
                sb.Append(str);
                if (i != _Count - 1) sb.Append("\n");
            }*/
            return sb.ToString();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        return "Error";
    }

    /// <summary>
    /// Instant access, zero copy
    /// </summary>
    public NativeArray<T> AsArray<T>(Allocator allocator = Allocator.TempJob) //= Allocator.None) 
        where T : unmanaged
    {
        //Debug.LogFormat("Got NativeArray<<b>{0}</b>> with capacity <b>{1}</b>", typeof(T), data.capacity);

        // Create a new NativeArray which aliases the buffer, using the current size. This doesn't allocate or copy
        // any data, it just sets up a NativeArray<T> which points at the m_Buffer.
        NativeArray<T> array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(data, _Capacity, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
        // Test: copy the safety handle
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
#endif
        return array;
        // Note: Maybe check if it is the specified type here.
        //Debug.LogErrorFormat("Column '<b>{0}</b>' wasn't Column<<b>{1}</b>>", column, nameof(T));
    }

    /// <summary>
    /// Read-only (Uses Count instead of Capacity (doesn't pass garbage))
    /// </summary>
    public NativeArray<T> AsArrayRO<T>(Allocator allocator = Allocator.None) where T : unmanaged
    {
        NativeArray<T> array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(data, Count, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
        return array;
    }

    public NativeLimitedList<T> AsLimitedList<T>() where T : unmanaged => new NativeLimitedList<T>(this);

    private byte* MallocTracked(int nEntities) => (byte*)UnsafeUtility.MallocTracked(nEntities * ti.size, ti.alignment, Allocator.Persistent, 1);

    // Return size of the component array for tracking memory usage (in bytes)
    public ulong SizeOf()
    {
        return sizeof(int) * 2 + (ulong)ti.size * (ulong)_Capacity;
    }

    public void AddComponent(EntityId id)
    {
        // does nothing; all entities already have the component
    }

    public void RemoveComponent(EntityId id)
    {
        // does nothing; all entities already have the component
    }

    public bool HasComponent(EntityId id) => true;

    /// <summary>
    /// nEntities
    /// </summary>
    public int Count => *_nEntities;
    public int nEntities => *_nEntities;

    public byte* data;
    /// <summary>
    /// Basically equal to a "count"
    /// </summary>
    internal int* _nEntities;
    internal int _Capacity;
    public TypeInfo ti;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
    // Statically register this type with the safety system, using a name derived from the type itself
    internal static readonly int s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<FilledColumn>();
#endif
}
