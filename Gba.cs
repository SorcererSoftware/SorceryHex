using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {

   class Pointer {
      readonly SortedList<int, int> _pointerSet = new SortedList<int, int>(); // unclaimed pointers
      readonly IDictionary<int, List<int>> _reversePointerSet = new Dictionary<int, List<int>>(); // unclaimed pointers helper
      readonly IDictionary<int, int[]> _destinations = new Dictionary<int, int[]>(); // claimed destinations (with pointers)
      readonly IDictionary<int, IDataRun> _pointedRuns = new Dictionary<int, IDataRun>();
      readonly SimpleDataRun _pointerRun;

      public IEnumerable<int> OpenDestinations { get { return _pointerSet.Values.Distinct().ToArray(); } }

      public Pointer(byte[] data) {
         _pointerRun = new SimpleDataRun(4, Solarized.Brushes.Orange, Utils.ByteFlyweights) { Underlined = true, Interpret = InterpretPointer, Jump = JumpPointer };

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
         _destinations[destination] = keys;
      }

      FrameworkElement InterpretPointer(byte[] data, int index) {
         if (_pointedRuns[index].Interpret == null) return null;
         return _pointedRuns[index].Interpret(data, data.ReadPointer(index));
      }

      int[] JumpPointer(byte[] data, int index) {
         return new[] { data.ReadPointer(index) };
      }
   }

   class Header : IRunParser {
      static SimpleDataRun HeaderRun(int len, string text, Geometry[] converter) { return new SimpleDataRun(len, Solarized.Brushes.Violet, Utils.ByteFlyweights) { HoverText = text, Underlined = true }; }
      static SimpleDataRun HeaderRun(int len, string text) { return HeaderRun(len, text, Utils.ByteFlyweights); }

      static readonly SimpleDataRun[] _headerRuns = new[] {
         HeaderRun(4, "HeaderRun Point"),
         HeaderRun(156, "Compressed Nintendo Logo"),
         HeaderRun(12, "Game Title", Utils.AsciiFlyweights),
         HeaderRun(4, "Game Code", Utils.AsciiFlyweights),
         HeaderRun(2, "Maker Code", Utils.AsciiFlyweights),
         HeaderRun(1, "Fixed Value"),
         HeaderRun(1, "Main Unit Code"),
         HeaderRun(1, "Device Type"),
         HeaderRun(7, "Reserved Area"),
         HeaderRun(1, "Software Version"),
         HeaderRun(1, "Complement Check"),
         HeaderRun(2, "Reserved Area")
      };

      public Header() { }

      public void Load(RunStorage runs) {
         int offset = 0;
         foreach (var run in _headerRuns) {
            runs.AddRun(offset, run);
            offset += run.GetLength(runs.Data, offset);
         }
      }

      public IEnumerable<int> Find(string term) { return null; }
   }

   class Lz : IRunParser {
      static SimpleDataRun LzRun(int len, InterpretationRule interpret) { return new SimpleDataRun(len, Solarized.Brushes.Cyan, Utils.ByteFlyweights) { Interpret = interpret }; }

      readonly Pointer _pointers;
      RunStorage _runs;

      public Lz(Pointer pointers) { _pointers = pointers; }

      public void Load(RunStorage runs) {
         _runs = runs;
         var initialContitions = new Func<int, bool>[]{
            loc => _runs.Data[loc + 0] == 0x10 && _runs.Data[loc + 1] == 0x20 &&
                   _runs.Data[loc + 2] == 0x00 && _runs.Data[loc + 3] == 0x00,
            loc => _runs.Data[loc + 0] == 0x10 && _runs.Data[loc + 1] % 20 == 0
         };
         var interpretations = new InterpretationRule[] { InterpretPalette, InterpretImage };

         for (int i = 0; i < initialContitions.Length; i++) {
            int count = 0;
            foreach (var loc in _pointers.OpenDestinations) {
               if (!initialContitions[i](loc)) continue;
               int uncompressed, compressed;
               GbaImages.CalculateLZSizes(_runs.Data, loc, out uncompressed, out compressed);
               if (uncompressed == -1 || compressed == -1) continue;
               var run = LzRun(compressed, interpretations[i]);
               _pointers.Claim(_runs, run, loc);
               _runs.AddRun(loc, run);
               count++;
            }
         }
      }

      public IEnumerable<int> Find(string term) { return null; }

      static FrameworkElement InterpretImage(byte[] data, int location)  {
         var dataBytes = GbaImages.UncompressLZ(data, location);
         int width, height; GbaImages.GuessWidthHeight(dataBytes.Length, out width, out height);
         var source = GbaImages.Expand16bitImage(dataBytes, GbaImages.DefaultPalette, width, height);
         return new Image { Source = source, Width = width, Height = height };
      }

      static FrameworkElement InterpretPalette(byte[] data, int location) {
         var dataBytes = GbaImages.UncompressLZ(data, location);
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
   }

   //class Header : IPartialParser {
   //   static readonly Brush Brush = Solarized.Brushes.Violet;
   //   static readonly Func<byte, Geometry> ToAscii = b => new string((char)b, 1).ToGeometry();
   //   class Entry {
   //      public readonly int Length;
   //      public readonly string Name;
   //      public Func<byte, Geometry> Parse;
   //      public Entry(int len, string name, Func<byte, Geometry> parse) {
   //         Length = len;
   //         Name = name;
   //         Parse = parse;
   //      }
   //      public Entry(int len, string name) : this(len, name, b => Utils.ByteFlyweights[b]) { }
   //   }

   //   readonly byte[] _data;
   //   readonly Queue<Border> _recycles = new Queue<Border>();
   //   readonly Entry[] _format;
   //   readonly int _headerLength;

   //   public Header(byte[] data) {
   //      _data = data;
   //      _format = new[] {
   //         new Entry(4, "Entry Point"),
   //         new Entry(156, "Compressed Nintendo Logo"),
   //         new Entry(12, "Game Title", ToAscii),
   //         new Entry(4, "Game Code", ToAscii),
   //         new Entry(2, "Maker Code", ToAscii),
   //         new Entry(1, "Fixed Value"),
   //         new Entry(1, "Main Unit Code"),
   //         new Entry(1, "Device Type"),
   //         new Entry(7, "Reserved Area"),
   //         new Entry(1, "Software Version"),
   //         new Entry(1, "Complement Check"),
   //         new Entry(2, "Reserved Area")
   //      };
   //      _headerLength = _format.Sum(f => f.Length);
   //   }

   //   public void Load() { }

   //   public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
   //      if (start >= _headerLength) return new FrameworkElement[length];
   //      var list = new List<FrameworkElement>();
   //      int currentFormat = 0;
   //      int currentOffset = 0;
   //      int remainder = length;

   //      while (currentFormat < _format.Length) {
   //         if (start > currentOffset + _format[currentFormat].Length) {
   //            currentOffset += _format[currentFormat].Length;
   //            currentFormat++;
   //            continue;
   //         }

   //         var format = _format[currentFormat];
   //         for (int i = 0; i < format.Length; i++) {
   //            if (start > currentOffset + i) continue;
   //            int left = 0, right = 0;
   //            if (i == 0) left = 2;
   //            if (i == format.Length - 1) right = 2;
   //            var element = UseTemplate(format.Parse(_data[currentOffset + i]), left, right, format.Name);
   //            list.Add(element);
   //            remainder--;
   //            if (remainder == 0) return list;
   //         }
   //         currentOffset += _format[currentFormat].Length;
   //         currentFormat++;
   //      }

   //      list.AddRange(new FrameworkElement[remainder]);
   //      return list;
   //   }

   //   public void Recycle(ICommandFactory commander, FrameworkElement element) { _recycles.Enqueue((Border)element); }
   //   public bool IsStartOfDataBlock(int location) { return location == 0; }
   //   public bool IsWithinDataBlock(int location) { return location < 0xC0; }
   //   public FrameworkElement GetInterpretation(int location) { return null; }
   //   public IList<int> Find(string term) { return null; }

   //   FrameworkElement UseTemplate(Geometry data, double leftBorder, double rightBorder, string tip) {
   //      if (_recycles.Count > 0) {
   //         var element = _recycles.Dequeue();
   //         ((Path)element.Child).Data = data;
   //         element.Margin = new Thickness(leftBorder, 0, rightBorder, 1);
   //         element.ToolTip = tip;
   //         return element;
   //      }

   //      var border = new Border {
   //         Child = new Path {
   //            HorizontalAlignment = HorizontalAlignment.Center,
   //            VerticalAlignment = VerticalAlignment.Center,
   //            Fill = Brush,
   //            Data = data,
   //            ClipToBounds = false,
   //            Margin = new Thickness(2, 3, 2, 1),
   //         },
   //         BorderThickness = new Thickness(0, 0, 0, 1),
   //         BorderBrush = Brush,
   //         Background = Brushes.Transparent,
   //         ToolTip = tip,
   //         Margin = new Thickness(leftBorder, 0, rightBorder, 1)
   //      };

   //      border.SetCreator(this);
   //      return border;
   //   }
   //}

   //class Lz<T> : IPartialParser where T : FrameworkElement {
   //   readonly byte[] _data;
   //   readonly IList<int> _imageLocations = new List<int>();
   //   readonly IList<int> _imageLengths = new List<int>();
   //   readonly Queue<Path> _recycles = new Queue<Path>();
   //   readonly IDictionary<int, T> _interpretations = new Dictionary<int, T>();
   //   readonly IList<int> _suspectLocations;

   //   public Func<byte[], T> Interpret { get; set; }

   //   public Lz(byte[] data, IList<int> suspectLocations) {
   //      _data = data;
   //      _suspectLocations = suspectLocations;
   //   }

   //   public void Load() {
   //      foreach (var loc in _suspectLocations) {
   //         int uncompressed, compressed;
   //         GbaImages.CalculateLZSizes(_data, loc, out uncompressed, out compressed);
   //         if (uncompressed == -1 || compressed == -1) continue;
   //         _imageLocations.Add(loc);
   //         _imageLengths.Add(compressed);
   //      }
   //   }

   //   public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
   //      var startIndex = Utils.SearchForStartPoint(start, _imageLocations, i => i, Utils.FindOptions.StartOrBefore);
   //      if (startIndex == _imageLocations.Count) return new FrameworkElement[length];
   //      var list = new List<FrameworkElement>();

   //      for (int i = 0; i < length; ) {
   //         int loc = start + i;
   //         if (startIndex >= _imageLocations.Count) {
   //            list.AddRange(new FrameworkElement[length - i]);
   //            i = length;
   //            continue;
   //         }

   //         int dataIndex = _imageLocations[startIndex];
   //         if (dataIndex > loc) {
   //            var sectionLength = Math.Min(length - i, dataIndex - loc);
   //            list.AddRange(new FrameworkElement[sectionLength]);
   //            i += sectionLength;
   //         } else if (dataIndex + _imageLengths[startIndex] < loc) {
   //            startIndex++;
   //         } else {
   //            int imageEnd = dataIndex + _imageLengths[startIndex];
   //            imageEnd = Math.Min(imageEnd, start + length);
   //            int lengthInView = imageEnd - loc;
   //            InterpretData(dataIndex);
   //            for (int j = 0; j < lengthInView; j++) {
   //               var element = UsePath(loc + j);
   //               commander.LinkToInterpretation(element, _interpretations[dataIndex]);
   //               list.Add(element);
   //            }
   //            startIndex++;
   //            i += lengthInView;
   //         }
   //      }

   //      return list;
   //   }

   //   public void Recycle(ICommandFactory commander, FrameworkElement element) {
   //      _recycles.Enqueue((Path)element);
   //      commander.UnlinkFromInterpretation(element);
   //   }

   //   public bool IsStartOfDataBlock(int location) {
   //      int startIndex = Utils.SearchForStartPoint(location, _imageLocations, i => i, Utils.FindOptions.StartOrBefore);
   //      return _imageLocations[startIndex] == location;
   //   }
   //   public bool IsWithinDataBlock(int location) {
   //      var startIndex = Utils.SearchForStartPoint(location, _imageLocations, i => i, Utils.FindOptions.StartOrBefore);
   //      return
   //         startIndex < _imageLocations.Count &&
   //         location > _imageLocations[startIndex] &&
   //         location < _imageLocations[startIndex] + _imageLengths[startIndex];
   //   }
   //   public FrameworkElement GetInterpretation(int location) {
   //      if (!_imageLocations.Contains(location)) return null;
   //      InterpretData(location);
   //      return _interpretations[location];
   //   }

   //   public IList<int> Find(string term) { return null; }

   //   void InterpretData(int dataIndex) {
   //      if (!_interpretations.ContainsKey(dataIndex)) {
   //         var dataBytes = GbaImages.UncompressLZ(_data, dataIndex);
   //         var interpretation = Interpret(dataBytes);
   //         _interpretations[dataIndex] = interpretation;
   //      }
   //   }

   //   Path UsePath(int source) {
   //      var geometry = Utils.ByteFlyweights[_data[source]];
   //      if (_recycles.Count > 0) {
   //         var element = _recycles.Dequeue();
   //         element.Data = geometry;
   //         return element;
   //      }

   //      var path = new Path {
   //         HorizontalAlignment = HorizontalAlignment.Center,
   //         VerticalAlignment = VerticalAlignment.Center,
   //         Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
   //         Fill = Solarized.Brushes.Cyan,
   //         Data = geometry
   //      };

   //      path.SetCreator(this);
   //      return path;
   //   }
   //}

   //class LzFactory {
   //   public static IPartialParser Images(byte[] data) {
   //      return new Lz<Image>(data, GbaImages.FindLZImages(data)) {
   //         Interpret = dataBytes => {
   //            int width, height; GbaImages.GuessWidthHeight(dataBytes.Length, out width, out height);
   //            var source = GbaImages.Expand16bitImage(dataBytes, GbaImages.DefaultPalette, width, height);
   //            return new Image { Source = source, Width = width, Height = height };
   //         }
   //      };
   //   }

   //   public static IPartialParser Palette(byte[] data) {
   //      return new Lz<Grid>(data, GbaImages.FindLZPalettes(data)) {
   //         Interpret = dataBytes => {
   //            var grid = new Grid { Width = 40, Height = 40, Background = Brushes.Transparent };
   //            for (int i = 0; i < 4; i++) {
   //               grid.RowDefinitions.Add(new RowDefinition());
   //               grid.ColumnDefinitions.Add(new ColumnDefinition());
   //            }
   //            var palette = new GbaImages.Palette(dataBytes);
   //            for (int i = 0; i < 16; i++) {
   //               var rectangle = new Rectangle {
   //                  Fill = new SolidColorBrush(palette.Colors[i]),
   //                  Margin = new Thickness(1)
   //               };
   //               Grid.SetRow(rectangle, i / 4);
   //               Grid.SetColumn(rectangle, i % 4);
   //               grid.Children.Add(rectangle);
   //            }
   //            return grid;
   //         }
   //      };
   //   }
   //}
}
