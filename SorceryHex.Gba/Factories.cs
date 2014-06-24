
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Dynamic;
using System.Windows;

namespace SorceryHex.Gba {
   [Export(typeof(IModelFactory))]
   public class PokemonFactory : IModelFactory {
      public string DisplayName { get { return "Pokemon Gba Game"; } }

      public bool CanCreateModel(string name, byte[] data) {
         if (!name.ToLower().EndsWith("gba")) return false;
         var code = Header.GetCode(data);

         //           ruby            sapphire          emerald
         if (code == "AXVE" || code == "AXPE" || code == "BPEE") return true;

         //          firered          leafgreen
         if (code == "BPRE" || code == "BPGE") return true;

         return false;
      }

      public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
         var pointerMapper = new PointerMapper(data);
         // var maps = new Maps(pointerMapper);
         var storage = new RunStorage(data
            , new Header(pointerMapper)
            , new Thumbnails(pointerMapper)
            , new Lz(pointerMapper)
            , new ScriptedDataTypes(pointerMapper, scriptInfo.Engine, scriptInfo.Scope, "maps.rb", "wild.rb", "trainer.rb")
            // , maps
            // , new WildData(pointerMapper, maps)
            , new PCS()
         );
         IModel model = new CompositeModel(data, storage);
         model = new PointerParser(model, data, storage, pointerMapper);
         return model;
      }

