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
      byte ByteNum(string name);
      byte ByteEnum(string name, dynamic[] names);
      short Short(string name);
      short ShortEnum(string name, dynamic[] names);
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

      public byte ByteEnum(string name, dynamic[] names) {
         var val = Byte(name);
         if (val >= names.Length) FaultReason = name + ": " + val + " larger than available range: " + names.Length;
         return val;
      }

      public short Short(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data.ReadShort(_location);
         _location += 2;
         _result[name] = result;
         return result;
      }

      public short ShortEnum(string name, dynamic[] names) {
         var val = Short(name);
         if (val >= names.Length) FaultReason = name + ": " + val + " larger than available range: " + names.Length;
         return val;
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

   class Builder : IBuilder {
      static IElementProvider hex(string hoverText = null) { return new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Number, hoverText: hoverText); }
      static IElementProvider nums(string hoverText = null) { return new GeometryElementProvider(Utils.NumericFlyweights, GbaBrushes.Number, hoverText: hoverText); }
      static IElementProvider enums(dynamic[] names, int stride, string hoverText = null) { return new EnumElementProvider(names, stride, GbaBrushes.Number, hoverText: hoverText); }

      static IDataRun Build(IDictionary<string, IDataRun> dict, Func<string, IElementProvider> func, int len, string hoverText = "", IEditor editor = null) {
         if (dict.ContainsKey(hoverText)) return dict[hoverText];
         var run = new SimpleDataRun(func(hoverText == "" ? null : hoverText), len, editor);
         dict[hoverText] = run;
         return run;
      }

      BuildableObject _result;
      readonly IRunStorage _runs;
      readonly byte[] _data;
      readonly IPointerMapper _mapper;
      readonly PCS _pcs;
      int _location;

      public BuildableObject Result { get { return _result; } }

      public Builder(IRunStorage runs, IPointerMapper mapper, PCS pcs, int location) {
         _runs = runs;
         _data = _runs.Data;
         _mapper = mapper;
         _pcs = pcs;
         _location = location;
         _result = new BuildableObject(runs.Data, _pcs, _location);
      }

      public void Clear() {
         _result = new BuildableObject(_data, _pcs, _location);
      }

      public bool Assert(bool value, string message) {
         Debug.Assert(value, message);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _byteRuns = new Dictionary<string, IDataRun>();
      public byte Byte(string name) {
         _runs.AddRun(_location, Build(_byteRuns, hex, 1, name));
         var value = _data[_location];
         _location++;
         _result.AppendByte(name);
         return value;
      }

      static readonly IList<InlineComboEditor> _byteEnumEditors = new List<InlineComboEditor>();
      static readonly IList<SimpleDataRun> _byteEnumRuns = new List<SimpleDataRun>();
      public byte ByteEnum(string name, dynamic[] names) {
         var editor = _byteEnumEditors.FirstOrDefault(e => e.Names == names && e.HoverText == name);
         if (editor == null) {
            editor = new InlineComboEditor(_data, 1, names, name);
            _byteEnumEditors.Add(editor);
         }
         var run = _byteEnumRuns.FirstOrDefault(r => r.Editor == editor);
         if (run == null) {
            run = new SimpleDataRun(enums(names, 1, name), 1, editor);
            _byteEnumRuns.Add(run);
         }
         _runs.AddRun(_location, run);

         var value = _data[_location];
         _location += 1;
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
         _runs.AddRun(_location, Build(_byteNumRuns, nums, 1, name, InlineByteNumEditor));
         var value = _data[_location];
         _location++;
         _result.AppendByte(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _shortRuns = new Dictionary<string, IDataRun>();
      public short Short(string name) {
         _runs.AddRun(_location, Build(_shortRuns, hex, 2, name));
         var value = _data.ReadShort(_location);
         _location += 2;
         _result.AppendShort(name);
         return value;
      }

      static readonly IList<InlineComboEditor> _shortEnumEditors = new List<InlineComboEditor>();
      static readonly IList<SimpleDataRun> _shortEnumRuns = new List<SimpleDataRun>();
      public short ShortEnum(string name, dynamic[] names) {
         var editor = _shortEnumEditors.FirstOrDefault(e => e.Names == names && e.HoverText == name);
         if (editor == null) {
            editor = new InlineComboEditor(_data, 2, names, name);
            _shortEnumEditors.Add(editor);
         }
         var run = _shortEnumRuns.FirstOrDefault(r => r.Editor == editor);
         if (run == null) {
            run = new SimpleDataRun(enums(names, 2, name), 2, editor);
            _shortEnumRuns.Add(run);
         }
         _runs.AddRun(_location, run);

         var value = _data[_location];
         _location += 2;
         _result.AppendShort(name);
         return value;
      }

      static readonly IDictionary<string, IDataRun> _wordRuns = new Dictionary<string, IDataRun>();
      public int Word(string name) {
         _runs.AddRun(_location, Build(_wordRuns, hex, 4, name));
         var value = _data.ReadData(4, _location);
         _location += 4;
         _result.AppendWord(name);
         return value;
      }

      static readonly IDataRun _speciesRun = new SimpleDataRun(SpeciesElementProvider.Instance, 2);
      public short Species() {
         _runs.AddRun(_location, _speciesRun);
         var value = _data.ReadShort(_location);
         _location += 2;
         _result.AppendShort("species");
         return value;
      }

      public void Pointer(string name) {
         var pointer = _data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         _result.AppendSkip(4);
      }

      public dynamic Pointer(string name, ChildReader reader) {
         var pointer = _data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = new Builder(_runs, _mapper, _pcs, pointer);
         reader(child);
         _result.Append(name, reader);
         return child.Result;
      }

      public string StringPointer(string name) {
         var pointer = _data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
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
         int start = _location;
         for (int i = 0; i < length; i++) {
            var child = new Builder(_runs, _mapper, _pcs, _location);
            reader(child);
            _location = child._location;
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
         var child = new Builder(_runs, _mapper, _pcs, pointer);
         for (int i = 0; i < length; i++) {
            child.Clear();
            reader(child);
         }
         _result.AppendArray(name, length, reader);
         return null;
      }

      public string String(int len, string name) {
         _runs.AddRun(_location, _pcs.StringRun);
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

   class Reader : IBuilder {
      readonly byte[] _data;
      int _location;
      public Reader(byte[] data, int location) { _data = data; _location = location; }

      public bool Assert(bool value, string message) { return true; }
      public byte Byte(string name) { return _data[_location++]; }
      public byte ByteEnum(string name, dynamic[] names) { return Byte(name); }
      public byte ByteNum(string name) { return Byte(name); }
      public short Short(string name) { return _data.ReadShort(_location += 2); }
      public short ShortEnum(string name, dynamic[] names) { return Short(name); }
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
