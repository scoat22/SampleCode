// For a first working pass, this is an object. In the future, it would be a struct. 

namespace SpreadSheetNS.Query
{
    /// <summary>
    /// Column Request (for reading)
    /// </summary>
    public struct Read<T> where T : IColumn
    {
        public ColumnId id;
        public T result;
        public Read(ColumnId id) { this.id = id; result = default; }
    }

    /// <summary>
    /// Column request (for writing)
    /// </summary>
    public struct Write<T> where T : IColumn
    {
        public ColumnId id;
        public T result;
        public Write(ColumnId id) { this.id = id; result = default; }
    }

    /// <summary>
    /// Access columns for reading or writing from the spreadsheet within this query object
    /// </summary>
    // basically a memory requester
    public class QueryableSpreadSheet : System.IDisposable
    {
        public QueryableSpreadSheet(SpreadSheet sheet, int nSystems)
        {
            _sheet = sheet;
            _agePerColumn = new uint[sheet.Capacity];

            // Initialize requester's age per column memory
            _requesters = new SystemMemory[nSystems];
            for (int i = 0; i < nSystems; i++)
            {
                // We don't want the ages to line up. Because otherwise it'll say the column changed forever if we never touch it. (So set to 0 initially)
                _requesters[i].ages = new uint[sheet.Capacity];
            }
            // initialize intersection cache
            _intersections = new SparseColumn[nSystems];
        }

        public EntityId AddEntities(SystemId systemId, int nEntities)
        {
            EntityId startId =_sheet.AddEntities(nEntities);

            BumpSizeAge(systemId);

            // Bump all (all columns get reallocated)
            for (ColumnId id = 0; id < _agePerColumn.Length; id++)
                BumpAge(-1, id);

            return startId;
        }

        public int nEntities => _sheet.nEntities;

        public bool DidSizeChange(SystemId id) => DidSizeChangeAndRememberAge(id);

        public void Query<A>(ref Read<A> a)
            where A : IColumn
        {
            a.result = _sheet.GetColumn<A>(a.id);
        }

        public void Query<A, B>(ref Read<A> a, ref Read<B> b)
            where A : FilledColumn where B : FilledColumn
        {
            a.result = _sheet.GetColumn<A>(a.id);
            b.result = _sheet.GetColumn<B>(b.id);
        }

        public void Query<A>(SystemId id, ref Write<A> a_RW)
            where A : IColumn
        {
            a_RW.result = _sheet.GetColumn<A>(a_RW.id); BumpAge(id, a_RW.id);
        }

        [UnityEngine.HideInCallstack]
        public void Query<A, B, C>(SystemId id, ref Write<A> a_RW, ref Write<B> b_RW, ref Write<C> c_RW)
            where A : FilledColumn
            where B : FilledColumn
            where C : FilledColumn
        {
            a_RW.result = _sheet.GetColumn<A>(a_RW.id); BumpAge(id, a_RW.id);
            b_RW.result = _sheet.GetColumn<B>(b_RW.id); BumpAge(id, b_RW.id);
            c_RW.result = _sheet.GetColumn<C>(c_RW.id); BumpAge(id, c_RW.id);
        }

        public void Query<ASparse, BSparse>(SystemId systemId, ref Read<ASparse> a, ref Read<BSparse> b, out SparseColumn intersection)
            where ASparse : SparseColumn
            where BSparse : SparseColumn
        {
            a.result = _sheet.GetColumn<ASparse>(a.id);
            b.result = _sheet.GetColumn<BSparse>(b.id);

            // Did anything change?
            if (DidColumnChangeAndRememberAge(systemId, a.id) || DidColumnChangeAndRememberAge(systemId, b.id)
            // It's the first time this function is called (maybe think of a better way to do this in the future)
                || _intersections[systemId] == null)
            {
                //Log("Columns changed! Calculating intersection...");
                _intersections[systemId]?.Dispose(); // Dispose the old intersection first
                intersection = SparseColumn.Intersection(a.result, b.result);
                _intersections[systemId] = intersection; // cache the intersection
               
            }
            {
                //Log("Retrieving cached intersection");
                intersection = _intersections[systemId]; // retrieve cached intersection
            }
        }

