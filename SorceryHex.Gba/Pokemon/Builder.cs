using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Windows;

namespace SorceryHex.Gba.Pokemon.DataTypes {
   public delegate void ChildReader(IBuilder builder);
   public delegate int ChildJump(IBuilder builder);

   public interface IBuilder {
      bool Assert(bool value, string message);
      byte Byte(string name);
      short Short(string name);
      int Word(string name);
      short Species();
      void Pointer(string name);
      dynamic Pointer(string name, ChildReader reader);
      string StringPointer(string name);
      void NullablePointer(string name);
      dynamic NullablePointer(string name, ChildReader reader);
      void InlineArray(string name, int length, ChildReader reader);
      dynamic Array(string name, int length, ChildReader reader);
      string String(int len, string name);
      void Unused(int count);
      void Link(int len, string name, ChildJump jump);
      void WriteDebug(object o);
   }

   class Parser : IBuilder {
      readonly IDictionary<string, object> _result = new ExpandoObject();
      readonly IRunStorage _runs;
      readonly PCS _pcs;
      int _location;

      public string FaultReason { get; private set; }
      public dynamic Result { get { return _result; } }

      public Parser(IRunStorage runs, PCS pcs, int location) {
         _runs = runs;
         _location = location;
         _pcs = pcs;
      }

      public bool Assert(bool value, string message) {
         if (FaultReason != null) return false;
         if (!value) FaultReason = "Assertion Failed: " + message;
         return value;
      }

      public byte Byte(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data[_location];
         _location++;
         _result[name] = result;
         return result;
      }

      public short Short(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data.ReadShort(_location);
         _location += 2;
         _result[name] = result;
         return result;
      }

      public int Word(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data.ReadData(4, _location);
         _location += 4;
         _result[name] = result;
         return result;
      }

      public byte ByteNum(string name) { return Byte(name); }
      public short Species() { return Short("species"); }

      public void Pointer(string name) {
         if (FaultReason != null) return;
         var pointer = _runs.Data.ReadPointer(_location);
         _location += 4;
         if (pointer == -1) {
            FaultReason = name + ": not a pointer";
            return;
         }
         _result[name] = null;
      }

      public dynamic Pointer(string name, ChildReader reader) {
         if (FaultReason != null) return null;
         var pointer = _runs.Data.ReadPointer(_location);
         _location += 4;
         if (pointer == -1) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var child = new Parser(_runs, _pcs, pointer);
         reader(child);
         FaultReason = child.FaultReason;
         if (FaultReason != null) return null;
         _result[name] = child.Result;
         return child.Result;
      }

      public string StringPointer(string name) {
         if (FaultReason != null) return null;
         var pointer = _runs.Data.ReadPointer(_location);
         _location += 4;
         if (pointer == -1) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var str = _pcs.ReadString(_runs.Data, pointer);
         if (str == null) {
            FaultReason = name + ": not a string";
            return null;
         }
         return str;
      }

      public void NullablePointer(string name) {
         if (FaultReason != null) return;
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return;
         }
         Pointer(name);
      }

