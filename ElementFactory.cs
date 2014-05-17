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
      void LinkToInterpretation(FrameworkElement element, FrameworkElement visual);
      void UnlinkFromInterpretation(FrameworkElement element);
   }

   // TODO allow an element factory to warn its owner about data block boundaries
   public interface IElementFactory {
      int Length { get; }
      IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsWithinDataBlock(int location);
      FrameworkElement GetInterpretation(int location);
   }

   class RangeChecker : IElementFactory {
      readonly IElementFactory _base;
      readonly Queue<FrameworkElement> _recycles = new Queue<FrameworkElement>();

      public int Length { get { return _base.Length; } }

      public RangeChecker(IElementFactory next) { _base = next; }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new List<FrameworkElement>();

         int pre = 0, post = 0;
         if (start < 0) { pre = -start; start = 0; length -= pre; }
         if (length < 0) { pre += length; length = 0; }
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

      public bool IsWithinDataBlock(int location) { return _base.IsWithinDataBlock(location); }

      public FrameworkElement GetInterpretation(int location) { return null; }

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

      public bool IsWithinDataBlock(int location) { return false; }

      public FrameworkElement GetInterpretation(int location) { return null; }

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
      #region Fields

      static readonly Geometry LeftArrow  = Geometry.Parse("m0,0 l0,2 -1,-1 z");
      static readonly Geometry RightArrow = Geometry.Parse("m0,0 l0,2  1,-1 z");
      static readonly Geometry Hat = Geometry.Parse("m0,0 l0,-1 1,0 z");
      static readonly Brush Brush = Solarized.Brushes.Orange;
      class BackPointer { public int Destination; public int[] Sources; }

      readonly IElementFactory _base;
      readonly byte[] _data;
      readonly IList<Border> _hasInterpretation = new List<Border>();
      readonly IList<int> _pointers = new List<int>();
      readonly IList<BackPointer> _backpointers = new List<BackPointer>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly Queue<Grid> _spareContainers = new Queue<Grid>();
      readonly Queue<Path> _spareHats = new Queue<Path>();

      #endregion

      #region Interface

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

      public bool IsWithinDataBlock(int location) { return _base.IsWithinDataBlock(location); }

      public FrameworkElement GetInterpretation(int location) { return _base.GetInterpretation(location); }

      #endregion

      #region Helpers

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
            if (_base.IsWithinDataBlock(i)) continue;
            if (_base.IsWithinDataBlock(i + 3)) continue;
            int value = _data.ReadPointer(i);
            if (_base.IsWithinDataBlock(value)) continue;

            _pointers.Add(i);
            if (!backPointers.ContainsKey(value)) backPointers[value] = new List<int>();
            backPointers[value].Add(i);

            i += 3;
         }

         _backpointers.Clear();
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

      IEnumerable<FrameworkElement> CreatePointerElements(ICommandFactory commander, int start, int length) {
         int pointerStart = start + length - 4;
         int value = _data.ReadPointer(pointerStart);
         var interpretation = GetInterpretation(value);

         var leftEdge  = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 0]], 2, 0, commander, value, interpretation);
         var data1     = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 1]], 0, 0, commander, value, interpretation);
         var data2     = UseTemplate(Utils.ByteFlyweights[_data[pointerStart + 2]], 0, 0, commander, value, interpretation);
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

   class GbaHeaderFormatter : IElementFactory {
      static readonly Brush Brush = Solarized.Brushes.Violet;
      static readonly Func<byte, Geometry> ToAscii = b => new string((char)b, 1).ToGeometry();
      class Entry {
         public readonly int Length;
         public readonly string Name;
         public Func<byte, Geometry> Parse;
         public Entry(int len, string name, Func<byte,Geometry> parse) {
            Length = len;
            Name = name;
            Parse = parse;
         }
         public Entry(int len, string name) : this(len, name, b => Utils.ByteFlyweights[b]) { }
      }

      readonly IElementFactory _base;
      readonly byte[] _data;
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly Entry[] _format;
      readonly int _headerLength;

      public int Length { get { return _data.Length; } }

      public GbaHeaderFormatter(IElementFactory fallback, byte[] data) {
         _base = fallback;
         _data = data;
         _format = new[] {
            new Entry(4, "Entry Point"),
            new Entry(156, "Compressed Nintendo Logo"),
            new Entry(12, "Game Title", ToAscii),
            new Entry(4, "Game Code", ToAscii),
            new Entry(2, "Maker Code", ToAscii),
            new Entry(1, "Fixed Value"),
            new Entry(1, "Main Unit Code"),
            new Entry(1, "Device Type"),
            new Entry(7, "Reserved Area"),
            new Entry(1, "Software Version"),
            new Entry(1, "Complement Check"),
            new Entry(2, "Reserved Area")
         };
         _headerLength = _format.Sum(f => f.Length);
      }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         if (start >= _headerLength) return _base.CreateElements(commander, start, length);
         var list = new List<FrameworkElement>();
         int currentFormat = 0;
         int currentOffset = 0;
         int remainder = length;

         while (currentFormat < _format.Length) {
            if (start > currentOffset + _format[currentFormat].Length) {
               currentOffset += _format[currentFormat].Length;
               currentFormat++;
               continue;
            }

            var format = _format[currentFormat];
            for (int i = 0; i < format.Length; i++) {
               if (start > currentOffset + i) continue;
               int left = 0, right = 0;
               if (i == 0) left = 2;
               if (i == format.Length - 1) right = 2;
               var element = UseTemplate(format.Parse(_data[currentOffset + i]), left, right, format.Name);
               list.Add(element);
               remainder--;
               if (remainder == 0) return list;
            }
            currentOffset += _format[currentFormat].Length;
            currentFormat++;
         }

         list.AddRange(_base.CreateElements(commander, _headerLength, remainder));
         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag == this) _recycles.Enqueue((Border)element);
         else _base.Recycle(commander, element);
      }

      public bool IsWithinDataBlock(int location) {
         if (location < 0xC0) return true;
         return _base.IsWithinDataBlock(location);
      }

      public FrameworkElement GetInterpretation(int location) { return _base.GetInterpretation(location); }

      FrameworkElement UseTemplate(Geometry data, double leftBorder, double rightBorder, string tip) {
         if (_recycles.Count > 0) {
            var element = _recycles.Dequeue();
            ((Path)element.Child).Data = data;
            element.Margin = new Thickness(leftBorder, 0, rightBorder, 1);
            element.ToolTip = tip;
            return element;
         }

         return new Border {
            Child = new Path {
               HorizontalAlignment = HorizontalAlignment.Center,
               VerticalAlignment = VerticalAlignment.Center,
               Fill = Brush,
               Data = data,
               ClipToBounds = false,
               Margin = new Thickness(2, 3, 2, 1),
            },
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Brush,
            Background = Brushes.Transparent,
            ToolTip = tip,
            Margin = new Thickness(leftBorder, 0, rightBorder, 1),
            Tag = this
         };
      }
   }

   class GbaLzFormatter<T> : IElementFactory where T : FrameworkElement {
      readonly IElementFactory _base;
      readonly byte[] _data;
      readonly IList<int> _imageLocations = new List<int>();
      readonly IList<int> _imageLengths = new List<int>();
      readonly Queue<Path> _recycles = new Queue<Path>();
      readonly IDictionary<int, T> _interpretations = new Dictionary<int, T>();

      public int Length { get { return _data.Length; } }
      public Func<byte[], T> Interpret { get; set; }

      public GbaLzFormatter(IElementFactory fallback, byte[] data, IList<int> suspectLocations) {
         _base = fallback;
         _data = data;
         foreach (var loc in suspectLocations) {
            int uncompressed, compressed;
            GbaImages.CalculateLZSizes(_data, loc, out uncompressed, out compressed);
            if (uncompressed == -1 || compressed == -1) continue;
            _imageLocations.Add(loc);
            _imageLengths.Add(compressed);
         }
      }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var startIndex = Utils.SearchForStartPoint(start, _imageLocations, i => i, Utils.FindOptions.StartOrBefore);
         var list = new List<FrameworkElement>();

         for (int i = 0; i < length; ) {
            int loc = start + i;
            int dataIndex = _imageLocations[startIndex];
            if (startIndex >= _imageLocations.Count) {
               list.AddRange(_base.CreateElements(commander, loc, length - i));
               i = length;
            } else if (dataIndex > loc) {
               var sectionLength = Math.Min(length - i, dataIndex - loc);
               list.AddRange(_base.CreateElements(commander, loc, sectionLength));
               i += sectionLength;
            } else if (dataIndex + _imageLengths[startIndex] < loc) {
               startIndex++;
            } else {
               int imageEnd = dataIndex + _imageLengths[startIndex];
               imageEnd = Math.Min(imageEnd, start + length);
               int lengthInView = imageEnd - loc;
               InterpretData(dataIndex);
               for (int j = 0; j < lengthInView; j++) {
                  var element = UsePath(loc + j);
                  commander.LinkToInterpretation(element, _interpretations[dataIndex]);
                  list.Add(element);
               }
               startIndex++;
               i += lengthInView;
            }
         }

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag != this) { _base.Recycle(commander, element); return; }
         _recycles.Enqueue((Path)element);
         commander.UnlinkFromInterpretation(element);
      }

      public bool IsWithinDataBlock(int location) {
         var startIndex = Utils.SearchForStartPoint(location, _imageLocations, i => i, Utils.FindOptions.StartOrBefore);
         bool inMyDataBlock =
            startIndex < _imageLocations.Count &&
            location > _imageLocations[startIndex] &&
            location < _imageLocations[startIndex] + _imageLengths[startIndex];
         return inMyDataBlock || _base.IsWithinDataBlock(location);
      }

      public FrameworkElement GetInterpretation(int location) {
         if (!_imageLocations.Contains(location)) return _base.GetInterpretation(location);
         InterpretData(location);
         return _interpretations[location];
      }

      void InterpretData(int dataIndex) {
         if (!_interpretations.ContainsKey(dataIndex)) {
            var dataBytes = GbaImages.UncompressLZ(_data, dataIndex);
            var interpretation = Interpret(dataBytes);
            _interpretations[dataIndex] = interpretation;
         }
      }

      Path UsePath(int source) {
         var geometry = Utils.ByteFlyweights[_data[source]];
         if (_recycles.Count > 0) {
            var element = _recycles.Dequeue();
            element.Data = geometry;
            return element;
         }

         return new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
            Fill = Solarized.Brushes.Cyan,
            Data = geometry,
            Tag = this
         };
      }
   }

   class GbaLzFormatterFactory {
      public static IElementFactory Images(IElementFactory fallback, byte[] data) {
         return new GbaLzFormatter<Image>(fallback, data, GbaImages.FindLZImages(data)) {
            Interpret = dataBytes => {
               int width, height; GbaImages.GuessWidthHeight(dataBytes.Length, out width, out height);
               var source = GbaImages.Expand16bitImage(dataBytes, GbaImages.DefaultPalette, width, height);
               return new Image { Source = source, Width = width, Height = height };
            }
         };
      }

      public static IElementFactory Palette(IElementFactory fallback, byte[] data) {
         return new GbaLzFormatter<Grid>(fallback, data, GbaImages.FindLZPalettes(data)) {
            Interpret = dataBytes => {
               var grid = new Grid { Width = 40, Height = 40, Background = Brushes.Transparent };
               for (int i = 0; i < 4; i++) {
                  grid.RowDefinitions.Add(new RowDefinition());
                  grid.ColumnDefinitions.Add(new ColumnDefinition());
               }
               var palette = new GbaImages.Palette(dataBytes);
               for (int i = 0; i < 16; i++) {
                  var rectangle = new Rectangle {
                     Fill = new SolidColorBrush(palette.Colors[i]),
                     Margin = new Thickness(1)
                  };
                  Grid.SetRow(rectangle, i / 4);
                  Grid.SetColumn(rectangle, i % 4);
                  grid.Children.Add(rectangle);
               }
               return grid;
            }
         };
      }
   }
}
