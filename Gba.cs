using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class VariableLengthDataRun : IDataRun {
      readonly byte _endCharacter;
      readonly int _stride;

      public Brush Color { get; set; }
      public Geometry[] Parser { get; set; }

      public string HoverText { get; set; }
      public bool Underlined { get; set; }
      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }

      public VariableLengthDataRun(byte endCharacter, int stride, Brush color, Geometry[] parser) {
         _endCharacter = endCharacter; _stride = stride;
         Color = color; Parser = parser;
      }

      public int GetLength(byte[] data, int startPoint) {
         int len = 0;
         while (data[startPoint + len] != _endCharacter) len += _stride;
         return len + _stride;
      }
   }

   class NestingDataRun : IDataRun {
      readonly IDataRun[] _children;

      public NestingDataRun(params IDataRun[] children) { _children = children; }

      public Brush Color { get; set; }
      public Geometry[] Parser { get; set; }
      public string HoverText { get; set; }
      public bool Underlined { get; set; }
      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }

      public int GetLength(byte[] data, int startPoint) {
         int lengthSum = 0;
         foreach (var run in _children) {
            int length = run.GetLength(data, startPoint);
            lengthSum += length;
            startPoint += length;
         }
         return lengthSum;
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

      readonly PointerMapper _pointers;

      public Header(PointerMapper mapper) { _pointers = mapper; }

      public void Load(IRunStorage runs) {
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

      public void Load(IRunStorage runs) {
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
               ImageUtils.CalculateLZSizes(runs.Data, loc, out uncompressed, out compressed);
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
         var dataBytes = ImageUtils.UncompressLZ(data, location);
         int width, height; ImageUtils.GuessWidthHeight(dataBytes.Length, out width, out height);
         var source = ImageUtils.Expand16bitImage(dataBytes, ImageUtils.DefaultPalette, width, height);
         return new Image { Source = source, Width = width, Height = height };
      }

      static FrameworkElement InterpretPalette(byte[] data, int location) {
         var dataBytes = ImageUtils.UncompressLZ(data, location);
         var grid = new Grid { Width = 40, Height = 40, Background = Brushes.Transparent };
         for (int i = 0; i < 4; i++) {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
         }
         var palette = new ImageUtils.Palette(dataBytes);
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