      public int CompareTo(IModelFactory other) {
         if (other is StandardFactory) return 1;
         if (other is DefaultFactory) return 1;
         if (other is SimpleFactory) return 1;
         return 0;
      }
   }

   [Export(typeof(IModelFactory))]
   public class StandardFactory : IModelFactory {
      public string DisplayName { get { return "Gba Game"; } }

      public bool CanCreateModel(string name, byte[] data) {
         return name.ToLower().EndsWith("gba");
      }

      public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
         var pointerMapper = new Gba.PointerMapper(data);
         var storage = new RunStorage(data
            , new Gba.Header(pointerMapper)
            , new Gba.Lz(pointerMapper)
         );
         IModel model = new CompositeModel(data, storage);
         model = new Gba.PointerParser(model, data, storage, pointerMapper);
         return model;
      }

      public int CompareTo(IModelFactory other) {
         if (other is PokemonFactory) return -1;
         if (other is DefaultFactory) return 1;
         if (other is SimpleFactory) return 1;
         return 0;
      }
   }

   public class Pointer {
      public int source;
      public int destination;
      public dynamic data;
      public override string ToString() {
         return source.ToHexString(6) + " -> " + destination.ToHexString(6);
      }
   }

   namespace Pokemon.DataTypes {
      public delegate void ChildReader(IBuilder builder);
      public delegate int ChildJump(IBuilder builder);

      public interface ITypes {
         string Version { get; }
         Pointer FindVariableArray(string generalLayout, ChildReader reader);
         Pointer FindVariableArray(byte ender, string generalLayout, ChildReader reader);
         Pointer[] FindMany(string generalLayout, ChildReader reader);
         Pointer[] FollowPointersUp(Pointer[] locations);
      }

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
            string result = PCS.ReadString(_runs.Data, _location);
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

      class Factory : IBuilder {
         static IElementProvider hex(string hoverText = null) { return new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Number, hoverText: hoverText); }
         static IElementProvider nums(string hoverText = null) { return new GeometryElementProvider(Utils.NumericFlyweights, GbaBrushes.Number, hoverText: hoverText); }

         static IDataRun Build(IDictionary<string, IDataRun> dict, Func<string, IElementProvider> func, int len, string hoverText = "") {
            if (dict.ContainsKey(hoverText)) return dict[hoverText];
            var run = new SimpleDataRun(func(hoverText == "" ? null : hoverText), len);
            dict[hoverText] = run;
            return run;
         }

         readonly IDictionary<string, object> _result = new ExpandoObject();
         readonly IRunStorage _runs;
         readonly PointerMapper _mapper;
         int _location;

         public dynamic Result { get { return _result; } }

         public Factory(IRunStorage runs, PointerMapper mapper, int location) { _runs = runs; _mapper = mapper; _location = location; }

         public bool Assert(bool value, string message) {
            Debug.Assert(value, message);
            return value;
         }

         static readonly IDictionary<string, IDataRun> _byteRuns = new Dictionary<string, IDataRun>();
         public byte Byte(string name) {
            _runs.AddRun(_location, Build(_byteRuns, hex, 1, name));
            var value = _runs.Data[_location];
            _location++;
            _result[name] = value;
            return value;
         }

         static readonly IDictionary<string, IDataRun> _byteNumRuns = new Dictionary<string, IDataRun>();
         public short ByteNum(string name) {
            _runs.AddRun(_location, Build(_byteNumRuns, nums, 1, name));
            var value = _runs.Data[_location];
            _location++;
            _result[name] = value;
            return value;
         }

         static readonly IDictionary<string, IDataRun> _shortRuns = new Dictionary<string, IDataRun>();
         public short Short(string name) {
            _runs.AddRun(_location, Build(_shortRuns, hex, 2, name));
            var value = _runs.Data.ReadShort(_location);
            _location += 2;
            _result[name] = value;
            return value;
         }

         static readonly IDictionary<string, IDataRun> _wordRuns = new Dictionary<string, IDataRun>();
         public int Word(string name) {
            _runs.AddRun(_location, Build(_wordRuns, hex, 4, name));
            var value = _runs.Data.ReadData(4, _location);
            _location += 4;
            _result[name] = value;
            return value;
         }

         static readonly IDataRun _speciesRun = new SimpleDataRun(SpeciesElementProvider.Instance, 2);
         public short Species() {
            _runs.AddRun(_location, _speciesRun);
            var value = _runs.Data.ReadShort(_location);
            _location += 2;
            _result["species"] = value;
            return value;
         }

         public void Pointer(string name) {
            var pointer = _runs.Data.ReadPointer(_location);
            _mapper.Claim(_runs, _location, pointer);
            _location += 4;
            _result[name] = null;
         }

         public dynamic Pointer(string name, ChildReader reader) {
            var pointer = _runs.Data.ReadPointer(_location);
            _mapper.Claim(_runs, _location, pointer);
            _location += 4;
            var child = new Factory(_runs, _mapper, pointer);
            reader(child);
            _result[name] = child.Result;
            return child.Result;
         }

         public void NullablePointer(string name) {
            if (_runs.Data.ReadData(4, _location) == 0) {
               _location += 4;
               _result[name] = null;
               return;
            }
            Pointer(name);
         }

         public dynamic NullablePointer(string name, ChildReader reader) {
            if (_runs.Data.ReadData(4, _location) == 0) {
               _location += 4;
               _result[name] = null;
               return null;
            }
            return Pointer(name, reader);
         }

         public dynamic Array(string name, int length, ChildReader reader) {
            if (_runs.Data.ReadData(4, _location) == 0) {
               _location += 4;
               _result[name] = null;
               return null;
            }
            var pointer = _runs.Data.ReadPointer(_location);
            _mapper.Claim(_runs, _location, pointer);
            _location += 4;
            var child = new Factory(_runs, _mapper, pointer);
            var array = new dynamic[length];
            for (int i = 0; i < array.Length; i++) {
               reader(child);
               array[i] = child.Result;
            }
            _result[name] = array;
            return array;
         }

         public string String(int len, string name) {
            _runs.AddRun(_location, PCS.StringRun);
            string result = PCS.ReadString(_runs.Data, _location);
            _location += len;
            _result[name] = result;
            return result;
         }

         static readonly IDataRun _unusedRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Unused), 1);
         public void Unused(int len) {
            while (len > 0) {
               _runs.AddRun(_location, _unusedRun);
               _location++;
               len--;
            }
         }

         public void Link(int len, string name, ChildJump jump) {
            var run = new SimpleDataRun(new JumpElementProvider(jump), len);
            _runs.AddRun(_location, run);
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
}
