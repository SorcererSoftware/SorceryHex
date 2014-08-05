using Microsoft.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SorceryHex.Gba.Pokemon.DataTypes {
   public class Pointer {
      public int source;
      public int destination;
      public dynamic data;
      public override string ToString() {
         return source.ToHexString(6) + " -> " + destination.ToHexString(6);
      }
   }

   public interface ITypes {
      string Version { get; }
      Pointer FindVariableArray(string generalLayout, ChildReader reader);
      Pointer FindVariableArray(byte ender, string generalLayout, ChildReader reader);
      BuildableObject[] ReadArray(int length, int location, ChildReader reader);
      Pointer ReadPointer(int location, string generalLayout, ChildReader reader);
      Pointer[] FindMany(string generalLayout, ChildReader reader);
      Pointer[] FollowPointersUp(Pointer[] locations);
      void Label(BuildableObject[] array, Func<int, string> label);
      void AddShortcut(string name, int location);
      void WaitFor(string name);
   }

   class ScriptedDataTypes : IRunParser, ITypes, ILabeler {
      readonly PointerMapper _mapper;
      readonly PCS _pcs;
      readonly ScriptInfo _scriptInfo;

      public ScriptedDataTypes(PointerMapper mapper, PCS pcs, ScriptInfo scriptInfo) {
         _mapper = mapper; _pcs = pcs; _scriptInfo = scriptInfo;
      }

      public IEnumerable<int> Find(string term) {
         term = term.ToLower();
         foreach (var key in _arrays.Keys.OrderBy(i => i)) {
            int stride = _arrays[key][0].Length;
            for (int i = 0; i < _arrays[key].Length; i++) {
               if (_labels[key](i).ToLower() == term) {
                  yield return key + i * stride;
               }
            }
         }
      }

      ICommandFactory _commander;
      IRunStorage _runs;
      BuilderCache _cache;
      public void Load(ICommandFactory commander, IRunStorage runs) {
         _commander = commander;
         _runs = runs;
         _cache = new BuilderCache(_runs, _mapper, _pcs);
         _tasks.Clear();
         _scriptInfo.Scope.SetVariable("types", this);
         var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory + "/pokemon_datatypes/");
         var files = dir.EnumerateFiles("*.rb").OrderBy(file => file.Name).ToArray();
         foreach (var script in files) {
            var currentScript = script; // closure
            var t = new Task(() => {
               try {
                  using (AutoTimer.Time("ScriptedDataTypes-" + currentScript.Name)) {
                     var source = _scriptInfo.Engine.CreateScriptSourceFromFile(currentScript.FullName);
                     source.Execute(_scriptInfo.Scope);
                  }
               } catch (Exception e) {
                  commander.LogError(currentScript.Name + ": " + e.Message);
               }
            });
            _tasks[currentScript.Name.Split('.')[0]] = t;
         }
         // _tasks.Values.Foreach(t => t.RunSynchronously());
         _tasks.Values.Foreach(t => t.Start());
         _tasks.Values.Foreach(t => t.Wait());
         _runs.AddLabeler(this);
      }

      #region ILabeler

      readonly IDictionary<int, BuildableObject[]> _arrays = new Dictionary<int, BuildableObject[]>();
      readonly IDictionary<int, Func<int, string>> _labels = new Dictionary<int, Func<int, string>>();
      public string GetLabel(int index) {
         var list = _labels.Keys.Where(i => i < index).OrderBy(i => i).ToList();
         if (list.Count == 0) return null;
         int location = list.Last();
         int stride = _arrays[location][0].Length;
         if ((index - location) % stride != 0) return null;
         int dex = (index - location) / stride;
         if (dex >= _arrays[location].Length) return null;
         return _labels[location](dex);
      }

      #endregion

      #region ITypes

      public string Version { get { return Header.GetCode(_runs.Segment); } }

      const int MinVariableLength = 10;
      public Pointer FindVariableArray(string generalLayout, ChildReader reader) {
         int stride = generalLayout.Length * 4;

         var addressesList = _mapper.OpenDestinations.ToList();
         var matchingPointers = new List<int>();
         var matchingLayouts = new List<int>();
         var matchingLengths = new List<int>();
         foreach (var address in addressesList) {
            int elementCount1 = 0, elementCount2 = 0;
            while (GeneralMatch(_runs.Segment.Inner(address + elementCount1 * stride), generalLayout)) elementCount1++;
            if (address + stride * elementCount1 >= _runs.Segment.Length) continue;
            if (elementCount1 < MinVariableLength) continue;

            var parser = new Parser(_runs, _pcs, location: address);
            for (int i = 0; true; i++) {
               reader(parser);
               if (parser.FaultReason != null) break;
               elementCount2++;
            }
            if (elementCount2 < MinVariableLength) continue;
            matchingLayouts.Add(address);
            matchingLengths.Add(elementCount2);
            matchingPointers.Add(_mapper.PointersFromDestination(address).First());
         }

         if (matchingLayouts.Count == 0) {
            Debug.Fail("No layouts matching " + generalLayout);
            return null;
         }

         return FindVariableArray(reader, stride, matchingPointers, matchingLayouts, matchingLengths);
      }

      public Pointer FindVariableArray(byte ender, string generalLayout, ChildReader reader) {
         int stride = generalLayout.Length * 4;

         var addressesList = _mapper.OpenDestinations.ToList();
         var matchingPointers = new List<int>();
         var matchingLayouts = new List<int>();
         var matchingLengths = new List<int>();
         foreach (var address in addressesList) {
            int elementCount = 0;
            while (address + stride * elementCount < _runs.Segment.Length && _runs.Segment[address + stride * elementCount] != ender) {
               elementCount++;
            }

            if (address + stride * elementCount >= _runs.Segment.Length) continue;
            if (elementCount < MinVariableLength) continue;
            if (Enumerable.Range(0, elementCount).Any(i => !GeneralMatch(_runs.Segment.Inner(address + i * stride), generalLayout))) continue;

            var parser = new Parser(_runs, _pcs, location: address);
            for (int i = 0; i < elementCount; i++) {
               reader(parser);
               if (parser.FaultReason != null) break;
            }
            if (parser.FaultReason != null) continue;
            matchingLayouts.Add(address);
            matchingLengths.Add(elementCount);
            matchingPointers.Add(_mapper.PointersFromDestination(address).First());
         }

         if (matchingLayouts.Count == 0) {
            Debug.Fail("No layouts matching " + generalLayout);
            return null;
         }

         return FindVariableArray(reader, stride, matchingPointers, matchingLayouts, matchingLengths);
      }

      public BuildableObject[] ReadArray(int length, int location, ChildReader reader) {
         var destination = _runs.Segment.Follow(location);
         if (destination == null) return null;
         var parser = new Parser(_runs, _pcs, location: destination.Location);
         var builder = new Builder(_cache, destination);
         var array = new BuildableObject[length];
         for (int i = 0; i < length; i++) {
            reader(parser);
            if (parser.FaultReason != null) return null;
            builder.Clear();
            reader(builder);
            array[i] = builder.Result;
         }
         _mapper.Claim(_runs, location, destination.Location);
         return array;
      }

      public Pointer ReadPointer(int location, string generalLayout, ChildReader reader) {
         var destination = _runs.Segment.Follow(location);
         if (destination == null) return null;
         return ReadPointerHelper(destination, generalLayout, reader);
      }

      public Pointer[] FindMany(string generalLayout, ChildReader reader) {
         var matchingPointers = new List<Pointer>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            var p = ReadPointerHelper(_runs.Segment.Inner(address), generalLayout, reader);
            if (p == null) continue;
            matchingPointers.Add(p);
         }
         return matchingPointers.ToArray();
      }

      /// <summary>
      /// Given a list of pointers, finds the ones that are pointed to.
      /// Returns the pointers pointing to the list.
      /// </summary>
      public Pointer[] FollowPointersUp(Pointer[] locations) {
         var pointerSet = new List<Pointer>();
         foreach (var destination in locations) {
            var pointers = _mapper.PointersFromDestination(destination.source);
            if (pointers == null || pointers.Length == 0) continue;
            var p = new Pointer {
               source = pointers.First(),
               destination = destination.source,
               data = new AutoArray(_runs.Segment.Inner(destination.source), locations)
            };
            pointerSet.Add(p);
         }
         return pointerSet.ToArray();
      }

      public void Label(BuildableObject[] array, Func<int, string> label) {
         _arrays[array[0].Location] = array;
         _labels[array[0].Location] = label;
         // array.Label(_runs, label);
      }

      public void AddShortcut(string name, int location) { _commander.CreateJumpShortcut(name, location); }

      readonly IDictionary<string, Task> _tasks = new Dictionary<string, Task>();
      public void WaitFor(string name) {
         _tasks[name].Wait();
      }

      #endregion

      #region Helpers

      bool GeneralMatch(ISegment segment, string layout) {
         layout = layout.ToLower();
         if (segment.Location + layout.Length * 4 > _runs.Segment.Length) return false;
         for (int i = 0; i < layout.Length; i++) {
            Debug.Assert(layout[i] == 'p' || layout[i] == 'w');
            if (layout[i] != 'p') continue;
            int value = segment.Read(i * 4, 4);
            var pointer = segment.Follow(i * 4);
            if (pointer == null && value != 0) return false;
         }
         return true;
      }

      Pointer FindVariableArray(ChildReader reader, int stride, IList<int> matchingPointers, IList<int> matchingLayouts, IList<int> matchingLengths) {
         var counts = new List<double>();
         for (int i = 0; i < matchingLayouts.Count; i++) {
            var layout = matchingLayouts[i];
            var length = matchingLengths[i];
            int repeatCount = 0;
            byte prev = _runs.Segment[layout];
            for (int j = 1; j < length; j++) {
               var current = _runs.Segment[layout + j];
               if (prev == current) repeatCount++;
               prev = current;
            }
            counts.Add((double)repeatCount / length);
         }

         var least = counts.Min();
         var index = counts.IndexOf(least);

         int offset = matchingLayouts[index];
         var factory = new Builder(_cache, _runs.Segment.Inner(offset));
         var data = new BuildableObject[matchingLengths[index]];
         for (int i = 0; i < matchingLengths[index]; i++) {
            factory.Clear();
            reader(factory);
            data[i] = factory.Result;
         }

         int start = matchingLayouts[index], end = matchingLayouts[index] + matchingLengths[index] * stride;
         _mapper.FilterPointer(i => i <= start || i >= end);
         _mapper.Claim(_runs, matchingLayouts[index]);
         return new Pointer { source = matchingPointers[index], destination = matchingLayouts[index], data = data };
      }

      Pointer ReadPointerHelper(ISegment segment, string generalLayout, ChildReader reader) {
         if (!GeneralMatch(segment, generalLayout)) return null;
         var parser = new Parser(_runs, _pcs, location: segment.Location);
         reader(parser);
         if (parser.FaultReason != null) return null; ;
         var factory = new Builder(_cache, segment);
         reader(factory);

         return new Pointer {
            source = _mapper.PointersFromDestination(segment.Location).First(),
            destination = segment.Location,
            data = factory.Result
         };
      }

      #endregion
   }

   public class AutoArray {
      readonly ISegment _data;
      readonly IList<Pointer> _pointers;
      public AutoArray(ISegment data, IList<Pointer> pointers) { _data = data; _pointers = pointers; }
      public dynamic this[int i] {
         get {
            var r = _pointers.FirstOrDefault(p => p.destination == _data.Follow(i * 4).Location);
            if (r != null) return r.data;
            return null;
         }
      }
      public int destinationof(int i) {
         var r = _pointers.FirstOrDefault(p => p.destination == _data.Follow(i * 4).Location);
         return r.destination;
      }
   }
}