        /*public void Query<A, B>(ref Write<A> a_RW, ref Read<B> b)
            where A : IColumn
            where B : IColumn
        {
            a_RW.result = _sheet.GetColumn<A>(a_RW.id);
            b.result = _sheet.GetColumn<B>(b.id);
            Bump(systemId, a_RW.id); // bump age
        }

        public void Query<A, B>(ref Write<A> a_RW, ref Write<B> b)
            where A : IColumn
            where B : IColumn
        {
            a_RW.result = _sheet.GetColumn<A>(a_RW.id);
            b.result = _sheet.GetColumn<B>(b.id);
            Bump(systemId, a_RW.id); // bump age
            Bump(systemId, b.id); // bump age
        }

        public void Query<A, B, C>(ref Read<A> a, ref Read<B> b, ref Read<C> c)
            where A : IColumn
            where B : IColumn
            where C : IColumn
        {
            a.result = _sheet.GetColumn<A>(a.id);
            b.result = _sheet.GetColumn<B>(b.id);
            c.result = _sheet.GetColumn<C>(c.id);
        }

        // Add more versions of these functions if you need them. I only stopped because I was lazy and didn't want to write anymore. 
        public void Query<A, B, C>(ref Write<A> a_RW, ref Read<B> b, ref Read<C> c)
            where A : IColumn
            where B : IColumn
            where C : IColumn
        {
            a_RW.result = _sheet.GetColumn<A>(a_RW.id);
            b.result = _sheet.GetColumn<B>(b.id);
            c.result = _sheet.GetColumn<C>(c.id);
            Bump(systemId, a_RW.id); // bump age
        }*/

        /// <summary>
        /// Returns true if any column has been accessed for writing since the last time we made a request
        /// </summary>
        /// <typeparam name="A"></typeparam>
        /// <param name="systemId">Who's asking?</param>
        /// <param name="a"></param>
        /// <returns></returns>
        public bool QueryChanged<A>(SystemId systemId, ref Read<A> a)
            where A : IColumn
        {
            a.result = _sheet.GetColumn<A>(a.id); // set the result
            if(DidColumnChangeAndRememberAge(systemId, a.id)) return true;
            return false; // No change
        }

        public void Dispose()
        {
            // Dispose intersections
            for (SystemId systemId = 0; systemId < _intersections.Length; systemId++)
            {
                _intersections[systemId].Dispose();
            }
        }

        /*public bool QueryChanged<A, B>(SystemId systemId, ref Read<A> a, ref Read<B> b)
            where A : IColumn
            where B : IColumn
        {
            a.result = _sheet.GetColumn<A>(a.id);
            b.result = _sheet.GetColumn<B>(b.id);

            // Did it change?
            if (_agePerColumn[a.id] != _requesters[systemId].ages[a.id]) return true;
            if (_agePerColumn[b.id] != _requesters[systemId].ages[b.id]) return true;

            // No change
            return false;
        }

        public bool QueryChanged<A, B, C>(SystemId systemId, ref Read<A> a, ref Read<B> b, ref Read<C> c)
            where A : IColumn
            where B : IColumn
            where C : IColumn
        {
            a.result = _sheet.GetColumn<A>(a.id);
            b.result = _sheet.GetColumn<B>(b.id);
            c.result = _sheet.GetColumn<C>(c.id);

            // Did it change?
            if (_agePerColumn[a.id] != _requesters[systemId].ages[a.id]) return true;
            if (_agePerColumn[b.id] != _requesters[systemId].ages[b.id]) return true;
            if (_agePerColumn[c.id] != _requesters[systemId].ages[c.id]) return true;

            // No change
            return false;
        }*/

        // has an if statement
        bool DidColumnChangeAndRememberAge(SystemId systemId, ColumnId columnId)
        {
            if(_requesters[systemId].ages[columnId] != _agePerColumn[columnId])
            {
                // Update memory
                //LogFormat("Column age changed from {0} to {1}. Updating memory...", _requesters[systemId].ages[columnId], _agePerColumn[columnId]);
                _requesters[systemId].ages[columnId] = _agePerColumn[columnId];
                return true;
            }
            return false;
        }

        bool DidSizeChangeAndRememberAge(SystemId systemId)
        {
            if(_requesters[systemId].size_age != _sizeAge)
            {
                // Update memory
                //LogFormat("Column age changed from {0} to {1}. Updating memory...", _requesters[systemId].ages[columnId], _agePerColumn[columnId]);
                _requesters[systemId].size_age = _sizeAge;
                return true;
            }
            return false;
        }

        // Have one function for this, mostly for debugging
        void BumpAge(SystemId systemId, ColumnId id) 
        {
            //Debug.LogFormat("System {0} modified column {1}", systemId, id);
            _agePerColumn[id]++;
        }

        void BumpSizeAge(SystemId systemId)
        {
            _sizeAge++;
        }

        /// <summary>
        /// The actual age, per column (bumped when the column is accessed for writing)
        /// </summary>
        uint[] _agePerColumn;
        /// <summary>
        /// Bumped whenever we add/remove entities (change the size)
        /// </summary>
        uint _sizeAge;

        /// <summary>
        /// The intersection, per systemId
        /// </summary>
        SparseColumn[] _intersections;

        /// <summary>
        /// Each systemId needs its own "memory" for when it last saw the column
        /// </summary>
        private SystemMemory[] _requesters;

        private SpreadSheet _sheet;

        public SpreadSheet GetSheetForDebug() => _sheet;

        // age/version per column
        struct SystemMemory
        {
            public uint[] ages;
            public uint size_age;
        }
    }
}