      public dynamic NullablePointer(string name, ChildReader reader) {
         if (FaultReason != null) return null;
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return null;
         }
         return Pointer(name, reader);
      }

      public void InlineArray(string name, int length, ChildReader reader) {
         if (FaultReason != null) return;
         var child = new Parser(_runs, _pcs, _location);
         for (int i = 0; i < length; i++) {
            reader(child);
            FaultReason = child.FaultReason;
            if (FaultReason != null) return;
         }
         _location = child._location;
      }

      public dynamic Array(string name, int length, ChildReader reader) {
         if (FaultReason != null) return null;
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return null;
         }
         var pointer = _runs.Data.ReadPointer(_location);
         _location += 4;
         if (pointer == -1) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var child = new Parser(_runs, _pcs, pointer);
         var array = new dynamic[length];
         for (int i = 0; i < array.Length; i++) {
            reader(child);
            FaultReason = child.FaultReason;
            if (FaultReason != null) return null;
            array[i] = child.Result;
         }
         _result[name] = array;
         return array;
      }

      public string String(int len, string name) {
         if (FaultReason != null) return null;
         string result = _pcs.ReadString(_runs.Data, _location, len);
         if (result == null) {
            FaultReason = name + " was not a string[" + len + "]";
            return null; ;
         }
         _location += len;
         if (result.Length >= len) {
            FaultReason = name + " : " + result + ": longer than " + len;
            return null;
         }
         _result[name] = result;
         return result;
      }

      public void Unused(int len) {
         if (FaultReason != null) return;
         _location += len;
      }

      public void Link(int len, string name, ChildJump jump) {
         if (FaultReason != null) return;
         _location += len;
      }

      public void WriteDebug(object o) { MessageBox.Show(MultiBoxControl.Parse(o)); }
   }

   class Builder2 : IBuilder {
      static IElementProvider hex(string hoverText = null) { return new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Number, hoverText: hoverText); }
      static IElementProvider nums(string hoverText = null) { return new GeometryElementProvider(Utils.NumericFlyweights, GbaBrushes.Number, hoverText: hoverText); }

      static IDataRun Build(IDictionary<string, IDataRun> dict, Func<string, IElementProvider> func, int len, string hoverText = "", IEditor editor = null) {
         if (dict.ContainsKey(hoverText)) return dict[hoverText];
         var run = new SimpleDataRun(func(hoverText == "" ? null : hoverText), len, editor);
         dict[hoverText] = run;
         return run;
      }

      BuildableObject _result;
      readonly IRunStorage _runs;
      readonly byte[] _data;
      readonly PointerMapper _mapper;
      readonly PCS _pcs;
      int _location;

      public BuildableObject Result { get { return _result; } }

      public Builder2(IRunStorage runs, PointerMapper mapper, PCS pcs, int location) {
         _runs = runs;
         _data = _runs.Data;
         _mapper = mapper;
         _pcs = pcs;
         _location = location;
         _result = new BuildableObject(runs.Data, _pcs, _location);
         // _result.Relocate(_location);
      }

      public Builder2(byte[] data, PCS pcs, int location) {
         _data = data;
         _pcs = pcs;
         _location = location;
         _result = new BuildableObject(_data, _pcs, _location);
         // _result.Relocate(_location);
      }

      public void Clear() {
         _result = new BuildableObject(_data, _pcs, _location);
         // _result.Relocate(_location);
      }

      public bool Assert(bool value, string message) {
         Debug.Assert(value, message);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _byteRuns = new Dictionary<string, IDataRun>();
      public byte Byte(string name) {
         if (_runs != null) _runs.AddRun(_location, Build(_byteRuns, hex, 1, name));
         var value = _data[_location];
         _location++;
         _result.AppendByte(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _byteNumRuns = new Dictionary<string, IDataRun>();
      IEditor _inlineByteNumEditor;
      IEditor InlineByteNumEditor {
         get {
            return _inlineByteNumEditor ?? (_inlineByteNumEditor = new InlineTextEditor(_data, 1, array => array[0].ToString(), str => new[] { byte.Parse(str) }));
         }
      }
      public byte ByteNum(string name) {
         if (_runs != null) _runs.AddRun(_location, Build(_byteNumRuns, nums, 1, name, InlineByteNumEditor));
         var value = _data[_location];
         _location++;
         _result.AppendByte(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _shortRuns = new Dictionary<string, IDataRun>();
      public short Short(string name) {
         if (_runs != null) _runs.AddRun(_location, Build(_shortRuns, hex, 2, name));
         var value = _data.ReadShort(_location);
         _location += 2;
         _result.AppendShort(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _wordRuns = new Dictionary<string, IDataRun>();
      public int Word(string name) {
         if (_runs != null) _runs.AddRun(_location, Build(_wordRuns, hex, 4, name));
         var value = _data.ReadData(4, _location);
         _location += 4;
         _result.AppendWord(name);
         return value;
      }

      static readonly IDataRun _speciesRun = new SimpleDataRun(SpeciesElementProvider.Instance, 2);
      public short Species() {
         if (_runs != null) _runs.AddRun(_location, _speciesRun);
         var value = _data.ReadShort(_location);
         _location += 2;
         _result.AppendShort("species");
         return value;
      }

      public void Pointer(string name) {
         var pointer = _data.ReadPointer(_location);
         if (_mapper != null) _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         _result.AppendSkip(4);
      }

      public dynamic Pointer(string name, ChildReader reader) {
         var pointer = _data.ReadPointer(_location);
         if (_mapper != null) _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = (_runs != null) ? new Builder2(_runs, _mapper, _pcs, pointer) : new Builder2(_data, _pcs, pointer);
         reader(child);
         _result.Append(name, reader);
         return child.Result;
      }

      public string StringPointer(string name) {
         var pointer = _data.ReadPointer(_location);
         if (_mapper != null) _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var str = _pcs.ReadString(_runs.Data, pointer);
         _result.AppendStringPointer(name);
         return str;
      }

      public void NullablePointer(string name) {
         if (_data.ReadData(4, _location) == 0) {
            _location += 4;
            _result.AppendSkip(4);
            return;
         }
         Pointer(name);
      }

      public dynamic NullablePointer(string name, ChildReader reader) {
         if (_data.ReadData(4, _location) == 0) {
            _location += 4;
            _result.Append(name, null);
            return null;
         }
         return Pointer(name, reader);
      }

      public void InlineArray(string name, int length, ChildReader reader) {
         // var array = new BuildableObject[length];
         int start = _location;
         for (int i = 0; i < length; i++) {
            var child = (_runs != null) ? new Builder2(_runs, _mapper, _pcs, _location) : new Builder2(_data, _pcs, _location);
            reader(child);
            _location = child._location;
            // array[i] = child._result;
         }
         _result.AppendInlineArray(name, length, _location - start, reader);
      }

      public dynamic Array(string name, int length, ChildReader reader) {
         if (_data.ReadData(4, _location) == 0) {
            _location += 4;
            _result.AppendSkip(4);
            return null;
         }
         var pointer = _data.ReadPointer(_location);
         if (_mapper != null) _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = (_runs != null) ? new Builder2(_runs, _mapper, _pcs, pointer) : new Builder2(_data, _pcs, pointer);
         for (int i = 0; i < length; i++) {
            child.Clear();
            reader(child);
         }
         _result.AppendArray(name, length, reader);
         return null;
      }

      public string String(int len, string name) {
         if (_runs != null) _runs.AddRun(_location, _pcs.StringRun);
         string result = _pcs.ReadString(_data, _location);
         _location += len;
         _result.AppendString(name, len);
         return result;
      }

      static readonly IDataRun _unusedRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Unused), 1);
      public void Unused(int len) {
         _result.AppendSkip(len);
         while (len > 0) {
            if (_runs != null) _runs.AddRun(_location, _unusedRun);
            _location++;
            len--;
         }
      }

      public void Link(int len, string name, ChildJump jump) {
         _result.AppendSkip(len);
         var run = new SimpleDataRun(new JumpElementProvider(jump), len);
         if (_runs != null) _runs.AddRun(_location, run);
         _location += len;
      }

      public void WriteDebug(object o) { MessageBox.Show(MultiBoxControl.Parse(o)); }
   }

   public class BuildableObject : DynamicObject {
      readonly byte[] _data;
      readonly PCS _pcs;
      readonly IList<string> _names = new List<string>();
      readonly IList<int> _lengths = new List<int>();
      readonly IList<Type> _types = new List<Type>();
      readonly IDictionary<string, ChildReader> _children = new Dictionary<string, ChildReader>();
      readonly ISet<string> _inline = new HashSet<string>();
      // readonly IDictionary<string, BuildableObject[]> _childrenArray = new Dictionary<string, BuildableObject[]>();
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

      //public void AppendArray(string name, BuildableObject[] array) {
      //   _names.Add(name);
      //   _types.Add(typeof(BuildableObject[]));
      //   _lengths.Add(array.Sum(b => b.Length));
      //   _childrenArray[name] = array;
      //}

      public void AppendSkip(int length) {
         _names.Add(null);
         _types.Add(null);
         _lengths.Add(length);
      }

      #endregion

      public override string ToString() {
         return "{ " + _names.Where(name => name != null).Aggregate((a, b) => a + ", " + b) + " }";
      }

      // public void Relocate(int location) { _location = location; }

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
         if (_types[index] == typeof(BuildableObject)) {
            int ptr = _data.ReadPointer(loc);
            if (ptr != -1) {
               var builder = new Builder2(_data, _pcs, ptr);
               _children[name](builder);
               result = builder.Result;
            }
         }
         if (_types[index] == typeof(BuildableObject[])) {
            if (_inline.Contains(name)) {
               var array = new BuildableObject[_childrenLength[name]];
               var builder = new Builder2(_data, _pcs, loc);
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
               var builder = new Builder2(_data, _pcs, ptr);
               for (int i = 0; i < _childrenLength[name]; i++) {
                  _children[name](builder);
                  array[i] = builder.Result;
                  builder.Clear();
               }
               result = array;
            }

            //if (_childrenArray.ContainsKey(name)) {
            //   result = _childrenArray[name];
            //   for (int i = 0; i < _childrenArray[name].Length; i++) {
            //      _childrenArray[name][i].Relocate(loc + _childrenArray[name].Take(i).Sum(p => p.Length));
            //   }
            //} else {
            //   int ptr = _data.ReadPointer(loc);
            //   if (ptr != -1) {
            //      result = new BuildableArray(_children[name], ptr, _childrenLength[name]);
            //   }
            //}
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

   //public class BuildableArray : DynamicObject, ILabeler, IEnumerable<BuildableObject> {
   //   readonly BuildableObject _member;
   //   readonly int _location, _length;
   //   public int Length { get { return _length; } }
   //   public BuildableArray(BuildableObject member, int location, int length) { _member = member; _location = location; _length = length; }

   //   #region Labels

   //   Func<int, string> _labelmaker;
   //   public void Label(IRunStorage runs, Func<int, string> label) { runs.AddLabeler(this); _labelmaker = label; }

   //   public string GetLabel(int index) {
   //      if (index < _location) return null;
   //      int stride = _member.Length;
   //      if ((index - _location) % stride != 0) return null;
   //      int i = (index - _location) / stride;
   //      if (i >= _length) return null;
   //      return _labelmaker(i);
   //   }

   //   #endregion

   //   public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result) {
   //      if (indexes.Length != 1) return base.TryGetIndex(binder, indexes, out result);
   //      try {
   //         var index = int.Parse(indexes[0].ToString()); // casting from a dynamic is difficult
   //         if (index >= _length) return base.TryGetIndex(binder, indexes, out result);
   //         int stride = _member.Length;
   //         _member.Relocate(_location + stride * index);
   //         result = _member;
   //         return true;
   //      } catch (Exception e) { return base.TryGetIndex(binder, indexes, out result); }
   //   }

   //   public override string ToString() {
   //      return "Array[" + _length + "] " + _member.ToString();
   //   }
   //   public int destinationof(int i) {
   //      int stride = _member.Length;
   //      return _location + i * stride;
   //   }

   //   #region Enumerator

   //   public IEnumerable<BuildableObject> each() { return this; }
   //   public IEnumerator<BuildableObject> GetEnumerator() {
   //      for (int i = 0; i < _length; i++) {
   //         _member.Relocate(_location + _member.Length * i);
   //         yield return _member;
   //      }
   //   }
   //   IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

   //   #endregion
   //}

   class Reader : IBuilder {
      readonly byte[] _data;
      int _location;
      public Reader(byte[] data, int location) { _data = data; _location = location; }

      public bool Assert(bool value, string message) { return true; }
      public byte Byte(string name) { return _data[_location++]; }
      public short Short(string name) { return _data.ReadShort(_location += 2); }
      public int Word(string name) { return _data.ReadData(4, _location += 4); }
      public short Species() { return Short(null); }
      public void Pointer(string name) { throw new NotImplementedException(); }
      public dynamic Pointer(string name, ChildReader reader) { throw new NotImplementedException(); }
      public string StringPointer(string name) { throw new NotImplementedException(); }
      public void NullablePointer(string name) { throw new NotImplementedException(); }
      public dynamic NullablePointer(string name, ChildReader reader) { throw new NotImplementedException(); }
      public void InlineArray(string name, int length, ChildReader reader) { throw new NotImplementedException(); }
      public dynamic Array(string name, int length, ChildReader reader) { throw new NotImplementedException(); }
      public string String(int len, string name) { throw new NotImplementedException(); }
      public void Unused(int count) { _location += count; }
      public void Link(int len, string name, ChildJump jump) { throw new NotImplementedException(); }
      public void WriteDebug(object o) { MessageBox.Show(MultiBoxControl.Parse(o)); }
   }
}
