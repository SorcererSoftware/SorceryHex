using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
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

      readonly PointerMapper _pointers;

      public Header(PointerMapper mapper) { _pointers = mapper; }

      public void Load(RunStorage runs) {
         int offset = 0;
         foreach (var run in _headerRuns) {
            runs.AddRun(offset, run);
            offset += run.GetLength(runs.Data, offset);
         }
         _pointers.FilterPointer(i => i >= offset);
      }

      public IEnumerable<int> Find(string term) { return null; }
   }

   class Lz : IRunParser {
      static SimpleDataRun LzRun(int len, InterpretationRule interpret) { return new SimpleDataRun(len, Solarized.Brushes.Cyan, Utils.ByteFlyweights) { Interpret = interpret }; }

      readonly PointerMapper _pointers;

      public Lz(PointerMapper pointers) { _pointers = pointers; }

      public void Load(RunStorage runs) {
         var initialContitions = new Func<int, bool>[]{
            loc => runs.Data[loc + 0] == 0x10 && runs.Data[loc + 1] == 0x20 &&
                   runs.Data[loc + 2] == 0x00 && runs.Data[loc + 3] == 0x00,
            loc => runs.Data[loc + 0] == 0x10 && runs.Data[loc + 1] % 20 == 0
         };
         var interpretations = new InterpretationRule[] { InterpretPalette, InterpretImage };

         for (int i = 0; i < initialContitions.Length; i++) {
            int count = 0;
            foreach (var loc in _pointers.OpenDestinations) {
               if (!initialContitions[i](loc)) continue;
               int uncompressed, compressed;
               GbaImages.CalculateLZSizes(runs.Data, loc, out uncompressed, out compressed);
               if (uncompressed == -1 || compressed == -1) continue;
               var run = LzRun(compressed, interpretations[i]);
               _pointers.Claim(runs, run, loc);
               runs.AddRun(loc, run);
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
