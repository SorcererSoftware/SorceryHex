using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SorceryHex.Gba.Pokemon {
   public class PCS : IRunParser, IEditor {
      readonly string[] _pcs = new string[0x100];
      readonly Geometry[] _pcsVisuals = new Geometry[0x100]; // leaving it null makes it use the default color and visualization

      public readonly IDataRun StringRun;

      IRunStorage _runs;

      public PCS() {
         StringRun = new VariableLengthDataRun(0xFF, 1, Solarized.Brushes.Violet, _pcsVisuals) {
            Interpret = GetInterpretation,
            Editor = this
         };

         foreach (var line in System.IO.File.ReadAllLines("data\\PCS3-W.ini")) {
            var sanitized = line.Trim();
            if (sanitized.StartsWith("#") || sanitized.Length == 0) continue;
            Debug.Assert(sanitized.StartsWith("0x"));
            var key = (byte)sanitized.Substring(2, 2).ParseAsHex();
            var value = sanitized.Substring(sanitized.IndexOf("'") + 1);
            value = value.Substring(0, value.Length - 1);
            _pcs[key] = value;
            _pcsVisuals[key] = value.ToGeometry();
         }
         _pcsVisuals[0x00] = "".ToGeometry();
         _pcsVisuals[0xFD] = "\\x".ToGeometry();
      }

      public void Load(ICommandFactory commander, IRunStorage runs) {
         _runs = runs;
         FindStrings();
      }

      public IEnumerable<int> Find(string term) {
         if (!term.All(s => s == ' ' || _pcs.Contains(new string(s, 1)))) yield break;

         var lower = term.ToLower();
         var upper = term.ToUpper();
         var byteRange = Enumerable.Range(0, 0x100).Select(i => (byte)i);

         // two searchTerms, one with caps and one with lowercase
         byte[] searchTerm1 =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : byteRange.First(key => _pcs[key] == lower.Substring(i, 1)))
            .ToArray();

         byte[] searchTerm2 =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : byteRange.First(key => _pcs[key] == upper.Substring(i, 1)))
            .ToArray();

         for (int i = 0, j = 0; i < _runs.Segment.Length; i++) {
            j = _runs.Segment[i] == searchTerm1[j] || _runs.Segment[i] == searchTerm2[j] ? j + 1 : 0;
            if (j < searchTerm1.Length) continue;
            yield return i - j + 1;
            j = 0;
         }
      }

      #region Editor

      public FrameworkElement CreateElementEditor(ISegment segment) { return null; }

      public void Edit(ISegment segment, char c) {
         if (c == ' ') {
            segment.Write(0, 1, 0);
            MoveToNext(this, new UpdateLocationEventArgs(segment.Location));
            return;
         } else if (c == '\n') {
            segment.Write(0, 1, 0);
            MoveToNext(this, new UpdateLocationEventArgs(segment.Location));
            return;
         }

         for (int i = 0x00; i <= 0xFF; i++) {
            if (_pcs[i] == null) continue;
            if (_pcs[i][0] != c) continue;
            if (segment.Read(0, 1) == 0xFF) segment.Write(1, 1, 0xFF);
            segment.Write(0, 1, i);
            MoveToNext(this, new UpdateLocationEventArgs(segment.Location, segment.Location + 1));
            return;
         }
      }

      public void CompleteEdit(ISegment segment) { }

      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      #endregion

      public string ReadString(ISegment segment, int maxLength = -1) {
         string result = string.Empty;
         int j = 0;
         for (; segment[j] != 0xFF && (j < maxLength || maxLength == -1); j++) {
            if (segment[j] == 0x00) {
               result += " ";
            } else if (segment[j] == 0xFD) {
               result += "\\x" + Utils.ToHexString(segment[j + 1]);
               j++;
            } else {
               if (_pcs[segment[j]] == null) return null;
               result += _pcs[segment[j]];
            }
         }
         if (j >= maxLength && maxLength != -1) return null;
         return result;
      }

      public void WriteString(ISegment segment, int length, string value) {
         throw new NotImplementedException();
      }

      void FindStrings() {
         int currentLength = 0;
         int currentSkip;
         for (int i = 0x200; i < _runs.Segment.Length; i++) {

            // phase one: quickly find something that looks string-like
            currentSkip = 0;
            while (currentLength == 0 && i < _runs.Segment.Length && _pcs[_runs.Segment[i]] == null) {
               currentSkip = Math.Min(currentSkip + 1, 0x10);
               i += currentSkip;
            }
            if (currentSkip > 0) i -= currentSkip - 1;

            // phase two: read to see if it's a string
            while (i < _runs.Segment.Length) {
               if (_pcs[_runs.Segment[i]] != null) {
                  currentLength++;
                  i++;
                  continue;
               }
               if (_runs.Segment[i] == 0x00 && currentLength > 0) { // accept 0x00 if we've already started
                  currentLength++;
                  i++;
                  continue;
               }
               if (_runs.Segment[i] == 0xFD) { // accept 0xFD as the escape character
                  i += 2;
                  currentLength += 2;
                  continue;
               }
               if (_runs.Segment[i] == 0xFF && currentLength >= 3) {
                  // if there are more than 3 of the same character in a row, don't add the run.
                  int startLoc = i - currentLength;

                  byte prevChar = _runs.Segment[startLoc];
                  int length = 0;
                  for (int j = 1; j < currentLength; j++) {
                     byte currentChar = _runs.Segment[startLoc + j];
                     length = (prevChar == currentChar) ? length + 1 : 0;
                     prevChar = currentChar;
                     if (length > 3) {
                        break;
                     }
                  }

                  if (length <= 3) {
                     if (_runs.IsFree(startLoc)) {
                        if (_runs.NextUsed(startLoc) > startLoc + currentLength) {
                           _runs.AddRun(startLoc, StringRun);
                        }
                     }
                  }
               }
               break;
            }
            currentLength = 0;
         }
      }

      FrameworkElement GetInterpretation(ISegment segment) {
         var result = ReadString(segment);
         return new TextBlock { Text = result, Foreground = Solarized.Theme.Instance.Primary, TextWrapping = TextWrapping.Wrap };
      }
   }

   enum Offset { IconImage, IconPalette, IconPaletteIndex }

   class Thumbnails : IRunParser {
      #region Utils

      public static readonly Brush MediaBrush = Solarized.Brushes.Cyan;

      static readonly IDictionary<Offset, int> _fireredLeafgreen = new Dictionary<Offset, int> {
         { Offset.IconImage, 0x138 },
         { Offset.IconPaletteIndex, 0x13C },
         { Offset.IconPalette, 0x140 }
      };

      static readonly IDictionary<Offset, int> _rubySapphire = new Dictionary<Offset, int> {
         { Offset.IconImage, 0x99AA0 },
         { Offset.IconPaletteIndex, 0x99BB0 },
         { Offset.IconPalette, 0x9D53C }
      };

      static readonly IDictionary<string, IDictionary<Offset, int>> _tables = new Dictionary<string, IDictionary<Offset, int>> {
         { "AXVE", _rubySapphire },
         { "AXPE", _rubySapphire },
         { "BPEE", _fireredLeafgreen }, // emerald
         { "BPRE", _fireredLeafgreen },
         { "BPGE", _fireredLeafgreen }
      };

      public static BitmapSource GetIcon(ISegment segment, int index) {
         var code = Header.GetCode(segment);
         var table = _tables[code];
         var imageOffset = segment.Follow(table[Offset.IconImage]);
         var paletteOffset = segment.Follow(table[Offset.IconPaletteIndex]);
         var paletteTable = segment.Follow(table[Offset.IconPalette]);

         paletteOffset = paletteOffset.Inner(index);
         imageOffset = imageOffset.Inner(index * 4);

         var paletteStart = paletteTable.Follow(paletteOffset[0] * 8);
         var imageStart = imageOffset.Follow(0);

         var palette = new ImageUtils.Palette(paletteStart.Resize(0x20));
         int width = 32, height = 64;
         return ImageUtils.Expand16bitImage(imageStart.Resize(0x400), palette, width, height);
      }

      public static ImageSource CropIcon(BitmapSource source) {
         int newWidth = MainWindow.ElementWidth, newHeight = MainWindow.ElementHeight;
         int oldWidth = (int)source.Width, oldHeight = (int)source.Height;
         int x = (oldWidth - newWidth) / 2, y = (oldHeight / 2 - newHeight) / 2 + 2;
         return new CroppedBitmap((BitmapSource)source, new Int32Rect(x, y, newWidth, newHeight));
      }

      #endregion

      readonly PointerMapper _mapper;

      public Thumbnails(PointerMapper mapper) { _mapper = mapper; }

      public IEnumerable<int> Find(string term) { return null; }

      public void Load(ICommandFactory commander, IRunStorage runs) {
         var data = runs.Segment;
         var code = Header.GetCode(data);
         var table = _tables[code];

         var imageOffset = data.Follow(table[Offset.IconImage]);
         _mapper.Claim(runs, imageOffset.Location);

         int index = 0;
         while (imageOffset[3] == 0x08) {
            int i = index; // closure
            var run = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), 0x400) {
               Interpret = seg => {
                  var source = GetIcon(runs.Segment, i);
                  var image = new Image { Source = source, Width = source.Width, Height = source.Height };
                  return image;
               }
            };

            _mapper.Claim(runs, run, imageOffset.Follow(0).Location);

            imageOffset = imageOffset.Inner(4);
            index++;
         }

         var palettes = data.Follow(table[Offset.IconPaletteIndex]).Resize(index);
         int paletteCount = Enumerable.Range(0, palettes.Length).Select(i => palettes[i]).Max() + 1;
         var pointersToPalettes = data.Follow(table[Offset.IconPalette]);
         var paletteRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), 0x20) {
            Interpret = Lz.InterpretUncompressedPalette
         };
         for (int i = 0; i < paletteCount; i++) _mapper.Claim(runs, paletteRun, pointersToPalettes.Follow(i * 8).Location);

         _mapper.Claim(runs, new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), index), data.Follow(table[Offset.IconPaletteIndex]).Location);
         _mapper.Claim(runs, data.Follow(table[Offset.IconPalette]).Location);
      }
   }
}
