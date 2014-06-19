
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Dynamic;

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
            , new ScriptedDataTypes(pointerMapper, scriptInfo.Engine, scriptInfo.Scope, "maps.rb")
            // , maps
            // , new WildData(pointerMapper, maps)
            , new PCS()
         );
         IModel model = new CompositeModel(data, storage);
         model = new PointerParser(model, data, storage, pointerMapper);
         return model;
      }

      public int CompareTo(IModelFactory other) {
         if (other is GbaFactory) return 1;
         if (other is DefaultFactory) return 1;
         if (other is SimpleFactory) return 1;
         return 0;
      }
   }

   [Export(typeof(IModelFactory))]
   public class GbaFactory : IModelFactory {
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

   public delegate void ChildReader(IPokemonDatatypeBuilder factory);

   public class GbaPointer {
      public int source;
      public int destination;
      public override string ToString() {
         return source.ToHexString(6) + " -> " + destination.ToHexString(6);
      }
   }

   public interface IDataTypeFinder {
      int FindOne(ChildReader reader);
      GbaPointer[] FindMany(ChildReader reader);
      GbaPointer[] FollowPointersUp(GbaPointer[] locations);
   }

   public interface IPokemonDatatypeBuilder {
      dynamic Result { get; }
      byte ReadByte(string name);
      short ReadShort(string name);
      int ReadWord(string name);
      void ReadPointer(string name);
      dynamic ReadPointer(string name, ChildReader reader);
      void ReadNullablePointer(string name);
      dynamic ReadNullablePointer(string name, ChildReader reader);
      dynamic ReadArray(string name, int length, ChildReader reader);
      dynamic ReadDynamicArray(string name, int stride, byte ender, ChildReader reader);
   }

   class PokemonDataTypeParser : IPokemonDatatypeBuilder {
      readonly IDictionary<string, object> _result = new ExpandoObject();
      readonly IRunStorage _runs;
      int _location;

      public string FaultReason { get; private set; }
      public dynamic Result { get { return _result; } }

      public PokemonDataTypeParser(IRunStorage runs, int location) {
         _runs = runs;
         _location = location;
      }

      public byte ReadByte(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data[_location];
         _location++;
         _result[name] = result;
         return result;
      }

      public short ReadShort(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data.ReadShort(_location);
         _location += 2;
         _result[name] = result;
         return result;
      }

      public int ReadWord(string name) {
         if (FaultReason != null) return 0;
         var result = _runs.Data.ReadData(4, _location);
         _location += 4;
         _result[name] = result;
         return result;
      }

      public void ReadPointer(string name) {
         if (FaultReason != null) return;
         var pointer = _runs.Data.ReadPointer(_location);
         _location += 4;
         if (pointer == -1) {
            FaultReason = name + ": not a pointer";
            return;
         }
         _result[name] = null;
      }

      public dynamic ReadPointer(string name, ChildReader reader) {
         if (FaultReason != null) return null;
         var pointer = _runs.Data.ReadPointer(_location);
         _location += 4;
         if (pointer == -1) {
            FaultReason = name + ": not a pointer";
            return null;
         }
         var child = new PokemonDataTypeParser(_runs, pointer);
         reader(child);
         FaultReason = child.FaultReason;
         if (FaultReason != null) return null;
         _result[name] = child.Result;
         return child.Result;
      }

      public void ReadNullablePointer(string name) {
         if (FaultReason != null) return;
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return;
         }
         ReadPointer(name);
      }

      public dynamic ReadNullablePointer(string name, ChildReader reader) {
         if (FaultReason != null) return null;
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return null;
         }
         return ReadPointer(name, reader);
      }

      public dynamic ReadArray(string name, int length, ChildReader reader) {
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
         var child = new PokemonDataTypeParser(_runs, pointer);
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

      public dynamic ReadDynamicArray(string name, int stride, byte ender, ChildReader reader) {
         throw new NotImplementedException();
      }
   }

   class PokemonDatatypeFactory : IPokemonDatatypeBuilder {
      static readonly IElementProvider _hex = new GeometryElementProvider(Utils.ByteFlyweights, GbaBrushes.Number);
      static readonly IElementProvider _nums = new GeometryElementProvider(Utils.NumericFlyweights, GbaBrushes.Number);
      readonly IDictionary<string, object> _result = new ExpandoObject();
      readonly IRunStorage _runs;
      readonly PointerMapper _mapper;
      int _location;

      public dynamic Result { get { return _result; } }

      public PokemonDatatypeFactory(IRunStorage runs, PointerMapper mapper, int location) { _runs = runs; _mapper = mapper; _location = location; }

      static readonly IDataRun _byteRun = new SimpleDataRun(_hex, 1);
      public byte ReadByte(string name) {
         _runs.AddRun(_location, _byteRun);
         var value = _runs.Data[_location];
         _location++;
         return value;
      }

      static readonly IDataRun _shortRun = new SimpleDataRun(_hex, 2);
      public short ReadShort(string name) {
         _runs.AddRun(_location, _shortRun);
         var value = _runs.Data.ReadShort(_location);
         _location += 2;
         return value;
      }

      static readonly IDataRun _wordRun = new SimpleDataRun(_hex, 4);
      public int ReadWord(string name) {
         _runs.AddRun(_location, _wordRun);
         var value = _runs.Data.ReadData(4, _location);
         _location += 4;
         return value;
      }

      public void ReadPointer(string name) {
         var pointer = _runs.Data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         _result[name] = null;
      }

      public dynamic ReadPointer(string name, ChildReader reader) {
         var pointer = _runs.Data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = new PokemonDatatypeFactory(_runs, _mapper, pointer);
         reader(child);
         _result[name] = child.Result;
         return child.Result;
      }

      public void ReadNullablePointer(string name) {
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return;
         }
         ReadPointer(name);
      }

      public dynamic ReadNullablePointer(string name, ChildReader reader) {
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return null;
         }
         return ReadPointer(name, reader);
      }

      public dynamic ReadArray(string name, int length, ChildReader reader) {
         if (_runs.Data.ReadData(4, _location) == 0) {
            _location += 4;
            _result[name] = null;
            return null;
         }
         var pointer = _runs.Data.ReadPointer(_location);
         _mapper.Claim(_runs, _location, pointer);
         _location += 4;
         var child = new PokemonDatatypeFactory(_runs, _mapper, pointer);
         var array = new dynamic[length];
         for (int i = 0; i < array.Length; i++) {
            reader(child);
            array[i] = child.Result;
         }
         _result[name] = array;
         return array;
      }

      public dynamic ReadDynamicArray(string name, int stride, byte ender, ChildReader reader) {
         throw new NotImplementedException();
      }
   }
}
