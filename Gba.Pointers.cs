using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PointerFormatter : IParser {
      #region Fields

      static readonly Geometry LeftArrow = Geometry.Parse("m0,0 l0,2 -1,-1 z");
      static readonly Geometry RightArrow = Geometry.Parse("m0,0 l0,2  1,-1 z");
      static readonly Geometry Hat = Geometry.Parse("m0,0 l0,-1 1,0 z");
      static readonly Brush Brush = Solarized.Brushes.Orange;
      class BackPointer { public int Destination; public int[] Sources; }

      readonly IParser _base;
      readonly byte[] _data;
      readonly IList<Border> _hasInterpretation = new List<Border>();
      readonly IList<int> _pointers = new List<int>();
      readonly IList<BackPointer> _backpointers = new List<BackPointer>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly Queue<Grid> _spareContainers = new Queue<Grid>();
      readonly Queue<Path> _spareHats = new Queue<Path>();
      bool _loaded = false;

      #endregion

      #region Interface

      public int Length { get { return _data.Length; } }

      public PointerFormatter(IParser fallback, byte[] data) {
         _data = data;
         _base = fallback;
      }

      public void Load() { _loaded = false; _base.Load(); LoadPointers(); _loaded = true; }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         if (!_loaded) return _base.CreateElements(commander, start, length);
         var pointerIndex = FindPointersInRange(start, length);

         var list = new List<FrameworkElement>();

         for (int i = 0; i < length; ) {
            int loc = start + i;
            if (pointerIndex >= _pointers.Count) {
               list.AddRange(_base.CreateElements(commander, loc, length - i));
               i = length;
            } else if (_pointers[pointerIndex] <= loc) {
               list.AddRange(CreatePointerElements(commander, loc, _pointers[pointerIndex] + 4 - loc));
               i += _pointers[pointerIndex] + 4 - loc;
               pointerIndex++;
            } else {
               list.AddRange(_base.CreateElements(commander, loc, Math.Min(_pointers[pointerIndex] - loc, length - i)));
               i += _pointers[pointerIndex] - loc;
            }
         }

         var startIndex = Utils.SearchForStartPoint(start, _backpointers, bp => bp.Destination, Utils.FindOptions.StartOrAfter);
         for (var i = startIndex; i < _backpointers.Count && _backpointers[i].Destination < start + length; i++) {
            var backPointer = _backpointers[i];
            int index = backPointer.Destination - start;
            list[index] = WrapForList(commander, list[index], backPointer.Sources);
         }

         foreach (var element in list.Skip(length)) Recycle(commander, element);
         return list.Take(length);
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag != this) {
            _base.Recycle(commander, element);
            return;
         }

         var border = element as Border;
         if (border != null) {
            _recycles.Enqueue(border);
            commander.RemoveJumpCommand(border);
            if (_hasInterpretation.Contains(border)) {
               commander.UnlinkFromInterpretation(border);
               _hasInterpretation.Remove(border);
            }
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

      /// <summary>
      /// Slightly Dumb: Might need more context.
      /// But ok for an initial sweep.
      /// </summary>
      void LoadPointers() {
         _pointers.Clear();
         IDictionary<int, IList<int>> backPointers = new Dictionary<int, IList<int>>();
         var end = Length - 3;
         for (int i = 0; i < end; i++) {
            if (_data[i + 3] != 0x08) continue;
            if (_base.IsWithinDataBlock(i)) continue;
            if (_base.IsWithinDataBlock(i + 3)) continue;
            int value = _data.ReadPointer(i);
            if (_base.IsWithinDataBlock(value)) continue;

            _pointers.Add(i);
            if (!backPointers.ContainsKey(value)) backPointers[value] = new List<int>();
            backPointers[value].Add(i);
         }

         backPointers = GainConfidence(backPointers);

         _backpointers.Clear();
         foreach (var back in backPointers.Keys.OrderBy(i => i)) {
            _backpointers.Add(new BackPointer { Destination = back, Sources = backPointers[back].ToArray() });
         }
      }

      IDictionary<int, IList<int>> GainConfidence(IDictionary<int, IList<int>> backPointers) {
         var tempArray = _pointers.ToArray();
         var tempDest = backPointers.Keys.OrderBy(i => i).ToArray();

         // build confidence in the guesses
         var confidence = new List<int>();
         for (int i = 0; i < tempArray.Length; i++) {
            int pointerConfidence = 0;

            // gain confidence for multiple pointers leading to the same location
            var dest = _data.ReadPointer(_pointers[i]);
            pointerConfidence += backPointers[dest].Count;

            // gain confidence for pointers leading to known data
            if (_base.IsStartOfDataBlock(dest)) pointerConfidence += int.MaxValue / 2;

            Debug.Assert(pointerConfidence > 0);

            // lose confidence in pointers that point to within another pointer
            if (ContainsAny(tempArray, dest - 1, dest - 2, dest - 3)) pointerConfidence--;

            // lose confidence in pointers that don't point to a multiple of 4 or start on a multiple of 4
            if (_pointers[i] % 4 != 0) pointerConfidence--;
            if (dest % 4 != 0) pointerConfidence--;

            // lose confidence in pointers that point to just 1 byte
            if (ContainsAny(tempDest, dest + 1)) pointerConfidence--;

            confidence.Add(pointerConfidence);
         }

         RemoveSharedSpacePointerConfidence(confidence);

         // remove pointers with no confidence
         var pointers2 = new List<int>();
         pointers2.AddRange(_pointers);
         _pointers.Clear();
         var backPointers2 = new Dictionary<int, IList<int>>();
         for (int i = 0; i < pointers2.Count; i++) {
            if (confidence[i] <= 0) continue;
            var value = _data.ReadPointer(pointers2[i]);
            if (!backPointers2.ContainsKey(value)) backPointers2[value] = new List<int>();
            backPointers2[value].Add(pointers2[i]);
            _pointers.Add(pointers2[i]);
         }

         return backPointers2;
      }

      static bool ContainsAny(int[] masterList, params int[] pointers) {
         return pointers.Any(pointer => {
            int index = Array.BinarySearch(masterList, pointer);
            return index >= 0 && index < masterList.Length;
         });
      }

      void RemoveSharedSpacePointerConfidence(IList<int> confidence) {
         for (int i = 0; i < _pointers.Count - 1; i++) {
            if (_pointers[i] + 4 <= _pointers[i + 1]) continue;
            if (confidence[i] < confidence[i + 1]) {
               confidence[i] = 0;
            } else if (confidence[i] > confidence[i + 1]) {
               confidence[i + 1] = 0;
            }
            if (i >= _pointers.Count - 2 || _pointers[i] + 4 <= _pointers[i + 2]) continue;
            if (confidence[i] < confidence[i + 2]) {
               confidence[i] = 0;
            } else if (confidence[i] > confidence[i + 2]) {
               confidence[i + 2] = 0;
            }
         }
      }

      int FindPointersInRange(int start, int length) {
         // binary search for the start point in the list
         int pointerStartIndex = 0, pointerEndIndex = _pointers.Count - 1;
         while (pointerStartIndex < pointerEndIndex) {
            int guessIndex = (pointerEndIndex + pointerStartIndex) / 2;
            if (_pointers[guessIndex] < start - 3) pointerStartIndex = guessIndex + 1;
            else if (_pointers[guessIndex] >= start - 3 && _pointers[guessIndex] <= start) return guessIndex;
            else pointerEndIndex = guessIndex - 1;
         }
         while (pointerStartIndex < _pointers.Count && _pointers[pointerStartIndex] < start - 3) pointerStartIndex++;
         return pointerStartIndex;
      }

      IEnumerable<FrameworkElement> CreatePointerElements(ICommandFactory commander, int start, int length) {
         int pointerStart = start + length - 4;
         int value = _data.ReadPointer(pointerStart);
         var interpretation = GetInterpretation(value);

         var leftEdge = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 0]], 2, 0, commander, value, interpretation);
         var data1 = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 1]], 0, 0, commander, value, interpretation);
         var data2 = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 2]], 0, 0, commander, value, interpretation);
         var rightEdge = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 3]], 0, 2, commander, value, interpretation);

         var set = new[] { leftEdge, data1, data2, rightEdge };
         foreach (var element in set.Take(4 - length)) Recycle(commander, element);
         return set.Skip(4 - length);
      }

      FrameworkElement UseTemplate(Geometry data, double leftBorder, double rightBorder, ICommandFactory commander, int location, FrameworkElement interpretation) {
         Border element;
         if (_recycles.Count > 0) {
            element = _recycles.Dequeue();
         } else {
            element = new Border {
               Child = new Path {
                  HorizontalAlignment = HorizontalAlignment.Center,
                  VerticalAlignment = VerticalAlignment.Center,
                  Fill = Brush,
                  Margin = new Thickness(4, 3, 4, 1),
               },
               BorderThickness = new Thickness(0, 0, 0, 1),
               BorderBrush = Brush,
               Background = Brushes.Transparent,
               Tag = this
            };
         }

         ((Path)element.Child).Data = data;
         element.Margin = new Thickness(leftBorder, 0, rightBorder, 1);
         commander.CreateJumpCommand(element, location);
         if (interpretation != null) {
            commander.LinkToInterpretation(element, interpretation);
            _hasInterpretation.Add(element);
         }
         return element;
      }

      FrameworkElement WrapForList(ICommandFactory commander, FrameworkElement element, params int[] jumpLocations) {
         Grid grid = null;
         if (_spareContainers.Count > 0) grid = _spareContainers.Dequeue();
         else grid = new Grid { Tag = this };

         Path hat = null;
         if (_spareHats.Count > 0) hat = _spareHats.Dequeue();
         else hat = new Path {
            Data = Hat,
            Fill = Brush,
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
