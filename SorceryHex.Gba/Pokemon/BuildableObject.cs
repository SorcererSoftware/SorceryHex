using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;

namespace SorceryHex.Gba.Pokemon.DataTypes {
   class NullPointerMapper : IPointerMapper {
      public void Claim(IRunStorage runs, int location, int pointer) { }
   }

   class NullRunStorage : IRunStorage {
      public byte[] Data { get { Debug.Fail("Don't call this!"); return null; } }
      public ISegment Segment { get; set; }
      public void AddRun(int location, IDataRun run) { }
      public void AddLabeler(ILabeler labeler) { }
      public bool IsFree(int location) { throw new NotImplementedException(); }
      public int NextUsed(int location) { throw new NotImplementedException(); }

      public IEnumerable<int> Runs(Func<string, IDataRun, bool> func) { yield break; }
   }

   public class BuildableObject : DynamicObject {
      readonly ISegment _segment;
      readonly BuilderCache _cache;
      readonly IList<string> _names = new List<string>();
      readonly IList<int> _lengths = new List<int>();
      readonly IList<Type> _types = new List<Type>();
      readonly IDictionary<string, ChildReader> _children = new Dictionary<string, ChildReader>();
      readonly ISet<string> _inline = new HashSet<string>();
      readonly IDictionary<string, int> _childrenLength = new Dictionary<string, int>();

      public int Location { get { return _segment.Location; } }
      public int Length { get { return _lengths.Sum(); } }

      public BuildableObject(ISegment segment, BuilderCache cache) { _segment = segment; _cache = cache; }

      #region Append

      public void AppendByte(string name) {
         _names.Add(name);
         _types.Add(typeof(byte));
         _lengths.Add(1);
      }

      public void AppendShort(string name) {
         _names.Add(name);
         _types.Add(typeof(short));
         _lengths.Add(2);
      }

      public void AppendWord(string name) {
         _names.Add(name);
         _types.Add(typeof(int));
         _lengths.Add(4);
      }

      public void AppendString(string name, int length) {
         _names.Add(name);
         _types.Add(typeof(string));
         _lengths.Add(length);
      }

      public void AppendStringPointer(string name) {
         _names.Add(name);
         _types.Add(typeof(string));
         _lengths.Add(4);
      }

      public void Append(string name, ChildReader reader) {
         _names.Add(name);
         _types.Add(typeof(BuildableObject));
         _lengths.Add(4);
         _children[name] = reader;
      }

      public void AppendArray(string name, int len, ChildReader reader) {
         _names.Add(name);
         _types.Add(typeof(BuildableObject[]));
         _lengths.Add(4);
         _children[name] = reader;
         _childrenLength[name] = len;
      }

      public void AppendInlineArray(string name, int len, int inlineLength, ChildReader reader) {
         _names.Add(name);
         _types.Add(typeof(BuildableObject[]));
         _childrenLength[name] = len;
         _children[name] = reader;
         _inline.Add(name);
         _lengths.Add(inlineLength);
      }

      public void AppendSkip(int length) {
         _names.Add(null);
         _types.Add(null);
         _lengths.Add(length);
      }

      #endregion

      public override string ToString() {
         return "{ " + _names.Where(name => name != null).Aggregate((a, b) => a + ", " + b) + " }";
      }

      #region Dynamic Object

      public override bool TryGetMember(GetMemberBinder binder, out object result) {
         var index = _names.IndexOf(binder.Name);
         if (index == -1) return base.TryGetMember(binder, out result);
         return TryMember(index, out result);
      }

      public override bool TrySetMember(SetMemberBinder binder, object value) {
         var index = _names.IndexOf(binder.Name);
         if (index == -1) return base.TrySetMember(binder, value);
         var loc = _lengths.Take(index).Sum();
         if (_types[index] == typeof(byte)) _segment.Write(loc, 1, byte.Parse(value.ToString()));
         else if (_types[index] == typeof(short)) _segment.Write(loc, 2, short.Parse(value.ToString()));
         else if (_types[index] == typeof(int)) _segment.Write(loc, 4, int.Parse(value.ToString()));
         else if (_types[index] == typeof(string)) _cache.Pcs.WriteString(_segment.Inner(loc), _lengths[index], (string)value);
         else return false;
         return true;
      }

      public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result) {
         var index = _names.IndexOf(binder.Name);
         if (index == -1) return base.TryInvokeMember(binder, args, out result);
         return TryMember(index, out result);
      }

      bool TryMember(int index, out object result) {
         string name = _names[index];
         var loc = _lengths.Take(index).Sum();
         result = null;
         if (_types[index] == typeof(byte)) result = _segment[loc];
         if (_types[index] == typeof(short)) result = (short)_segment.Read(loc, 2);
         if (_types[index] == typeof(int)) result = _segment.Read(loc, 4);
         if (_types[index] == typeof(string)) {
            if (_lengths[index] == 4) {
               var ptr = _segment.Follow(loc);
               if (ptr != null) result = _cache.Pcs.ReadString(ptr);
            } else {
               result = _cache.Pcs.ReadString(_segment.Inner(loc), _lengths[index]);
            }
         }
         var runStorage = new NullRunStorage { Segment = _segment };
         var pointerMapper = new NullPointerMapper();
         var nullCache = _cache.GetFixed();
         if (_types[index] == typeof(BuildableObject)) {
            var ptr = _segment.Follow(loc);
            if (ptr != null) {
               var builder = new Builder(nullCache, ptr);
               _children[name](builder);
               result = builder.Result;
            }
         }
         if (_types[index] == typeof(BuildableObject[])) {
            if (_inline.Contains(name)) {
               var array = new BuildableObject[_childrenLength[name]];
               var builder = new Builder(nullCache, _segment.Inner(loc));
               for (int i = 0; i < _childrenLength[name]; i++) {
                  _children[name](builder);
                  array[i] = builder.Result;
                  builder.Clear();
               }
               result = array;
            } else {
               var array = new BuildableObject[_childrenLength[name]];
               var ptr = _segment.Follow(loc);
               if (ptr == null) {
                  result = null;
                  return true;
               }
               var builder = new Builder(nullCache, ptr);
               for (int i = 0; i < _childrenLength[name]; i++) {
                  _children[name](builder);
                  array[i] = builder.Result;
                  builder.Clear();
               }
               result = array;
            }
         }

         return true;
      }

      #endregion
   }
}
