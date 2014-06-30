using SorceryHex.Gba.Pokemon.DataTypes;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace SorceryHex.Gba.Pokemon.DataTypes {
   class NullPointerMapper : IPointerMapper {
      public void Claim(IRunStorage runs, int location, int pointer) { }
   }

   class NullRunStorage : IRunStorage {
      public byte[] Data { get; set; }
      public void AddRun(int location, IDataRun run) { }
      public void AddLabeler(ILabeler labeler) { }
      public bool IsFree(int location) { throw new NotImplementedException(); }
      public int NextUsed(int location) { throw new NotImplementedException(); }
   }

   public class BuildableObject : DynamicObject {
      readonly byte[] _data;
      readonly PCS _pcs;
      readonly IList<string> _names = new List<string>();
      readonly IList<int> _lengths = new List<int>();
      readonly IList<Type> _types = new List<Type>();
      readonly IDictionary<string, ChildReader> _children = new Dictionary<string, ChildReader>();
      readonly ISet<string> _inline = new HashSet<string>();
      readonly IDictionary<string, int> _childrenLength = new Dictionary<string, int>();
      readonly int _location;

      public int Location { get { return _location; } }
      public int Length { get { return _lengths.Sum(); } }

      public BuildableObject(byte[] data, PCS pcs, int location) { _data = data; _pcs = pcs; _location = location; }

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
         var loc = _location + _lengths.Take(index).Sum();
         if (_types[index] == typeof(byte)) WriteBytes(loc, byte.Parse(value.ToString()), 1);
         else if (_types[index] == typeof(short)) WriteBytes(loc, short.Parse(value.ToString()), 2);
         else if (_types[index] == typeof(int)) WriteBytes(loc, int.Parse(value.ToString()), 4);
         else if (_types[index] == typeof(string)) _pcs.WriteString(_data, loc, _lengths[index], (string)value);
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
         var loc = _location + _lengths.Take(index).Sum();
         result = null;
         if (_types[index] == typeof(byte)) result = _data[loc];
         if (_types[index] == typeof(short)) result = _data.ReadShort(loc);
         if (_types[index] == typeof(int)) result = _data.ReadData(4, loc);
         if (_types[index] == typeof(string)) {
            if (_lengths[index] == 4) {
               int ptr = _data.ReadPointer(loc);
               if (ptr != -1) result = _pcs.ReadString(_data, ptr);
            } else {
               result = _pcs.ReadString(_data, loc, _lengths[index]);
            }
         }
         var runStorage = new NullRunStorage { Data = _data };
         var pointerMapper = new NullPointerMapper();
         if (_types[index] == typeof(BuildableObject)) {
            int ptr = _data.ReadPointer(loc);
            if (ptr != -1) {
               var builder = new Builder(runStorage, pointerMapper, _pcs, ptr);
               _children[name](builder);
               result = builder.Result;
            }
         }
         if (_types[index] == typeof(BuildableObject[])) {
            if (_inline.Contains(name)) {
               var array = new BuildableObject[_childrenLength[name]];
               var builder = new Builder(runStorage, pointerMapper, _pcs, loc);
               for (int i = 0; i < _childrenLength[name]; i++) {
                  _children[name](builder);
                  array[i] = builder.Result;
                  builder.Clear();
               }
               result = array;
            } else {
               var array = new BuildableObject[_childrenLength[name]];
               int ptr = _data.ReadPointer(loc);
               if (ptr == -1) {
                  result = null;
                  return true;
               }
               var builder = new Builder(runStorage, pointerMapper, _pcs, ptr);
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

      void WriteBytes(int location, int value, int length) {
         while (length > 0) {
            _data[location] = (byte)value;
            value >>= 8;
            location++;
            length--;
         }
      }

      #endregion
   }
}
