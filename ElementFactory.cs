using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface ICommandFactory {
      void CreateJumpCommand(FrameworkElement element, params int[] jumpLocation);
      void RemoveJumpCommand(FrameworkElement element);
   }

   public interface IElementFactory {
      int Length { get; }
      IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
   }

   class RangeChecker : IElementFactory {
      readonly IElementFactory _base;
      readonly Queue<FrameworkElement> _recycles = new Queue<FrameworkElement>();

      public int Length { get { return _base.Length; } }

      public RangeChecker(IElementFactory next) { _base = next; }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new List<FrameworkElement>();

         int pre = 0, post = 0;
         if (start < 0) { pre = -start; start = 0; }
         if (start + length >= Length) { post = start + length - Length; length = Length - start; }

         if (pre > 0) list.AddRange(Enumerable.Range(0, pre).Select(UseElement));
         list.AddRange(_base.CreateElements(commander, start, length));
         if (post > 0) list.AddRange(Enumerable.Range(0, post).Select(UseElement));

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag == this) _recycles.Enqueue((Rectangle)element);
         else _base.Recycle(commander, element);
      }

      FrameworkElement UseElement(int i) {
         if (_recycles.Count > 0) return _recycles.Dequeue();

         return new Rectangle { Tag = this };
      }
   }

   class DataHolder : IElementFactory {
      readonly byte[] _data;
      readonly Queue<Path> _recycles = new Queue<Path>();

      public DataHolder(byte[] data) { _data = data; }
      public int Length { get { return _data.Length; } }
      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         Debug.Assert(length < 0x20 * 0x40);
         return Enumerable.Range(start, length).Select(i => {
            var path = UsePath();
            path.Data = Utils.ByteFlyweights[_data[i]];

            bool lightweight = _data[i] == 0xFF || _data[i] == 0x00;
            path.Fill = lightweight ? Solarized.Theme.Instance.Secondary : Solarized.Theme.Instance.Primary;
            path.Opacity = lightweight ? .75 : 1;

            return path;
         });
      }
      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         Debug.Assert(element.Tag == this);
         _recycles.Enqueue((Path)element);
      }

      Path UsePath() {
         if (_recycles.Count > 0) return _recycles.Dequeue();

         return new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
            Tag = this
         };
      }
   }

   class GbaPointerFormatter : IElementFactory {
      static readonly Geometry LeftArrow  = Geometry.Parse("m0,0 l0,2 -1,-1 z");
      static readonly Geometry RightArrow = Geometry.Parse("m0,0 l0,2  1,-1 z");
      static readonly Geometry Hat = Geometry.Parse("m0,0 l0,-1 1,0 z");
      readonly IElementFactory _base;
      readonly byte[] _data;
      class BackPointer { public int Destination; public int[] Sources; }
      readonly IList<int> _pointers = new List<int>();
      readonly IList<BackPointer> _backpointers = new List<BackPointer>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly Queue<Grid> _spareContainers = new Queue<Grid>();
      readonly Queue<Path> _spareHats = new Queue<Path>();

      public int Length { get { return _data.Length; } }
      public GbaPointerFormatter(IElementFactory fallback, byte[] data) {
         _data = data;
         _base = fallback;
         LoadPointers();
      }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var pointerIndex = FindPointersInRange(start, length);

         var list = new List<FrameworkElement>();

         for (int i = 0; i < length;) {
            int loc = start+i;
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

         for (var i = FindBackPointersForStartPoint(start); i < _backpointers.Count && _backpointers[i].Destination < start + length; i++) {
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

      /// <summary>
      /// Slightly Dumb: Might need more context.
      /// But ok for an initial sweep.
      /// </summary>
      void LoadPointers() {
         _pointers.Clear();
         var backPointers = new Dictionary<int, IList<int>>();
         var end = Length - 3;
         for (int i = 0; i < end; i++) {
            if (_data[i + 3] != 0x08) continue;
            _pointers.Add(i);

            int value = _data.ReadPointer(i);
            if (!backPointers.ContainsKey(value)) backPointers[value] = new List<int>();
            backPointers[value].Add(i);

            i += 3;
         }

         foreach (var back in backPointers.Keys.OrderBy(i => i)) {
            _backpointers.Add(new BackPointer { Destination = back, Sources = backPointers[back].ToArray() });
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

      int FindBackPointersForStartPoint(int start) {
         int pointerStartIndex = 0, pointerEndIndex = _backpointers.Count - 1;
         while (pointerStartIndex < pointerEndIndex) {
            int guessIndex = (pointerEndIndex + pointerStartIndex) / 2;
            var destination = _backpointers[guessIndex].Destination;
            if (destination == start) return guessIndex;
            if (destination < start) pointerStartIndex = guessIndex + 1;
            else pointerEndIndex = guessIndex - 1;
         }
         while (pointerStartIndex < _backpointers.Count && _backpointers[pointerStartIndex].Destination < start) pointerStartIndex++;
         return pointerStartIndex;
      }

      IEnumerable<FrameworkElement> CreatePointerElements(ICommandFactory commander, int start, int length) {
         int pointerStart = start + length - 4;
         int value = _data.ReadPointer(pointerStart);
         var str = value.ToHexString();
         while (str.Length < 6) str = "0" + str;

         var leftEdge = UseTemplate(LeftArrow);
         commander.CreateJumpCommand(leftEdge, value);

         var data1 = UseTemplate(str.Substring(0, 3).ToGeometry());
         commander.CreateJumpCommand(data1, value);

         var data2 = UseTemplate(str.Substring(3).ToGeometry());
         commander.CreateJumpCommand(data2, value);

         var rightEdge = UseTemplate(RightArrow);
         commander.CreateJumpCommand(rightEdge, value);

         var set = new[] { leftEdge, data1, data2, rightEdge };
         foreach (var element in set.Take(4 - length)) Recycle(commander, element);
         return set.Skip(4 - length);
      }

      FrameworkElement UseTemplate(Geometry data) {
         if (_recycles.Count > 0) {
            var element = _recycles.Dequeue();
            ((Path)element.Child).Data = data;
            return element;
         }

         return new Border {
            Child = new Path {
               HorizontalAlignment = HorizontalAlignment.Center,
               VerticalAlignment = VerticalAlignment.Center,
               Stretch = Stretch.Uniform,
               Fill = Solarized.Brushes.Red,
               Data = data,
               Margin = new Thickness(1)
            },
            Background = Brushes.Transparent,
            Tag = this
         };
      }

      FrameworkElement WrapForList(ICommandFactory commander, FrameworkElement element, params int[] jumpLocations) {
         Grid grid = null;
         if (_spareContainers.Count > 0) grid = _spareContainers.Dequeue();
         else grid = new Grid { Tag = this };

         Path hat = null;
         if (_spareHats.Count > 0) hat = _spareHats.Dequeue();
         else hat = new Path {
            Data = Hat,
            Fill = Solarized.Brushes.Red,
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
   }

   class GbaImagesFormatter : IElementFactory {
      readonly IElementFactory _base;
      readonly byte[] _data;
      readonly IList<int> _imageLocations;
      readonly IList<int> _imageLengths;

      public int Length { get { return _data.Length; } }

      public GbaImagesFormatter(IElementFactory fallback, byte[] data) {
         _base = fallback;
         _data = data;
         _imageLocations = GbaImages.FindLZImages(_data);
         _imageLengths = _imageLocations.Select(loc => GbaImages.CompressedLZSize(_data, loc)).ToList();
      }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         throw new NotImplementedException();
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         throw new NotImplementedException();
      }
   }
}
