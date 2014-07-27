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
      byte ByteEnum(string name, IEnumerable<dynamic> enumerableNames);
      short Short(string name);
      short ShortEnum(string name, dynamic[] names);
      int Word(string name);
      short Species(dynamic[] names);
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
         var result = _runs.Segment[_location];
         _location++;
         _result[name] = result;
         return result;
      }

      public byte ByteEnum(string name, IEnumerable<dynamic> enumerableNames) {
         var names = enumerableNames.ToArray();
         var val = Byte(name);
         if (val >= names.Length) FaultReason = name + ": " + val + " larger than available range: " + names.Length;
         return val;
      }

      public short Short(string name) {
         if (FaultReason != null) return 0;
         var result = (short)_runs.Segment.Read(_location, 2);
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
         var result = _runs.Segment.Read(_location, 4);
         _location += 4;
         _result[name] = result;
         return result;
      }

      public byte ByteNum(string name) { return Byte(name); }
      public short Species(dynamic[] names) { return Short("species"); }

      public void Pointer(string name) {
         if (FaultReason != null) return;
         var pointer = _runs.Segment.Follow(_location);
         _location += 4;
         if (pointer == null) {
            FaultReason = name + ": not a pointer";
            return;
         }
         _result[name] = null;
      }

      public dynamic Pointer(string name, ChildReader reader) {
         if (FaultReason != null) return null;
         var pointer = _runs.Segment.Follow(_location);
         _location += 4;
         if (pointer == null) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var child = new Parser(_runs, _pcs, pointer.Location);
         reader(child);
         FaultReason = child.FaultReason;
         if (FaultReason != null) return null;
         _result[name] = child.Result;
         return child.Result;
      }

      public string StringPointer(string name) {
         if (FaultReason != null) return null;
         var pointer = _runs.Segment.Follow(_location);
         _location += 4;
         if (pointer == null) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var str = _pcs.ReadString(pointer);
         if (str == null) {
            FaultReason = name + ": not a string";
            return null;
         }
         return str;
      }

      public void NullablePointer(string name) {
         if (FaultReason != null) return;
         if (_runs.Segment.Read(_location, 4) == 0) {
            _location += 4;
            _result[name] = null;
            return;
         }
         Pointer(name);
      }

      public dynamic NullablePointer(string name, ChildReader reader) {
         if (FaultReason != null) return null;
         if (_runs.Segment.Read(_location, 4) == 0) {
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
         if (_runs.Segment.Read(_location, 4) == 0) {
            _location += 4;
            _result[name] = null;
            return null;
         }
         var pointer = _runs.Segment.Follow(_location);
         _location += 4;
         if (pointer == null) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var child = new Parser(_runs, _pcs, pointer.Location);
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
         string result = _pcs.ReadString(_runs.Segment.Inner(_location), len);
         if (result == null) {
            FaultReason = name + " was not a string[" + len + "]";
            return null;
         }
         _location += len;
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

   public class BuilderCache {
      public readonly IRunStorage Runs;
      public readonly IPointerMapper Mapper;
      public readonly PCS Pcs;
      public readonly IDictionary<string, IDataRun> ByteRuns;
      public readonly IList<InlineComboEditor> ByteEnumEditors;
      public readonly IList<SimpleDataRun> ByteEnumRuns;
      public readonly IDictionary<string, IDataRun> ByteNumRuns;
      public readonly IDictionary<string, IDataRun> ShortRuns;
      public readonly IList<InlineComboEditor> ShortEnumEditors;
      public readonly IList<SimpleDataRun> ShortEnumRuns;
      public readonly IDictionary<string, IDataRun> WordRuns;
      public readonly IDataRun UnusedRun;

      public IDataRun SpeciesRun { get; private set; }
      public InlineComboEditor SpeciesEnumEditor { get; private set; }

      public BuilderCache(IRunStorage runs, IPointerMapper mapper, PCS pcs) {
         Runs = runs;
         Mapper = mapper;
         Pcs = pcs;

         ByteRuns = new Dictionary<string, IDataRun>();
         ByteEnumEditors = new List<InlineComboEditor>();
         ByteEnumRuns = new List<SimpleDataRun>();
         ByteNumRuns = new Dictionary<string, IDataRun>();
         ShortRuns = new Dictionary<string, IDataRun>();
         ShortEnumEditors = new List<InlineComboEditor>();
         ShortEnumRuns = new List<SimpleDataRun>();
         WordRuns = new Dictionary<string, IDataRun>();
         UnusedRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Unused), 1);
      }

      BuilderCache(BuilderCache other) {
         ByteRuns = other.ByteRuns;
         ByteEnumEditors = other.ByteEnumEditors;
         ByteEnumRuns = other.ByteEnumRuns;
         ByteNumRuns = other.ByteNumRuns;
         ShortRuns = other.ShortRuns;
         ShortEnumEditors = other.ShortEnumEditors;
         ShortEnumRuns = other.ShortEnumRuns;
         WordRuns = other.WordRuns;
         UnusedRun = other.UnusedRun;

         Runs = new NullRunStorage();
         Mapper = new NullPointerMapper();
         Pcs = other.Pcs;
         SpeciesRun = other.SpeciesRun;
         SpeciesEnumEditor = other.SpeciesEnumEditor;
      }

      public void SetupSpecies(dynamic[] names) {
         if (SpeciesEnumEditor != null) {
            Debug.Assert(SpeciesEnumEditor.Names.Length == names.Length && Enumerable.Range(0, names.Length).All(i => SpeciesEnumEditor.Names[i].ToString() == names[i].ToString()));
            return;
         }
         SpeciesEnumEditor = new InlineComboEditor(2, names, "species");
         SpeciesRun = new SimpleDataRun(new SpeciesElementProvider(names), 2, SpeciesEnumEditor);
      }

      public BuilderCache GetFixed() { return new BuilderCache(this); }
   }

   class Builder : IBuilder {
      static IElementProvider hex(string hoverText = null) { return new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Number, hoverText: hoverText); }
      static IElementProvider nums(string hoverText = null) { return new GeometryElementProvider(Utils.NumericFlyweights, GbaBrushes.Number, hoverText: hoverText); }
      static IElementProvider enums(dynamic[] names, int stride, string hoverText = null) { return new EnumElementProvider(names, stride, GbaBrushes.Enum, hoverText: hoverText); }

      static IDataRun Build(IDictionary<string, IDataRun> dict, Func<string, IElementProvider> func, int len, string hoverText = "", IEditor editor = null) {
         if (dict.ContainsKey(hoverText)) return dict[hoverText];
         var run = new SimpleDataRun(func(hoverText == "" ? null : hoverText), len, editor);
         dict[hoverText] = run;
         return run;
      }

      readonly BuilderCache _cache;
      readonly ISegment _segment;
      int _location;

      public BuildableObject Result { get; private set; }

      public Builder(BuilderCache cache, ISegment segment) {
         _cache = cache;
         _segment = segment;
         Result = new BuildableObject(_segment, _cache);
      }

      public void Clear() { Result = new BuildableObject(_segment.Inner(_location), _cache); }

      public bool Assert(bool value, string message) {
         Debug.Assert(value, message);
         return value;
      }

      public byte Byte(string name) {
         _cache.Runs.AddRun(_segment.Location + _location, Build(_cache.ByteRuns, hex, 1, name));
         var value = _segment[_location];
         _location++;
         Result.AppendByte(name);
         return value;
      }

      public byte ByteEnum(string name, IEnumerable<dynamic> enumerableNames) {
         var names = enumerableNames.ToArray();

         var editor = _cache.ByteEnumEditors.FirstOrDefault(e => e.Names.Equals(names) && e.HoverText == name);
         if (editor == null) {
            editor = new InlineComboEditor(1, names, name);
            _cache.ByteEnumEditors.Add(editor);
         }
         var run = _cache.ByteEnumRuns.FirstOrDefault(r => r.Editor == editor);
         if (run == null) {
            run = new SimpleDataRun(enums(names, 1, name), 1, editor);
            _cache.ByteEnumRuns.Add(run);
         }
         _cache.Runs.AddRun(_segment.Location + _location, run);

         var value = _segment[_location];
         _location += 1;
         Result.AppendByte(name);
         return value;
      }

      IEditor _inlineByteNumEditor;
      IEditor InlineByteNumEditor {
         get {
            return _inlineByteNumEditor ?? (_inlineByteNumEditor = new InlineTextEditor(1, array => array[0].ToString(), str => new GbaSegment(new[] { byte.Parse(str) }, 0)));
         }
      }
      public byte ByteNum(string name) {
         _cache.Runs.AddRun(_segment.Location + _location, Build(_cache.ByteNumRuns, nums, 1, name, InlineByteNumEditor));
         var value = _segment[_location];
         _location++;
         Result.AppendByte(name);
         return value;
      }

      public short Short(string name) {
         _cache.Runs.AddRun(_segment.Location + _location, Build(_cache.ShortRuns, hex, 2, name));
         var value = (short)_segment.Read(_location, 2);
         _location += 2;
         Result.AppendShort(name);
         return value;
      }

      public short ShortEnum(string name, dynamic[] names) {
         var editor = _cache.ShortEnumEditors.FirstOrDefault(e => e.Names == names && e.HoverText == name);
         if (editor == null) {
            editor = new InlineComboEditor(2, names, name);
            _cache.ShortEnumEditors.Add(editor);
         }
         var run = _cache.ShortEnumRuns.FirstOrDefault(r => r.Editor == editor);
         if (run == null) {
            run = new SimpleDataRun(enums(names, 2, name), 2, editor);
            _cache.ShortEnumRuns.Add(run);
         }
         _cache.Runs.AddRun(_segment.Location + _location, run);

         var value = _segment[_location];
         _location += 2;
         Result.AppendShort(name);
         return value;
      }

      public int Word(string name) {
         _cache.Runs.AddRun(_segment.Location + _location, Build(_cache.WordRuns, hex, 4, name));
         var value = _segment.Read(_location, 4);
         _location += 4;
         Result.AppendWord(name);
         return value;
      }

      public short Species(dynamic[] names) {
         _cache.SetupSpecies(names);
         _cache.Runs.AddRun(_segment.Location + _location, _cache.SpeciesRun);

         var value = (short)_segment.Read(_location, 2);
         _location += 2;
         Result.AppendShort("species");
         return value;
      }

      public void Pointer(string name) {
         var pointer = _segment.Read(_location, 4) - 0x08000000;
         _cache.Mapper.Claim(_cache.Runs, _segment.Location + _location, pointer);
         _location += 4;
         Result.AppendSkip(4);
      }

      public dynamic Pointer(string name, ChildReader reader) {
         var pointer = _segment.Read(_location, 4) - 0x08000000;
         _cache.Mapper.Claim(_cache.Runs, _segment.Location + _location, pointer);
         var child = new Builder(_cache, _segment.Follow(_location));
         _location += 4;
         reader(child);
         Result.Append(name, reader);
         return child.Result;
      }

      public string StringPointer(string name) {
         var pointer = _segment.Read(_location, 4) - 0x08000000;
         _cache.Mapper.Claim(_cache.Runs, _segment.Location + _location, pointer);
         var str = _cache.Pcs.ReadString(_segment.Follow(_location));
         _cache.Runs.AddRun(pointer, _cache.Pcs.StringRun);
         _location += 4;
         Result.AppendStringPointer(name);
         return str;
      }

      public void NullablePointer(string name) {
         if (_segment.Read(_location, 4) == 0) {
            _location += 4;
            Result.AppendSkip(4);
            return;
         }
         Pointer(name);
      }

      public dynamic NullablePointer(string name, ChildReader reader) {
         if (_segment.Read(_location, 4) == 0) {
            _location += 4;
            Result.Append(name, null);
            return null;
         }
         return Pointer(name, reader);
      }

      public void InlineArray(string name, int length, ChildReader reader) {
         int start = _location;
         for (int i = 0; i < length; i++) {
            var child = new Builder(_cache, _segment.Inner(_location));
            reader(child);
            _location += child._location;
         }
         Result.AppendInlineArray(name, length, _location - start, reader);
      }

      public dynamic Array(string name, int length, ChildReader reader) {
         if (_segment.Read(_location, 4) == 0) {
            _location += 4;
            Result.AppendSkip(4);
            return null;
         }
         var pointer = _segment.Read(_location, 4) - 0x08000000;
         if (_cache.Mapper != null) _cache.Mapper.Claim(_cache.Runs, _segment.Location + _location, pointer);
         var child = new Builder(_cache, _segment.Follow(_location));
         _location += 4;
         for (int i = 0; i < length; i++) {
            child.Clear();
            reader(child);
         }
         Result.AppendArray(name, length, reader);
         return null;
      }

      public string String(int len, string name) {
         _cache.Runs.AddRun(_segment.Location + _location, _cache.Pcs.StringRun);
         string result = _cache.Pcs.ReadString(_segment.Inner(_location));
         _location += len;
         Result.AppendString(name, len);
         return result;
      }

      public void Unused(int len) {
         Result.AppendSkip(len);
         while (len > 0) {
            if (_cache.Runs != null) _cache.Runs.AddRun(_segment.Location + _location, _cache.UnusedRun);
            _location++;
            len--;
         }
      }

      public void Link(int len, string name, ChildJump jump) {
         Result.AppendSkip(len);
         var run = new SimpleDataRun(new JumpElementProvider(jump), len);
         if (_cache.Runs != null) _cache.Runs.AddRun(_segment.Location + _location, run);
         _location += len;
      }

      public void WriteDebug(object o) { MessageBox.Show(MultiBoxControl.Parse(o)); }
   }

   class Reader : IBuilder {
      readonly ISegment _segment;
      int _location;
      public Reader(ISegment segment) { _segment = segment; }

      public bool Assert(bool value, string message) { return true; }
      public byte Byte(string name) { return _segment[_location++]; }
      public byte ByteEnum(string name, IEnumerable<dynamic> enumerableNames) { return Byte(name); }
      public byte ByteNum(string name) { return Byte(name); }
      public short Short(string name) { return (short)_segment.Read(_location += 2, 2); }
      public short ShortEnum(string name, dynamic[] names) { return Short(name); }
      public int Word(string name) { return _segment.Read(_location += 4, 4); }
      public short Species(dynamic[] names) { return Short(null); }
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
