using System;
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
      void NullablePointer(string name);
      dynamic NullablePointer(string name, ChildReader reader);
      dynamic Array(string name, int length, ChildReader reader);
      string String(int len, string name);
      void Unused(int count);
      void Link(int len, string name, ChildJump jump);
      void WriteDebug(object o);
   }

   class Parser : IBuilder {
      readonly IDictionary<string, object> _result = new ExpandoObject();
      readonly IRunStorage _runs;
      int _location;

      public string FaultReason { get; private set; }
      public dynamic Result { get { return _result; } }

      public Parser(IRunStorage runs, int location) {
         _runs = runs;
         _location = location;
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
         var child = new Parser(_runs, pointer);
         reader(child);
         FaultReason = child.FaultReason;
         if (FaultReason != null) return null;
         _result[name] = child.Result;
         return child.Result;
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
         var child = new Parser(_runs, pointer);
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
         string result = PCS.ReadString(_runs.Data, _location, len);
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

      static IDataRun Build(IDictionary<string, IDataRun> dict, Func<string, IElementProvider> func, int len, string hoverText = "") {
         if (dict.ContainsKey(hoverText)) return dict[hoverText];
         var run = new SimpleDataRun(func(hoverText == "" ? null : hoverText), len);
         dict[hoverText] = run;
         return run;
      }

      BuildableObject _result;
      readonly IRunStorage _runs;
      readonly PointerMapper _mapper;
      int _location;

      public dynamic Result { get { return _result; } }

      public Builder2(IRunStorage runs, PointerMapper mapper, int location) {
         _result = new BuildableObject(runs.Data);
         _runs = runs;
         _mapper = mapper;
         _location = location;
         _result.Relocate(_location);
      }

      public void Clear() {
         _result = new BuildableObject(_runs.Data);
         _result.Relocate(_location);
      }

      public bool Assert(bool value, string message) {
         Debug.Assert(value, message);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _byteRuns = new Dictionary<string, IDataRun>();
      public byte Byte(string name) {
         _runs.AddRun(_location, Build(_byteRuns, hex, 1, name));
         var value = _runs.Data[_location];
         _location++;
         _result.AppendByte(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _byteNumRuns = new Dictionary<string, IDataRun>();
      public short ByteNum(string name) {
         _runs.AddRun(_location, Build(_byteNumRuns, nums, 1, name));
         var value = _runs.Data[_location];
         _location++;
         _result.AppendByte(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _shortRuns = new Dictionary<string, IDataRun>();
      public short Short(string name) {
         _runs.AddRun(_location, Build(_shortRuns, hex, 2, name));
         var value = _runs.Data.ReadShort(_location);
         _location += 2;
         _result.AppendShort(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _wordRuns = new Dictionary<string, IDataRun>();
      public int Word(string name) {
         _runs.AddRun(_location, Build(_wordRuns, hex, 4, name));
         var value = _runs.Data.ReadData(4, _location);
         _location += 4;
         _result.AppendWord(name);
         return value;
      }

      static readonly IDataRun _speciesRun = new SimpleDataRun(SpeciesElementProvider.Instance, 2);
      public short Species() {
         _runs.AddRun(_location, _speciesRun);
         var value = _runs.Data.ReadShort(_location);
         _location += 2;
         _result.AppendShort("species");
         return value;
      }

      public void Pointer(string name) {
         var pointer = _runs.Data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         _result.AppendSkip(4);
      }

      public dynamic Pointer(string name, ChildReader reader) {
         var pointer = _runs.Data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = new Builder2(_runs, _mapper, pointer);
         reader(child);
         _result.Append(name, child.Result);
         return child.Result;
      }

      public void NullablePointer(string name) {
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result.AppendSkip(4);
            return;
         }
         Pointer(name);
      }

      public dynamic NullablePointer(string name, ChildReader reader) {
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result.AppendSkip(4);
            return null;
         }
         return Pointer(name, reader);
      }

      public dynamic Array(string name, int length, ChildReader reader) {
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result.AppendSkip(4);
            return null;
         }
         var pointer = _runs.Data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = new Builder2(_runs, _mapper, pointer);
         for (int i = 0; i < length; i++) {
            reader(child);
         }
         _result.AppendArray(name, length, child.Result);
         return null;
      }

      public string String(int len, string name) {
         _runs.AddRun(_location, PCS.StringRun);
         string result = PCS.ReadString(_runs.Data, _location);
         _location += len;
         _result.AppendString(name, len);
         return result;
      }

      static readonly IDataRun _unusedRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Unused), 1);
      public void Unused(int len) {
         _result.AppendSkip(len);
         while (len > 0) {
            _runs.AddRun(_location, _unusedRun);
            _location++;
            len--;
         }
      }

      public void Link(int len, string name, ChildJump jump) {
         _result.AppendSkip(len);
         var run = new SimpleDataRun(new JumpElementProvider(jump), len);
         _runs.AddRun(_location, run);
         _location += len;
      }

      public void WriteDebug(object o) { MessageBox.Show(MultiBoxControl.Parse(o)); }
   }

   class BuildableObject : DynamicObject {

      readonly byte[] _data;
      readonly IList<string> _names = new List<string>();
      readonly IList<int> _lengths = new List<int>();
      readonly IList<Type> _types = new List<Type>();
      readonly IDictionary<string, BuildableObject> _children = new Dictionary<string, BuildableObject>();
      readonly IDictionary<string, int> _childrenLength = new Dictionary<string, int>();
      int _location;

      public BuildableObject(byte[] data) { _data = data; }

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

      public void Append(string name, BuildableObject b) {
         _names.Add(name);
         _types.Add(typeof(BuildableObject));
         _lengths.Add(4);
         _children[name] = b;
      }

      public void AppendArray(string name, int len, BuildableObject b) {
         _names.Add(name);
         _types.Add(typeof(BuildableObject[]));
         _lengths.Add(4);
         _children[name] = b;
         _childrenLength[name] = len;
      }

      public void AppendSkip(int length) {
         _names.Add(null);
         _types.Add(null);
         _lengths.Add(length);
      }

      #endregion

      public void Relocate(int location) { _location = location; }

      #region Dynamic Object

      public override bool TryGetMember(GetMemberBinder binder, out object result) {
         var index = _names.IndexOf(binder.Name);
         if (index == -1) return base.TryGetMember(binder, out result);
         var loc = _location + _lengths.Take(index).Sum();
         result = null;
         if (_types[index] == typeof(byte)) result = _data[loc];
         if (_types[index] == typeof(short)) result = _data.ReadShort(loc);
         if (_types[index] == typeof(int)) result = _data.ReadData(4, loc);
         if (_types[index] == typeof(string)) result = PCS.ReadString(_data, loc, _lengths[index]);
         if (_types[index] == typeof(BuildableObject)) {
            int ptr = _data.ReadPointer(loc);
            if (ptr != -1) {
               _children[binder.Name].Relocate(ptr);
               result = _children[binder.Name];
            }
         }
         if (_types[index] == typeof(BuildableObject[])) {
            int ptr = _data.ReadPointer(loc);
            if (ptr != -1) {
               result = new BuildableArray(_children[binder.Name], ptr, _childrenLength[binder.Name]);
            }
         }

         return true;
      }

      public override bool TrySetMember(SetMemberBinder binder, object value) {
         var index = _names.IndexOf(binder.Name);
         if (index == -1) return base.TrySetMember(binder, value);
         var loc = _location + _lengths.Take(index).Sum();
         if (_types[index] == typeof(byte)) WriteBytes(loc, byte.Parse(value.ToString()), 1);
         else if (_types[index] == typeof(short)) WriteBytes(loc, short.Parse(value.ToString()), 2);
         else if (_types[index] == typeof(int)) WriteBytes(loc, int.Parse(value.ToString()), 4);
         else if (_types[index] == typeof(string)) PCS.WriteString(_data, loc, _lengths[index], (string)value);
         else return false;
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

      class BuildableArray : DynamicObject {
         readonly BuildableObject _member;
         readonly int _location, _length;
         public BuildableArray(BuildableObject member, int location, int length) { _member = member; _location = location; _length = length; }
         public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result) {
            if (indexes.Length != 1) return base.TryGetIndex(binder, indexes, out result);
            try {
               var index = (int)indexes[0];
               if (index >= _length) return base.TryGetIndex(binder, indexes, out result);
               int stride = _member._lengths.Sum();
               _member.Relocate(_location + stride * index);
               result = _member;
               return true;
            } catch { return base.TryGetIndex(binder, indexes, out result); }
         }
      }
   }

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
      public void NullablePointer(string name) { throw new NotImplementedException(); }
      public dynamic NullablePointer(string name, ChildReader reader) { throw new NotImplementedException(); }
      public dynamic Array(string name, int length, ChildReader reader) { throw new NotImplementedException(); }
      public string String(int len, string name) { throw new NotImplementedException(); }
      public void Unused(int count) { _location += count; }
      public void Link(int len, string name, ChildJump jump) { throw new NotImplementedException(); }
      public void WriteDebug(object o) { MessageBox.Show(MultiBoxControl.Parse(o)); }
   }
}
