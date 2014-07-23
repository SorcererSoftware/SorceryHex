using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }
      public IEditor Editor { get; set; }

      public IElementProvider Provider { get; private set; }

      public VariableLengthDataRun(byte endCharacter, int stride, Brush color, Geometry[] parser) {
         Provider = new GeometryElementProvider(parser, color);
         _endCharacter = endCharacter; _stride = stride;
         Color = color; Parser = parser;
      }

      public ISegment GetLength(ISegment segment) {
         int len = 0;
         while (segment[len] != _endCharacter) len += _stride;
         return segment.Resize(len + _stride);
      }
   }

   class Header : IRunParser {
      public static string GetCode(byte[] rom) {
         return new string(Enumerable.Range(0, 4).Select(i => (char)rom[0xAC + i]).ToArray());
      }

      static SimpleDataRun HeaderRun(int len, string text, Geometry[] converter) { return new SimpleDataRun(new GeometryElementProvider(converter, Solarized.Brushes.Violet, true, text), len); }
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

      public void Load(ICommandFactory commander, IRunStorage runs) {
         int offset = 0;
         foreach (var run in _headerRuns) {
            runs.AddRun(offset, run);
            offset += run.GetLength(new GbaSegment(runs.Data, offset)).Length;
         }
         _pointers.FilterPointer(i => i >= offset);
      }

      public IEnumerable<int> Find(string term) { return null; }
   }

   class Lz : IRunParser {
      static SimpleDataRun LzRun(int len, InterpretationRule interpret) { return new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, Solarized.Brushes.Cyan), len) { Interpret = interpret }; }

      readonly PointerMapper _pointers;

      public Lz(PointerMapper pointers) { _pointers = pointers; }

      public void Load(ICommandFactory commander, IRunStorage runs) {
         var initialContitions = new Func<int, bool>[]{
            loc => runs.Data[loc + 0] == 0x10 && runs.Data[loc + 1] == 0x20 &&
                   runs.Data[loc + 2] == 0x00 && runs.Data[loc + 3] == 0x00,
            loc => runs.Data[loc + 0] == 0x10 && runs.Data[loc + 1] % 0x20 == 0
         };
         var interpretations = new InterpretationRule[] { InterpretCompressedPalette, InterpretImage };

         for (int i = 0; i < initialContitions.Length; i++) {
            int count = 0;
            foreach (var loc in _pointers.OpenDestinations) {
               if (!initialContitions[i](loc)) continue;
               int uncompressed, compressed;
               ImageUtils.CalculateLZSizes(new GbaSegment(runs.Data, loc), out uncompressed, out compressed);
               if (uncompressed == -1 || compressed == -1) continue;
               var run = LzRun(compressed, interpretations[i]);
               _pointers.Claim(runs, run, loc);
               count++;
            }
         }
      }

      public IEnumerable<int> Find(string term) { return null; }

      static FrameworkElement InterpretImage(ISegment segment) {
         var dataBytes = ImageUtils.UncompressLZ(segment);
         Debug.Assert(dataBytes != null);
         int width, height; ImageUtils.GuessWidthHeight(dataBytes.Length, out width, out height);
         var source = ImageUtils.Expand16bitImage(dataBytes, ImageUtils.DefaultPalette, width, height);
         return new Image { Source = source, Width = width, Height = height };
      }

      static FrameworkElement InterpretCompressedPalette(ISegment segment) {
         var dataBytes = ImageUtils.UncompressLZ(segment);
         return InterpretUncompressedPalette(dataBytes);
      }

      public static FrameworkElement InterpretUncompressedPalette(ISegment segment) {
         var grid = new Grid { Width = 40, Height = 40, Background = Brushes.Transparent };
         for (int i = 0; i < 4; i++) {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
         }
         var palette = new ImageUtils.Palette(segment);
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

   static class GbaBrushes {
      public static Brush Error = Solarized.Brushes.Red;      // not used to color data
      public static Brush Pointer = Solarized.Brushes.Orange; // links. Not meant to be hand-edited
      public static Brush Number = Solarized.Brushes.Yellow;  // short/simple intuitive in-line data
      public static Brush Other = Solarized.Brushes.Green;    // 
      public static Brush Media = Solarized.Brushes.Cyan;     // Complex/Long data, not meant to be hand-edited
      public static Brush Code = Solarized.Brushes.Blue;      // scripts or assembly
      public static Brush Strings = Solarized.Brushes.Violet; // variable length data with little to no internal structure
      public static Brush Enum = Solarized.Brushes.Magenta;   // in-line data with named elements
      public static Brush Unused = Solarized.Theme.Instance.Secondary;
   }
}
