using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PCS : IRunParser, IEditor {
      readonly string[] _pcs = new string[0x100];
      readonly Geometry[] _pcsVisuals = new Geometry[0x100]; // leaving it null makes it use the default color and visualization
      readonly IDataRun _stringRun;

      IRunStorage _runs;

      public PCS() {
         _stringRun = new VariableLengthDataRun(0xFF, 1, Solarized.Brushes.Violet, _pcsVisuals) {
            Interpret = GetInterpretation,
            Editor = this
         };
      }

      public void Load(IRunStorage runs) {
         _runs = runs;
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

         for (int i = 0, j = 0; i < _runs.Data.Length; i++) {
            j = _runs.Data[i] == searchTerm1[j] || _runs.Data[i] == searchTerm2[j] ? j + 1 : 0;
            if (j < searchTerm1.Length) continue;
            yield return i - j + 1;
            j = 0;
         }
      }

      #region Editor

      public void Edit(int location, char c) {
         if (c == ' ') {
            _runs.Data[location] = 0;
            MoveToNext(this, EventArgs.Empty);
            return;
         } else if (c == '\n') {
            _runs.Data[location] = 0;
            MoveToNext(this, EventArgs.Empty);
            return;
         }

         for (int i = 0x00; i <= 0xFF; i++) {
            if (_pcs[i] == null) continue;
            if (_pcs[i][0] != c) continue;
            if (_runs.Data[location] == 0xFF) _runs.Data[location + 1] = 0xFF; // TODO this byte needs to show the update too
            _runs.Data[location] = (byte)i;
            MoveToNext(this, EventArgs.Empty);
            return;
         }
      }

      public void CompleteEdit(int location) { }

      public event EventHandler MoveToNext;

      #endregion

      void FindStrings() {
         int currentLength = 0;
         for (int i = 0x200; i < _runs.Data.Length; i++) {
            if (_pcs[_runs.Data[i]] != null) {
               currentLength++;
               continue;
            }
            if (_runs.Data[i] == 0x00 && currentLength > 0) { // accept 0x00 if we've already started
               currentLength++;
               continue;
            } else if (_runs.Data[i] == 0xFD) { // accept 0xFD as the escape character
               i++;
               currentLength += 2;
               continue;
            } else if (_runs.Data[i] == 0xFF && currentLength >= 3) {
               // if all the characters are the same, don't add the run.
               int startLoc = i - currentLength;
               if (!Enumerable.Range(1, currentLength).All(j => _runs.Data[startLoc + j] == _runs.Data[startLoc])) {
                  if (_runs.IsFree(startLoc)) {
                     _runs.AddRun(startLoc, _stringRun);
                  }
               }
            }
            currentLength = 0;
         }
      }

      FrameworkElement GetInterpretation(byte[] data, int location) {
         string result = string.Empty;
         for (int j = 0; data[location + j] != 0xFF; j++) {
            if (data[location + j] == 0x00) {
               result += " ";
            } else if (data[location + j] == 0xFD) {
               result += "\\x" + Utils.ToHexString(data[location + j + 1]);
               j++;
            } else {
               result += _pcs[data[location + j]];
            }
         }
         return new TextBlock { Text = result, Foreground = Solarized.Theme.Instance.Primary, TextWrapping = TextWrapping.Wrap };
      }
   }

   class Maps : IRunParser {
      #region Setup

      static readonly Entry DataLayout = new Entry("map",
         new Entry("mapTileData", DataTypes.@pointer),
         new Entry("mapEventData",
            new Entry("personCount", DataTypes.@byte), new Entry("warpCount", DataTypes.@byte), new Entry("scriptCount", DataTypes.@byte), new Entry("signpostCount", DataTypes.@byte),
            new Entry("persons", "personCount"
               //*
               ,
               new Entry("?", DataTypes.@byte), new Entry("picture", DataTypes.@byte), new Entry("?", DataTypes.@byte), new Entry("?", DataTypes.@byte),
               new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
               new Entry("?", DataTypes.@byte), new Entry("movementType", DataTypes.@byte), new Entry("movement", DataTypes.@byte), new Entry("?", DataTypes.@byte),
               new Entry("isTrainer", DataTypes.@byte), new Entry("?", DataTypes.@byte), new Entry("viewRadius", DataTypes.@short),
               new Entry("script", DataTypes.@nullablepointer),
               new Entry("id", DataTypes.@short), new Entry("?", DataTypes.@byte), new Entry("?", DataTypes.@byte)
               //*/
            ),
            new Entry("warps", "warpCount"
               //*
               ,
               new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
               new Entry("?", DataTypes.@byte), new Entry("warp", DataTypes.@byte), new Entry("map", DataTypes.@byte), new Entry("bank", DataTypes.@byte)
               //*/
            ),
            new Entry("scripts", "scriptCount"
               //*
               ,
               new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
               new Entry("?", DataTypes.@short), new Entry("scriptVariable", DataTypes.@short),
               new Entry("scriptVariableValue", DataTypes.@short), new Entry("?", DataTypes.@short),
               new Entry("script", DataTypes.@nullablepointer)
               //*/
            ),
            new Entry("signposts", "signpostCount"
               //*
               ,
               new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
               new Entry("talkingLevel", DataTypes.@byte), new Entry("signpostType", DataTypes.@byte), new Entry("?", DataTypes.@short),
               new Entry("?", DataTypes.@unknown4)
               // new Entry("dynamic", new DataType(4)) // *script || -itemID .hiddenID .amount || <missing>? //*/
               //*/
            )
         ),
         new Entry("script", DataTypes.@nullablepointer),
         new Entry("connections", DataTypes.@nullablepointer,
            new Entry("count", DataTypes.@word),
            new Entry("data", DataTypes.@pointer)
         ),
         new Entry("song", DataTypes.@short), new Entry("map", DataTypes.@short),
         new Entry("label_id", DataTypes.@byte), new Entry("flash", DataTypes.@byte), new Entry("weather", DataTypes.@byte), new Entry("type", DataTypes.@byte),
         new Entry("_", DataTypes.@short), new Entry("labelToggle", DataTypes.@byte), new Entry("_", DataTypes.@byte)
      );

      #endregion

      readonly PointerMapper _mapper;
      byte[] _data;
      SortedSet<int> _mapLocations;
      SortedSet<int> _mapBankAddress;
      SortedSet<int> _allSubsetAddresses;
      int _masterMapAddress;

      public Maps(PointerMapper mapper) { _mapper = mapper; }

      public void Load(IRunStorage runs) {
         _data = runs.Data;
         var code = Header.GetCode(_data);

         //           ruby            sapphire          emerald
         if (code == "AXVE" || code == "AXPE" || code == "BPEE") {
            DataLayout.Children[0] = new Entry("mapTileData",
               new Entry("width", DataTypes.@word), new Entry("height", DataTypes.@word),
               new Entry("borderTile", DataTypes.@pointer),
               new Entry("tiles", DataTypes.@pointer),
               new Entry("tileset", DataTypes.@pointer),
               new Entry("tileset", DataTypes.@pointer)
            );
         } else { // FR / LG
            DataLayout.Children[0] = new Entry("mapTileData",
               new Entry("width", DataTypes.@word), new Entry("height", DataTypes.@word),
               new Entry("borderTile", DataTypes.@pointer),
               new Entry("tiles", DataTypes.@pointer),
               new Entry("tileset", DataTypes.@pointer),
               new Entry("tileset", DataTypes.@pointer),
               new Entry("borderWidth", DataTypes.@byte), new Entry("borderHeight", DataTypes.@byte), new Entry("_", DataTypes.@byte), new Entry("_", DataTypes.@byte)
            );
         }

         // match the nested layout
         _allSubsetAddresses = new SortedSet<int>();
         var matchingLayouts = new List<int>();
         var matchingPointers = new List<int>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            if (!DataTypes.CouldBe(_data, DataLayout, address, addressesList)) continue;
            DataTypes.SeekChildren(runs, DataLayout, address, _mapper);
            _mapper.Claim(runs, address);
            matchingPointers.Add(_mapper.PointersFromDestination(address).First()); // can I do without the first?
            matchingLayouts.Add(address);
         }

         // third pass: find which of the pointers in matchingPointers have references to them (those are the heads of the lists)
         _mapBankAddress = new SortedSet<int>();
         var mapBankPointers = new List<int>();
         foreach (var mapPointer in matchingPointers) {
            var bankPointer = _mapper.PointersFromDestination(mapPointer);
            if (bankPointer == null || bankPointer.Length == 0) continue;
            _mapBankAddress.Add(mapPointer);
            mapBankPointers.Add(bankPointer.First()); // can I do without the first?
         }

         _masterMapAddress = _mapper.OpenDestinations.First(mapBankPointers.Contains);
         _mapLocations = new SortedSet<int>(matchingLayouts);
         foreach (var location in matchingLayouts) _allSubsetAddresses.Add(location);
         foreach (var location in _mapBankAddress) _allSubsetAddresses.Add(location);
      }

      /*
      FrameworkElement GetInterpretation(int location) {
         if (_mapBankAddress.Contains(location)) {
            if (!_interpretations.ContainsKey(location)) _interpretations[location] = new TextBlock { Text = "MapBank", Foreground = Solarized.Theme.Instance.Emphasis };
            return _interpretations[location];
         }

         if (!_mapLocations.Contains(location)) return null;
         if (!_interpretations.ContainsKey(location)) _interpretations[location] = new TextBlock { Text = "Map", Foreground = Solarized.Theme.Instance.Emphasis };
         return _interpretations[location];
      }
      //*/

      public IEnumerable<int> Find(string term) { if (term == "maps") yield return _masterMapAddress; }
   }

   class WildData : IRunParser {
      #region Setup

      static readonly Entry DataLayout = new Entry("wild", "+FF",
         new Entry("bank_map", DataTypes.@short), new Entry("_", DataTypes.@short),
         new Entry("grass", DataTypes.@nullablepointer,
            new Entry("rate", DataTypes.@word),
            new Entry("encounters", "12",
               new Entry("lowLevel", DataTypes.@byte), new Entry("highLevel", DataTypes.@byte), new Entry("species", DataTypes.@short)
            )
         ),
         new Entry("surf", DataTypes.@nullablepointer,
            new Entry("rate", DataTypes.@word),
            new Entry("encounters", "5",
               new Entry("lowLevel", DataTypes.@byte), new Entry("highLevel", DataTypes.@byte), new Entry("species", DataTypes.@short)
            )
         ),
         new Entry("tree", DataTypes.@nullablepointer,
            new Entry("rate", DataTypes.@word),
            new Entry("encounters", "5",
               new Entry("lowLevel", DataTypes.@byte), new Entry("highLevel", DataTypes.@byte), new Entry("species", DataTypes.@short)
            )
         ),
         new Entry("fishing", DataTypes.@nullablepointer,
            new Entry("rate", DataTypes.@word),
            new Entry("encounters", "10",
               new Entry("lowLevel", DataTypes.@byte), new Entry("highLevel", DataTypes.@byte), new Entry("species", DataTypes.@short)
            )
         )
      );

      #endregion

      readonly PointerMapper _mapper;
      int _layout, _count;

      public WildData(PointerMapper mapper) { _mapper = mapper; }

      public IEnumerable<int> Find(string term) { if (term == "wild") yield return _layout; }

      public void Load(IRunStorage runs) {
         var matchingLayouts = new List<int>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            if (runs.Data[address + 2] != 0) continue;
            if (runs.Data[address + 3] != 0) continue;
            if (!DataTypes.CouldBe(runs.Data, DataLayout, address, addressesList)) continue;
            matchingLayouts.Add(address);
         }

         var counts = new List<double>();
         foreach (var layout in matchingLayouts) {
            int repeatCount = 0;
            int len = DataTypes.FindDynamicLength(runs.Data, layout, DataLayout);
            byte prev = runs.Data[layout];
            for (int i = 1; i < len; i++) {
               var current = runs.Data[layout + i];
               if (prev == current) repeatCount++;
               prev = current;
            }
            counts.Add((double)repeatCount / len);
         }

         var least = counts.Min();
         var index = counts.IndexOf(least);
         _layout = matchingLayouts[index];
         _count = DataTypes.FindDynamicLength(runs.Data, _layout, DataLayout);
         DataTypes.SeekChildren(runs, DataLayout, _layout, _mapper);
         _mapper.Claim(runs, _layout);
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
         { "BPEE", _fireredLeafgreen },
         { "BPRE", _fireredLeafgreen },
         { "BPGE", _fireredLeafgreen }
      };

      public static FrameworkElement GetIcon(byte[] data, int index) {
         var code = Header.GetCode(data);
         var table = _tables[code];
         int imageOffset = data.ReadPointer(table[Offset.IconImage]);
         int paletteOffset = data.ReadPointer(table[Offset.IconPaletteIndex]);
         int paletteTable = data.ReadPointer(table[Offset.IconPalette]);

         paletteOffset += index;
         imageOffset += index * 4;

         var paletteStart = data.ReadPointer(paletteTable + data[paletteOffset] * 8);
         var imageStart = data.ReadPointer(imageOffset);

         var dataBytes = new byte[0x400];
         Array.Copy(data, imageStart, dataBytes, 0, 0x400);
         var paletteBytes = new byte[0x20];
         Array.Copy(data, paletteStart, paletteBytes, 0, 0x20);
         var palette = new ImageUtils.Palette(paletteBytes);
         int width = 32, height = 64;
         var source = ImageUtils.Expand16bitImage(dataBytes, palette, width, height);
         var image = new Image { Source = source, Width = width, Height = height };
         return image;
      }

      public static void CropIcon(Image image) {
         int newWidth = MainWindow.ElementWidth, newHeight = MainWindow.ElementHeight;
         int oldWidth = (int)image.Width, oldHeight = (int)image.Height;
         int x = (oldWidth - newWidth) / 2, y = (oldHeight / 2 - newHeight) / 2 + 2;
         image.Source = new CroppedBitmap((BitmapSource)image.Source, new Int32Rect(x, y, newWidth, newHeight));
         image.Width = 24;
         image.Height = 24;
      }

      #endregion

      readonly PointerMapper _mapper;

      public Thumbnails(PointerMapper mapper) { _mapper = mapper; }

      public IEnumerable<int> Find(string term) { return null; }

      public void Load(IRunStorage runs) {
         var data = runs.Data;
         var code = Header.GetCode(data);
         var table = _tables[code];

         int imageOffset = data.ReadPointer(table[Offset.IconImage]);
         _mapper.Claim(runs, imageOffset);

         int index = 0;
         while (data[imageOffset + 3] == 0x08) {
            int i = index; // closure
            var run = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), 0x400) {
               Interpret = (d, dex) => GetIcon(data, i)
            };

            _mapper.Claim(runs, run, data.ReadPointer(imageOffset));

            imageOffset += 4;
            index++;
         }

         byte[] palettes = new byte[index];
         Array.Copy(data, data.ReadPointer(table[Offset.IconPaletteIndex]), palettes, 0, index);
         int paletteCount = palettes.Max() + 1;
         int pointersToPalettes = data.ReadPointer(table[Offset.IconPalette]);
         var paletteRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), 0x20) {
            Interpret = (d, dex) => {
               var dataBytes = new byte[0x20];
               Array.Copy(d, dex, dataBytes, 0, dataBytes.Length);
               return Lz.InterpretPalette(dataBytes);
            }
         };
         for (int i = 0; i < paletteCount; i++) _mapper.Claim(runs, paletteRun, data.ReadPointer(pointersToPalettes + i * 8));

         _mapper.Claim(runs, new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), index), data.ReadPointer(table[Offset.IconPaletteIndex]));
         _mapper.Claim(runs, data.ReadPointer(table[Offset.IconPalette]));
      }
   }
}
