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
      static SimpleDataRun HeaderRun(int len, string text, Geometry[] converter) { return new SimpleDataRun(len, Solarized.Brushes.Violet, converter) { HoverText = text, Underlined = true }; }
      static SimpleDataRun HeaderRun(int len, string text) { return HeaderRun(len, text, Utils.ByteFlyweights); }

      static readonly SimpleDataRun[] _headerRuns = new[] {
         HeaderRun(4, "ARM7 Entry Instruction"),
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
}
