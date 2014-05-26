using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PointerMapper {
      public static Brush Brush = Solarized.Brushes.Orange;
      readonly SortedList<int, int> _pointerSet = new SortedList<int, int>(); // unclaimed pointers
      readonly IDictionary<int, List<int>> _reversePointerSet = new Dictionary<int, List<int>>(); // unclaimed pointers helper
      readonly IDictionary<int, int[]> _destinations = new Dictionary<int, int[]>(); // claimed destinations (with pointers)
      readonly IDictionary<int, IDataRun> _pointedRuns = new Dictionary<int, IDataRun>();
      readonly SimpleDataRun _pointerRun;

      public IEnumerable<int> OpenDestinations { get { return _pointerSet.Values.Distinct().ToArray(); } }

      public List<int> MappedDestinations { get { lock (_destinations) return _destinations.Keys.ToList(); } }

      public PointerMapper(byte[] data) {
         _pointerRun = new SimpleDataRun(4, Brush, Utils.ByteFlyweights) { Underlined = true, Interpret = InterpretPointer, Jump = JumpPointer };

         for (int i = 3; i < data.Length; i += 4) {
            if (data[i] != 0x08) continue;
            var address = data.ReadPointer(i - 3);
            if (address % 4 != 0) continue;
            _pointerSet.Add(i - 3, address);
            if (!_reversePointerSet.ContainsKey(address)) _reversePointerSet[address] = new List<int>();
            _reversePointerSet[address].Add(i - 3);
         }
      }

      public void Claim(RunStorage storage, IDataRun run, int destination) {
         var keys = _reversePointerSet[destination].ToArray();
         foreach (var key in keys) {
            storage.AddRun(key, _pointerRun);
            _pointedRuns[key] = run;
            _pointerSet.Remove(key);
         }
         _reversePointerSet.Remove(destination);
         lock (_destinations) _destinations[destination] = keys;
      }

      public void ClaimRemainder(RunStorage storage) {
         var pointerLocations = _pointedRuns.Keys.ToList();
         pointerLocations.Sort();
         foreach (var destination in _reversePointerSet.Keys) {
            int index = pointerLocations.BinarySearch(destination);
            if (index < 0) index = ~index - 1; // get the item just before here
            if (index < 0) index = 0;

            // don't add it if it points into a used range
            var pointer = pointerLocations[index];
            if (pointer < destination && destination < pointer + _pointedRuns[pointer].GetLength(storage.Data, pointer)) continue;

            var keys = _reversePointerSet[destination].ToArray();
            foreach (var key in keys) {
               storage.AddRun(key, _pointerRun);
            }
            lock (_destinations) _destinations[destination] = keys;
         }
         _pointerSet.Clear();
         _reversePointerSet.Clear();
      }

      public void FilterPointer(Func<int, bool> func) {
         foreach (var pointer in _pointerSet.Keys.ToArray()) {
            int dest = _pointerSet[pointer];
            if (func(dest)) continue;
            _pointerSet.Remove(pointer);
            _reversePointerSet[dest].Remove(pointer);
            if (_reversePointerSet[dest].Count == 0) _reversePointerSet.Remove(dest);
         }
      }

      public int[] PointersFromDestination(int destination) { return _destinations[destination]; }

      FrameworkElement InterpretPointer(byte[] data, int index) {
         if (!_pointedRuns.ContainsKey(index) || _pointedRuns[index].Interpret == null) return null;
         return _pointedRuns[index].Interpret(data, data.ReadPointer(index));
      }

      int[] JumpPointer(byte[] data, int index) {
         return new[] { data.ReadPointer(index) };
      }
   }

   class PointerParser : IParser {
      #region Fields

      static readonly Geometry Hat = Geometry.Parse("m0,0 l0,-1 1,0 z");

      readonly IParser _base;
      readonly byte[] _data;
      readonly Queue<Grid> _spareContainers = new Queue<Grid>();
      readonly Queue<Path> _spareHats = new Queue<Path>();
      readonly RunStorage _storage;
      readonly PointerMapper _mapper;
      bool _loaded = false;

      #endregion

      #region Interface

      public int Length { get { return _data.Length; } }

      public PointerParser(IParser fallback, byte[] data, RunStorage storage, PointerMapper mapper) {
         _data = data;
         _base = fallback;
         _storage = storage;
         _mapper = mapper;
      }

      public void Load() {
         _loaded = false;
         _base.Load();
         _mapper.ClaimRemainder(_storage);
         _loaded = true;
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var elements =_base.CreateElements(commander, start, length);

         var destinations = _mapper.MappedDestinations;
         destinations.Sort();
         var startIndex = destinations.BinarySearch(start);
         if (startIndex < 0) startIndex = ~startIndex;
         for (var i = startIndex; i < destinations.Count && destinations[i] < start + length; i++) {
            var backPointer = destinations[i];
            int index = backPointer - start;
            elements[index] = WrapForList(commander, elements[index], _mapper.PointersFromDestination(backPointer));
         }

         return elements;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.GetCreator() != this) {
            _base.Recycle(commander, element);
            return;
         }

         var grid = element as Grid;
         if (grid != null) {
            Debug.Assert(grid.Children.Count == 2);
            var child = (FrameworkElement)grid.Children[0];
            var hat = (Path)grid.Children[1];
            grid.Children.Clear();
            _spareContainers.Enqueue(grid);
            _spareHats.Enqueue(hat);
            commander.RemoveJumpCommand(hat);
            Recycle(commander, child);
            return;
         }

         Debug.Fail("How did we get here? We tagged it, but we can't recycle it!");
      }

      public bool IsStartOfDataBlock(int location) { return _base.IsStartOfDataBlock(location); }
      public bool IsWithinDataBlock(int location) { return _base.IsWithinDataBlock(location); }
      public FrameworkElement GetInterpretation(int location) { return _base.GetInterpretation(location); }

      public IList<int> Find(string term) { return _base.Find(term); }

      #endregion

      #region Helpers

      static bool ContainsAny(int[] masterList, params int[] pointers) {
         return pointers.Any(pointer => {
            int index = Array.BinarySearch(masterList, pointer);
            return index >= 0 && index < masterList.Length;
         });
      }

      FrameworkElement WrapForList(ICommandFactory commander, FrameworkElement element, params int[] jumpLocations) {
         Grid grid = null;
         if (_spareContainers.Count > 0) grid = _spareContainers.Dequeue();
         else grid = new Grid();
         grid.SetCreator(this);

         Path hat = null;
         if (_spareHats.Count > 0) hat = _spareHats.Dequeue();
         else hat = new Path {
            Data = Hat,
            Fill = PointerMapper.Brush,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 10, Height = 10
         };

         grid.Children.Add(element);
         grid.Children.Add(hat);
         commander.CreateJumpCommand(hat, jumpLocations);
         return grid;
      }

      #endregion
   }

}
