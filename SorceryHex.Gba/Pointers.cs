﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   /*
    * ffva notes:
    * 1554E8 Abilities { -requiredAbp -abilityIndex }
    * 156E6C Equipment { 32 bits, each bit is an equipment type flag }
    * 15616C Inate     { .abilityIndex .abilityIndex .abilityIndex .abilityIndex }
    * 145A28 Level     { exp -hp -mp }
    * 156E04 Stats     { .strength .agility .stamina .magic }
   //*/

   public interface IPointerMapper {
      void Claim(IRunStorage runs, int location, int pointer);
   }

   public class PointerMapper : IPointerMapper {
      public static Brush Brush = GbaBrushes.Pointer;
      readonly SortedList<int, int> _pointerSet = new SortedList<int, int>(); // unclaimed pointers
      readonly IDictionary<int, List<int>> _reversePointerSet = new Dictionary<int, List<int>>(); // unclaimed pointers helper
      readonly IDictionary<int, int[]> _destinations = new Dictionary<int, int[]>(); // claimed destinations (with pointers)
      readonly IDictionary<int, IDataRun> _pointedRuns = new Dictionary<int, IDataRun>();
      readonly SimpleDataRun _pointerRun;

      public IEnumerable<int> OpenDestinations { get { return _pointerSet.Values.Distinct().ToArray(); } }

      public List<int> MappedDestinations { get { lock (_destinations) return _destinations.Keys.ToList(); } }

      public PointerMapper(byte[] data) {
         _pointerRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, Brush, true), 4) { Interpret = InterpretPointer, Jump = JumpPointer };

         for (int i = 3; i < data.Length; i += 4) {
            if (data[i] != 0x08) continue;
            var address = data.ReadPointer(i - 3);
            if (address % 4 != 0) continue;
            _pointerSet.Add(i - 3, address);
            if (!_reversePointerSet.ContainsKey(address)) _reversePointerSet[address] = new List<int>();
            _reversePointerSet[address].Add(i - 3);
         }
      }

      readonly ISet<int> _deferredDestinations = new HashSet<int>();
      readonly IDictionary<int, IDataRun> _deferredDestinationsWithRuns = new Dictionary<int, IDataRun>();
      void Defer(int destination) {
         _deferredDestinations.Add(destination); Debug.Assert(_reversePointerSet.ContainsKey(destination));
      }
      void Defer(int destination, IDataRun run) {
         _deferredDestinationsWithRuns[destination] = run; Debug.Assert(_reversePointerSet.ContainsKey(destination));
      }

      public void ClaimDeferred(ICommandFactory commandFactory, IRunStorage storage) {
         foreach (var destination in _deferredDestinations) {
            if (!_reversePointerSet.ContainsKey(destination)) {
               commandFactory.LogError("Pointer confilct at " + destination.ToHexString() +
               ": one part of the program marked it" + Environment.NewLine +
               "- as a pointer destination and another marked it as impossible for" + Environment.NewLine +
               "- a pointer destination. Check the location in the ROM. There may" + Environment.NewLine +
               "- be an error in the ROM or in a script that caused this conflict.");
               continue;
            }
            var keys = _reversePointerSet[destination].ToArray();
            foreach (var key in keys) {
               if (storage.IsFree(key)) storage.AddRun(key, _pointerRun);
               _pointerSet.Remove(key);
            }
            _reversePointerSet.Remove(destination);
         }
         _deferredDestinations.Clear();

         foreach (var destination in _deferredDestinationsWithRuns.Keys) {
            if (!_reversePointerSet.ContainsKey(destination)) {
               commandFactory.LogError("Pointer confilct at " + destination.ToHexString() +
               ": one part of the program marked it" + Environment.NewLine +
               "- as a pointer destination and another marked it as impossible for" + Environment.NewLine +
               "- a pointer destination. Check the location in the ROM. There may" + Environment.NewLine +
               "- be an error in the ROM or in a script that caused this conflict.");
               continue;
            }
            var keys = _reversePointerSet[destination].ToArray();
            foreach (var key in keys) {
               if (storage.IsFree(key)) {
                  storage.AddRun(key, _pointerRun);
                  _pointedRuns[key] = _deferredDestinationsWithRuns[destination];
               }
               _pointerSet.Remove(key);
            }
            _reversePointerSet.Remove(destination);
         }
         _deferredDestinationsWithRuns.Clear();
      }

      public void Claim(IRunStorage storage, IDataRun run, int destination) {
         // if it's already claimed, that's fine
         if (_destinations.ContainsKey(destination)) return;
         storage.AddRun(destination, run);
         Defer(destination, run);
         var keys = _reversePointerSet[destination].ToArray();
         lock (_destinations) _destinations[destination] = keys;
      }

      public void Claim(IRunStorage storage, int source, int destination) {
         // if it's already claimed, that's fine
         if (_reversePointerSet.ContainsKey(destination)) {
            if (_destinations.ContainsKey(destination)) return;
            Claim(storage, destination);
         } else {
            storage.AddRun(source, _pointerRun);

            lock (_destinations) {
               if (_destinations.ContainsKey(destination)) {
                  _destinations[destination] = _destinations[destination].Concat(new int[] { source }).ToArray();
               } else {
                  _destinations[destination] = new int[] { source };
               }
            }
         }
      }

      public void Claim(IRunStorage storage, int destination) {
         // if it's already claimed, that's fine
         if (_destinations.ContainsKey(destination)) return;
         Defer(destination);
         var keys = _reversePointerSet[destination].ToArray();
         lock (_destinations) _destinations[destination] = keys;
      }

      public void ClaimRemainder(IRunStorage storage) {
         var pointerLocations = _pointedRuns.Keys.ToList();
         pointerLocations.Sort();
         foreach (var destination in _reversePointerSet.Keys) {
            int index = pointerLocations.BinarySearch(destination);
            if (index < 0) index = ~index - 1; // get the item just before here
            if (index < 0) index = 0;

            // don't add it if it points into a used range
            if (pointerLocations.Count > 0) {
               var pointer = pointerLocations[index];
               if (pointer < destination && destination < pointer + _pointedRuns[pointer].GetLength(storage.Segment.Follow(pointer)).Length) continue;
            }

            var keys = _reversePointerSet[destination].ToArray();
            if (keys.All(storage.IsFree)) {
               foreach (var key in keys) {
                  storage.AddRun(key, _pointerRun);
               }
               lock (_destinations) _destinations[destination] = keys;
            }
         }
         _pointerSet.Clear();
         _reversePointerSet.Clear();
      }

      /// <summary>
      /// Remove all pointers with destinations that fail to meet a condition
      /// </summary>
      /// <param name="func">A function that returns true for destinations that may exist, or false for destinations that are incorrect.</param>
      public void FilterPointer(Func<int, bool> func) {
         foreach (var pointer in _pointerSet.Keys.ToArray()) {
            int dest = _pointerSet[pointer];
            if (func(dest)) continue;
            _pointerSet.Remove(pointer);
            _reversePointerSet[dest].Remove(pointer);
            if (_reversePointerSet[dest].Count == 0) _reversePointerSet.Remove(dest);
         }
      }

      static readonly int[] Empty = new int[0];
      public int[] PointersFromDestination(int destination) {
         if (!_destinations.ContainsKey(destination)) {
            if (_reversePointerSet.ContainsKey(destination)) {
               return _reversePointerSet[destination].ToArray();
            } else {
               return Empty;
            }
         }
         return _destinations[destination];
      }

      FrameworkElement InterpretPointer(ISegment segment) {
         if (!_pointedRuns.ContainsKey(segment.Location) || _pointedRuns[segment.Location].Interpret == null) return null;
         var run = _pointedRuns[segment.Location];
         return run.Interpret(run.GetLength(segment.Follow(0)));
      }

      int[] JumpPointer(ISegment segment, int index) {
         return new[] { segment.Follow(index).Location };
      }
   }

   class PointerParser : IModel {
      #region Fields

      static readonly Geometry Hat = Geometry.Parse("m0,0 l0,-1 1,0 z");

      readonly IModel _base;
      readonly byte[] _data;
      readonly Queue<Grid> _spareContainers = new Queue<Grid>();
      readonly Queue<Path> _spareHats = new Queue<Path>();
      readonly RunStorage _storage;
      readonly PointerMapper _mapper;
      bool _loaded = false;

      #endregion

      #region Parser

      public ISegment Segment { get { return _base.Segment; } }

      public PointerParser(IModel fallback, byte[] data, RunStorage storage, PointerMapper mapper) {
         _data = data;
         _base = fallback;
         _storage = storage;
         _mapper = mapper;
      }

      public void Load(ICommandFactory commander) {
         _loaded = false;
         _base.Load(commander);
         using (AutoTimer.Time("Gba.PointerParser-post_base_load")) {
            _mapper.FilterPointer(dest => !_storage.IsWithinDataBlock(dest));
            _mapper.ClaimDeferred(commander, _storage);
            _mapper.ClaimRemainder(_storage);
            _loaded = true;
         }
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var elements = _base.CreateElements(commander, start, length);
         if (!_loaded) return elements;

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

         var grid = (Grid)element;
         Debug.Assert(grid.Children.Count == 2);
         var child = (FrameworkElement)grid.Children[0];
         var hat = (Path)grid.Children[1];
         grid.Children.Clear();
         _spareContainers.Enqueue(grid);
         _spareHats.Enqueue(hat);
         commander.RemoveJumpCommand(hat);
         Recycle(commander, child);
      }

      public bool IsStartOfDataBlock(int location) { return _base.IsStartOfDataBlock(location); }
      public bool IsWithinDataBlock(int location) { return _base.IsWithinDataBlock(location); }
      public string GetLabel(int location) { return _base.GetLabel(location); }
      public int GetDataBlockStart(int location) { return _base.GetDataBlockStart(location); }
      public int GetDataBlockLength(int location) { return _base.GetDataBlockLength(location); }

      public FrameworkElement GetInterpretation(int location) { return _base.GetInterpretation(location); }

      public IEnumerable<int> Find(string term) { return _base.Find(term); }

      #endregion

      #region Editor

      public FrameworkElement CreateElementEditor(ISegment segment) { return _base.CreateElementEditor(segment); }

      public void Edit(ISegment segment, char c) { _base.Edit(segment, c); }

      public void CompleteEdit(ISegment segment) { _base.CompleteEdit(segment); }

      public event EventHandler<UpdateLocationEventArgs> MoveToNext {
         add { _base.MoveToNext += value; }
         remove { _base.MoveToNext -= value; }
      }

      #endregion

      #region Helpers

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
         Grid.SetColumnSpan(grid, Grid.GetColumnSpan(element));
         commander.CreateJumpCommand(hat, jumpLocations);
         return grid;
      }

      #endregion
   }

   class GbaSegment : ISegment {
      readonly byte[] _data;

      public bool HasLength { get { return Length != -1; } }
      public int Length { get; private set; }
      public int Location { get; private set; }
      public byte this[int index] {
         get {
            if (HasLength && index >= Length) throw new IndexOutOfRangeException();
            return _data[Location + index];
         }
      }

      public GbaSegment(byte[] data, int location) { _data = data; Location = location; Length = -1; }
      public GbaSegment(byte[] data, int location, int length) { _data = data; Location = location; Length = length; }

      public int Read(int offset, int length) { return _data.ReadData(length, Location + offset); }
      public void Write(int offset, int length, int value) {
         while (length > 0) {
            _data[Location + offset] = (byte)value;
            value >>= 8;
            offset++;
            length--;
         }
      }
      public ISegment Inner(int offset) { return new GbaSegment(_data, Location + offset); }
      public ISegment Follow(int offset) {
         if (Read(offset + 3, 1) != 0x08) return null;
         int value = Read(offset, 3);
         if (value < 0 || value > _data.Length) return null;
         return new GbaSegment(_data, value);
      }
      public ISegment Resize(int length) { return new GbaSegment(_data, Location, length); }
   }
}
