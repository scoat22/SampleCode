using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace SpreadSheetNS
{
    /// <summary>
    /// Basically a "ComponentManager" or "ColumnManager"
    /// </summary>
    public unsafe class SpreadSheet : IDisposable
    {
        public const int MaxEntities = 100000;

        /// <param name="capacity">Initial capacity; number of columns</param>
        public SpreadSheet(int capacity)
        {
            _Columns = new IColumn[capacity];
            _nEntities = 0;
            signature = new Signature();
        }

        public SpreadSheet(SpreadsheetPrefab prefab)
        {
            // Calculate the capacity
            int maxId = 0;
            for (int i = 0; i < prefab.columns.Length; i++)
                maxId = max(maxId, prefab.columns[i].id);
            int capacity = maxId + 1; // Need 1 more space (fix off by 1 error)

            _Columns = new IColumn[capacity];
            _nEntities = 0;

            signature = prefab.signature;
            for (int i = 0; i < prefab.columns.Length; i++)
            {
                ColumnPrefab column = prefab.columns[i];
                ColumnId id = column.id;
                TypeInfo ti = column.type;

                switch (column.columnType)
                {
                    case ColumnTypeCode.Filled: AddFilledColumn(id, ti); break;
                    case ColumnTypeCode.Sparse: AddSparseColumn(id); break;
                    case ColumnTypeCode.SparseWithData: AddSparseColumnWithData(id, ti); break;
                    case ColumnTypeCode.SparseManaged: AddSparseManagedColumn(id); break;
                    default:
                        Debug.LogFormat("Unsupported columnTypeId '{0}'", column.columnType);
                        break;
                }
            }
            AddEntities(0);
        }

        public void Dispose()
        {
            Clear();
        }

        public static SpreadSheet Copy(SpreadSheet source)
        {
            SpreadSheet dest = new SpreadSheet(source._Columns.Length); // Copy capacity from source
            return Copy(source, dest);
        }

        public static SpreadSheet Copy(SpreadSheet source, SpreadSheet dest)
        {
            if (source._Columns.Length == dest._Columns.Length)
            {
                dest.signature = source.signature;
                dest.Clear(); // Make sure it has nothing in it
                dest.AddEntities(source.nEntities);

                // Create columns from source
                foreach (int columnId in source.GetColumns())
                {
                    IColumn column = source._Columns[columnId];

                    switch (column)
                    {
                        case FilledColumn filled_source:
                            {
                                // Add the column
                                FilledColumn filled_dest = dest.AddFilledColumn(columnId, filled_source.ti);
                                // Copy from source to dest
                                UnsafeUtility.MemCpy(filled_dest.data, filled_source.data, filled_source.ti.size * source.nEntities);
                                //filled_dest._Capacity = filled_source._Capacity; // We shouldn't need this
                            }
                            break;
                        case SparseColumnWithData spData_source:
                            {
                                // Add the column
                                SparseColumnWithData spData_dest = dest.AddSparseColumnWithData(columnId, spData_source.ti);
                                // Copy from source to dest
                                UnsafeUtility.MemCpy(spData_dest.dense.buffer, spData_source.dense.buffer, sizeof(int) * spData_source.Count);      // just copy important part of dense
                                UnsafeUtility.MemCpy(spData_dest.sparse.buffer, spData_source.sparse.buffer, sizeof(int) * spData_source._Capacity); // copy entire sparse
                                UnsafeUtility.MemCpy(spData_dest.data, spData_source.data, spData_source.ti.size * spData_source.Count);
                                UnsafeUtility.MemCpy(spData_dest.count, spData_source.count, sizeof(int));
                                //spData_dest.data_capacity = spData_source.data_capacity; // We shouldn't need this
                            }
                            break;
                        case SparseColumn sparse_source:
                            {
                                // Add the column
                                SparseColumn sparse_dest = dest.AddSparseColumn(columnId);
                                // Copy from source to dest
                                if (sparse_dest.sparse.buffer == null) Debug.LogError("Null dest sparse buffer");
                                if (sparse_source.sparse.buffer == null) Debug.LogError("Null source sparse buffer");
                                UnsafeUtility.MemCpy(sparse_dest.dense.buffer, sparse_source.dense.buffer, sizeof(int) * sparse_source.Count);      // just copy important part of dense
                                UnsafeUtility.MemCpy(sparse_dest.sparse.buffer, sparse_source.sparse.buffer, sizeof(int) * sparse_source._Capacity); // copy entire sparse
                                UnsafeUtility.MemCpy(sparse_dest.count, sparse_source.count, sizeof(int));
                            }
                            break;
                        default:
                            Debug.LogFormat("Column type '{0}' doesn't support Copy()", column.GetType().Name);
                            // unsupported column type 
                            break;
                    }
                }
                // Very specific test
                //Debug.Assert(dest.GetSparseColumn(0).HasComponent(0), "Entity0 wasn't selected in copy SpreadSheet");
                return dest;
            }
            else
            {
                Debug.LogErrorFormat("Copy() failed! Column lengths not equal (source: {0}, destination: {1})", source._Columns.Length, dest._Columns.Length);
                return null;
            }
        }

        /// <summary>
        /// Are all the saved values the same? (Mostly for testing purposes. Obviously this operation is very slow)
        /// </summary>
        public static bool Equals(SpreadSheet a, SpreadSheet b)
        {
            if (a._ColumnSet.Count == b._ColumnSet.Count)
            {
                foreach (int columnId in a.GetColumns())
                {
                    IColumn column = a._Columns[columnId];

                    switch (column)
                    {
                        case FilledColumn filled_a:
                            FilledColumn filled_b = b.GetFilledColumn(columnId);
                            if (filled_b != null)
                            {
                                if (0 != UnsafeUtility.MemCmp(filled_a.data, filled_b.data, a.nEntities * filled_a.ti.size))
                                {
                                    // filled columns not equal
                                    Debug.LogFormat("FilledColumns are not equal (columnId: {0}).", columnId);
                                    return false;
                                }
                            }
                            else
                            {
                                // filled column null
                                Debug.Log("FilledColumn of SpreadSheet B was null!");
                                return false;
                            }
                            break;
                        case SparseColumnWithData spData_a:
                            SparseColumnWithData spData_b = b.GetSparseColumnWithData(columnId);
                            if (spData_b != null)
                            {
                                if (spData_a.Count != spData_b.Count)
                                {
                                    // count not equal
                                    Debug.LogFormat("Counts of SparseColumnsWithData are not equal (columnId: {0}).", columnId);
                                    return false;
                                }
                                if (0 != UnsafeUtility.MemCmp(spData_a.dense.buffer, spData_b.dense.buffer, sizeof(int) * spData_a.Count))
                                {
                                    // dense columns not equal
                                    Debug.LogFormat("Dense arrays of SparseColumnsWithData are not equal (columnId: {0}).", columnId);
                                    return false;
                                }
                                // We need to test sparse too, to be 100% accurate... but I'm so lazy...
                                if (0 != UnsafeUtility.MemCmp(spData_a.data, spData_b.data, spData_a.ti.size * spData_a.Count))
                                {
                                    // data columns not equal
                                    Debug.LogFormat("Data arrays of SparseColumnsWithData are not equal (columnId: {0}).", columnId);
                                    return false;
                                }
                            }
                            else
                            {
                                // column null
                                Debug.Log("SparseColumnWithData of SpreadSheet B was null!");
                                return false;
                            }
                            break;
                        case SparseColumn sparse_a:
                            SparseColumn sparse_b = b.GetSparseColumn(columnId);
                            if (sparse_b != null)
                            {
                                if (sparse_a.Count != sparse_b.Count)
                                {
                                    // count not equal
                                    Debug.LogFormat("Counts of SparseColumn are not equal (columnId: {0}).", columnId);
                                    return false;
                                }
                                if (0 != UnsafeUtility.MemCmp(sparse_a.dense.buffer, sparse_b.dense.buffer, sizeof(int) * sparse_a.Count))
                                {
                                    // dense columns not equal
                                    Debug.LogFormat("Dense arrays of SparseColumn are not equal (columnId: {0}).", columnId);
                                    return false;
                                }
                            }
                            else
                            {
                                Debug.Log("SparseColumn of SpreadSheet B was null!");
                                return false;
                            }
                            break;
                        default:
                            Debug.LogFormat("Column type '{0}' doesn't support Equals() (columnId: {1})", column.GetType().Name, columnId);
                            // unsupported column type 
                            return false;
                    }
                }
            }
            else
            {
                Debug.Log("SpreadSheet nColumns are not equal!");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Copy the input EntityIDs from source to destination (in order)
        /// </summary>
        /*public static SpreadSheet Copy(SpreadSheet source, Ids entityIds)
        {
            Debug.LogError("Todo: implement SpreadSheet.Copy");
            //Debug.LogFormat("Copying Spreadsheet ({0} IDs)", entityIDs.Length);
            SpreadSheet dest = new SpreadSheet(source._Columns.Length); // Copy capacity from source

            // For each column in source
            foreach (int colId in source.GetColumns())
            {
                if (source.GetColumn(colId) is FilledColumn column)
                {
                    TypeInfo ti = source.Header.typeInfo[colId];
                    Span<byte> sourceBytes = column.GetColumnRawData();
                    Span<byte> destBytes = dest.AddColumn(colId, ti);

                    fixed (byte* destBuffer = destBytes)
                    {
                        fixed (byte* sourceBuffer = sourceBytes)
                        {
                            byte* dst = destBuffer;
                            for (int i = 0; i < entityIds.Length; i++)
                            {
                                // Copy specified entity from source
                                byte* src = sourceBuffer + entityIds[i] * ti.size;
                                UnsafeUtility.MemCpy(dst, src, ti.size);
                                dst += ti.size;  // Increment destination (packed)
                            }
                        }
                    }
                }
                else Debug.LogErrorFormat("Column {0} wasn't a filled column", colId);
            }
            return dest;
        }*/

        public int nEntities => _nEntities;

        /// <summary>
        /// Adds a filled column
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="initialCapacity"></param>
        public FilledColumn AddFilledColumn<T>(ColumnId id, bool clearMemory = false)//, byte* @default = null) 
            where T : unmanaged
        {
#if UNITY_EDITOR
            if (_Columns.Length > id)
            {
                if (_Columns[id] != null)
                {
                    Debug.LogFormat("Column {0} was already created", id);
                    return GetFilledColumn(id);
                }
            }
            else Debug.LogErrorFormat("Error: columnId <b>{0}</b> was greater than capacity <b>{1}</b>", id, _Columns.Length);
#endif
            // If no default value specified, just set it to the actual default or whatever its called. 
            //if(@default == null) @default = default;

            FilledColumn c = AddFilledColumn(id, new TypeInfo(
                type: typeof(T),
                alignment: UnsafeUtility.AlignOf<T>(),
                size: sizeof(T),
                ClearMemory: clearMemory,
                @default: default));
            return c;
        }

        public FilledColumn AddFilledColumn(ColumnId id, TypeInfo ti)
        {
            return (FilledColumn)AddColumn(new FilledColumn(ti), id);
        }

        /// <summary>
        /// Adds a sparse column
        /// </summary>
        public SparseColumn AddSparseColumn(ColumnId id)
        {
            DebugCheckColumnAccess(id);
            return (SparseColumn)AddColumn(new SparseColumn(), id);
        }

        /// <summary>
        /// Adds a spase column with data (<see cref="SparseColumnWithData"/>)
        /// </summary>
        public SparseColumnWithData AddSparseColumn<T>(ColumnId id, bool clear = false) where T : unmanaged
        {
            DebugCheckColumnAccess(id);
            SparseColumnWithData c = new SparseColumnWithData(new TypeInfo(
                type: typeof(T),
                alignment: UnsafeUtility.AlignOf<T>(),
                size: sizeof(T),
                ClearMemory: clear));
            return (SparseColumnWithData)AddColumn(c, id);
        }

        public SparseColumnWithData AddSparseColumnWithData(ColumnId id, TypeInfo ti)
        {
            return (SparseColumnWithData)AddColumn(new SparseColumnWithData(ti), id);
        }

        /// <summary>
        /// Adds a spase column with data, where the data is a fixed set of items of type T (UNTESTED)
        /// </summary>
        public SparseColumnWithData AddSparseFixedSetColumn<T>(int nItems, ColumnId id, bool clear = false) where T : unmanaged
        {
            DebugCheckColumnAccess(id);
            SparseColumnWithData c = new SparseColumnWithData(new TypeInfo(
                type: null, // Todo: untested
                alignment: UnsafeUtility.AlignOf<T>() * nItems,
                size: sizeof(T) * nItems,
                ClearMemory: clear));
            AddColumn(c, id);
            return c;
        }

        public SparseManagedColumn AddSparseManagedColumn(ColumnId id)
        {
            DebugCheckColumnAccess(id);
            SparseManagedColumn c = new SparseManagedColumn();
            AddColumn(c, id);
            return c;
        }

        void DebugCheckColumnAccess(ColumnId id)
        {
#if UNITY_EDITOR
            if (id >= _Columns.Length)
            {
                Debug.LogErrorFormat("Column id <b>{0}</b> was greater than SpreadSheet capacity <b>{1}</b> (Add more capacity in Spreadsheet constructor)", id, _Columns.Length);
                //if (_Columns[columnId] != null) Debug.LogFormat("Column {0} already exists", columnId);
            }
#endif
        }

        private IColumn AddColumn(IColumn c, ColumnId id)
        {
            if (id < 0 || id >= _Columns.Length)
            {
                Debug.LogErrorFormat("ColumnId {0} was out of range {1}", id, _Columns.Length);
                return null;
            }
            if (_Columns[id] != null)
            {
                //Debug.LogFormat("Column {0} already existed. Returning the column", columnId);
                return _Columns[id];
            }
            _Columns[id] = c;
            _ColumnSet.Add(id);
            c.PrePushEntities(MIN_COLUMN_CAPACITY); // Initialize to min_size
            // Columns must be initialized with however many entities there already are
            c.PushEntities(nEntities);
            return c;
        }

        public void RemoveColumn(ColumnId id)
        {
            //if (_Columns[columnId] != null)
            {
                _Columns[id].Dispose();
                _Columns[id] = null;        // Reset the column
                _ColumnSet.Remove(id);
            }
        }

        public bool TryGetColumn(ColumnId id, out IColumn column)
        {
            if (id > 0 && id < _Columns.Length)
            {
                column = _Columns[id];
                if (column == null)
                {
                    //Debug.LogWarningFormat("Null column at columnId: <b>{0}</b>", columnId);
                    return false;
                }
                return true;
            }
            column = null;
            return false;
        }

        public IColumn GetColumn(ColumnId id)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            RangeCheckColumn(id);
#endif
            return _Columns[id];
        }

        public FilledColumn GetFilledColumn(ColumnId id) => GetColumn<FilledColumn>(id);

        public SparseColumn GetSparseColumn(ColumnId id) => GetColumn<SparseColumn>(id);

        public SparseColumnWithData GetSparseColumnWithData(ColumnId id) => GetColumn<SparseColumnWithData>(id);

        public SparseManagedColumn GetSparseManagedColumn(ColumnId id) => GetColumn<SparseManagedColumn>(id);

        public HashedColumn GetHashedColumn(ColumnId id) => GetColumn<HashedColumn>(id);

        [HideInCallstack]
        public T GetColumn<T>(ColumnId id) where T : IColumn
        {
#if UNITY_EDITOR
            if (RangeCheckColumn(id))
            {
                if (_Columns[id] == null)
                {
                    Debug.LogErrorFormat("Column '{0}' was null", signature.GetName(id));
                }
                else if (_Columns[id].GetType() != typeof(T))
                {
                    Debug.LogErrorFormat("Column '{0}' was <b>{1}</b>, not <b>{2}</b>", signature.GetName(id), _Columns[id].GetType().Name, typeof(T));
                }
            }
#endif
            return (T)_Columns[id];
        }

        bool RangeCheckColumn(int columnId)
        {
            if (columnId < 0 || columnId >= _Columns.Length)
            {
                Debug.LogErrorFormat("columnId <b>{0}</b> out of range <b>{1}</b>", columnId, _Columns.Length);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get valid columns (indexes)
        /// </summary>
        public IEnumerable<int> GetColumns() => _ColumnSet;

        /// <summary>
        /// Clear all data (except the signature)
        /// </summary>
        public void Clear()
        {
            foreach (int columnId in new List<int>(GetColumns()))
                RemoveColumn(columnId);
            _nEntities = 0;
        }

        /// <summary>
        /// Allocates memory for entities, but doesn't actually add them. (for if you
        /// know you're going to add a bunch of entities, but not all at once. It's a lot more memory efficient.)
        /// </summary>
        /// <param name="nEntities"></param>
        public void PreAddEntities(int nEntities)
        {
            //Debug.LogFormat("Pre-adding {0} entities (current total: {1})", nEntities, _nEntities);
            foreach (int i in _ColumnSet)
                _Columns[i].PrePushEntities(nEntities);
        }

        /// <summary>
        /// Push entities
        /// </summary>
        /// <param name="nEntities">Number of entities to add</param>
        /// <returns>The first entityId of the new entities</returns>
        public EntityId AddEntities(int nEntities)
        {
            EntityId start = _nEntities;
            foreach (int i in _ColumnSet)
                _Columns[i].PushEntities(nEntities);
            _nEntities += nEntities;
            return start;
        }

        public void Pop_Entities(int nEntities)
        {
            foreach (int i in _ColumnSet)
                _Columns[i].PopEntities(nEntities);
            _nEntities -= nEntities;
        }

        /// <summary>
        /// Input must be sorted
        /// </summary>
        public void Insert_Entities_Sorted(NativeArray<int> ids)
        {
            foreach (int i in _ColumnSet)
                _Columns[i].Insert_Entities_Sorted(ids);
            _nEntities += ids.Length;
        }

        /// <summary>
        /// Input must be sorted
        /// </summary>
        public void Extract_Entities_Sorted(NativeArray<int> ids)
        {
            foreach (int i in _ColumnSet)
            {
                _Columns[i].Extract_Entities_Sorted(ids);
            }
            _nEntities -= ids.Length;
        }

        static void print_list<T>(Span<T> list, int length) where T : unmanaged
        {
            Debug.LogFormat("Printing Array with length: {0}", length);
            for (int i = 0; i < length; i++)
            {
                Debug.LogFormat("[{0}] {1}", i, list[i]);
            }
            Debug.Log("Array end");
        }

        /// <summary>
        /// Number of columns
        /// </summary>
        public int Capacity => _Columns.Length;
        public int nColumns => _ColumnSet.Count;

        public Signature signature;

        /// <summary>
        /// Non-empty columns
        /// </summary>
        // Having an enumerable list of ColumnIDs helps us get rid of branching (we don't need to check for null on each column set)
        internal HashSet<int> _ColumnSet = new HashSet<int>();
        internal IColumn[] _Columns;
        /// <summary>
        /// Number of entities
        /// </summary>
        internal int _nEntities = 0;
        const int MIN_COLUMN_CAPACITY = 1;
    }
}