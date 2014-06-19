using Microsoft.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

   class ScriptedDataTypes : IRunParser, IDataTypeFinder {
      readonly PointerMapper _mapper;
      readonly ScriptEngine _engine;
      readonly ScriptScope _scope;
      readonly string[] _scripts;

      public ScriptedDataTypes(PointerMapper mapper, ScriptEngine engine, ScriptScope scope, params string[] scriptList) {
         _mapper = mapper; _engine = engine; _scope = scope; _scripts = scriptList;
      }

      public IEnumerable<int> Find(string term) { return null; }

      IRunStorage _runs;
      public void Load(IRunStorage runs) {
         _runs = runs;
         _scope.SetVariable("types", this);
         var dir = AppDomain.CurrentDomain.BaseDirectory + "/pokemon_datatypes/";
         foreach (var script in _scripts) {
            var source = _engine.CreateScriptSourceFromFile(dir + script);
            // var code = source.Compile();
            source.Execute(_scope);
         }
      }

      public int FindVariableArray(byte ender, ChildReader reader) {
         var lengthFinder = new DataTypeLengthFinder();
         reader(lengthFinder);
         int stride = lengthFinder.Length;

         var addressesList = _mapper.OpenDestinations.ToList();
         var matchingLayouts = new List<int>();
         var matchingLengths = new List<int>();
         foreach (var address in addressesList) {

            int elementCount = 0;
            while (address + stride * elementCount < _runs.Data.Length && _runs.Data[address + stride * elementCount] != ender) {
               elementCount++;
            }
            if (address + stride * elementCount >= _runs.Data.Length) continue;
            if (elementCount < 10) continue;

            var parser = new PokemonDataTypeParser(_runs, address);
            for (int i = 0; i < elementCount; i++) {
               reader(parser);
               if (parser.FaultReason != null) break;
            }
            if (parser.FaultReason != null) continue;
            matchingLayouts.Add(address);
            matchingLengths.Add(elementCount);
         }

         var counts = new List<double>();
         for (int i = 0; i < matchingLayouts.Count; i++) {
            var layout = matchingLayouts[i];
            var length = matchingLengths[i];
            int repeatCount = 0;
            byte prev = _runs.Data[layout];
            for (int j = 1; j < length; j++) {
               var current = _runs.Data[layout + j];
               if (prev == current) repeatCount++;
               prev = current;
            }
            counts.Add((double)repeatCount / length);
         }

         var least = counts.Min();
         var index = counts.IndexOf(least);

         var factory = new PokemonDatatypeFactory(_runs, _mapper, matchingLayouts[index]);
         for (int i = 0; i < matchingLengths[index]; i++) {
            reader(factory);
         }
         return matchingLayouts[index];
      }

      public GbaPointer[] FindMany(ChildReader reader) {
         var matchingPointers = new List<GbaPointer>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            var parser = new PokemonDataTypeParser(_runs, address);
            reader(parser);
            if (parser.FaultReason != null) continue;
            var factory = new PokemonDatatypeFactory(_runs, _mapper, address);
            reader(factory);

            matchingPointers.Add(new GbaPointer { source = _mapper.PointersFromDestination(address).First(), destination = address });
         }
         return matchingPointers.ToArray();
      }

      /// <summary>
      /// Given a list of pointers, finds the ones that are pointed to.
      /// Returns the pointers pointing to the list.
      /// </summary>
      public GbaPointer[] FollowPointersUp(GbaPointer[] locations) {
         var pointerSet = new List<GbaPointer>();
         foreach (var destination in locations) {
            var pointers = _mapper.PointersFromDestination(destination.source);
            if (pointers == null || pointers.Length == 0) continue;
            pointerSet.Add(new GbaPointer { source = pointers.First(), destination = destination.source }); // can I do without the first?
         }
         return pointerSet.ToArray();
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

      public int this[byte bank, byte map] {
         get {
            int bankPointer = _masterMapAddress + bank * 4;
            int mapPointer = _data.ReadPointer(bankPointer) + map * 4;
            return _data.ReadPointer(mapPointer);
         }
      }
   }

   enum Offset { IconImage, IconPalette, IconPaletteIndex }

   class SpeciesElementProvider : IElementProvider {
      public static readonly IElementProvider Instance = new SpeciesElementProvider();
      SpeciesElementProvider() { }

      readonly Queue<Image> _recycles = new Queue<Image>();
      readonly Queue<Rectangle> _empties = new Queue<Rectangle>();
      readonly IDictionary<int, ImageSource> _cache = new Dictionary<int, ImageSource>();

      public bool IsEquivalent(IElementProvider other) { return other == this; }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         if (innerIndex == 1) return ProvideEmpty();
         int index = data.ReadShort(runStart);
         var image = ProvideImage();
         ImageSource source;
         if (_cache.ContainsKey(index)) {
            source = _cache[index];
         } else {
            source = Thumbnails.CropIcon(Thumbnails.GetIcon(data, data.ReadShort(runStart)));
            source.Freeze();
            _cache[index] = source;
         }
         image.Source = source;
         return image;
      }

      public void Recycle(FrameworkElement element) {
         if (element is Image) _recycles.Enqueue((Image)element);
         else if (element is Rectangle) _empties.Enqueue((Rectangle)element);
         else Debug.Fail("Cannot deal with this kind of element.");
      }

      FrameworkElement ProvideEmpty() {
         if (_empties.Count > 0) return _empties.Dequeue();
         return new Rectangle();
      }

      Image ProvideImage() {
         if (_recycles.Count > 0) return _recycles.Dequeue();
         var image = new Image { Width = MainWindow.ElementWidth, Height = MainWindow.ElementHeight };
         Grid.SetColumnSpan(image, 2);
         return image;
      }
   }

   class MapElementProvider : IElementProvider {
      readonly GeometryElementProvider _provider = new GeometryElementProvider(Utils.ByteFlyweights, Solarized.Brushes.Orange, true, "map reference");
      readonly Maps _maps;
      public MapElementProvider(Maps maps) { _maps = maps; }

      public bool IsEquivalent(IElementProvider other) {
         var that = other as MapElementProvider;
         if (that == null) return false;
         return that._maps == _maps;
      }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         var element = _provider.ProvideElement(commandFactory, data, runStart, innerIndex, runLength);
         byte bank = data[runStart];
         byte map = data[runStart + 1];
         commandFactory.CreateJumpCommand(element, _maps[bank, map]);
         return element;
      }

      public void Recycle(FrameworkElement element) { _provider.Recycle(element); }
   }

   class WildData : IRunParser {
      #region Setup

      static readonly DataType _level = new DataType(1, new SimpleDataRun(new GeometryElementProvider(Utils.NumericFlyweights, Solarized.Brushes.Yellow), 1));
      static readonly DataType _species = new DataType(2, new SimpleDataRun(SpeciesElementProvider.Instance, 2));

      static readonly Entry[] _encounterChildren = new[] {
         new Entry("lowLevel", _level), new Entry("highLevel", _level), new Entry("species", _species)
      };

      #endregion

      readonly Entry _dataLayout;
      readonly PointerMapper _mapper;
      int _layout, _count;

      public WildData(PointerMapper mapper, Maps maps) {
         _mapper = mapper;
         var names = new[] { "grass", "surf", "tree", "fishing" };
         var lengths = new[] { 12, 5, 5, 10 };
         var entryList = new List<Entry>();
         var mapType = new DataType(2, new SimpleDataRun(new MapElementProvider(maps), 2));
         entryList.Add(new Entry("bank_map", mapType));
         entryList.Add(new Entry("_", DataTypes.@short));
         for (int i = 0; i < names.Length; i++) {
            entryList.Add(new Entry(names[i], DataTypes.@nullablepointer,
               new Entry("rate", DataTypes.@word),
               new Entry("encounters", lengths[i].ToString(), _encounterChildren)));
         }
         _dataLayout = new Entry("wild", "+FF", entryList.ToArray());
      }

      public IEnumerable<int> Find(string term) { if (term == "wild") yield return _layout; }

      public void Load(IRunStorage runs) {
         var matchingLayouts = new List<int>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            if (runs.Data[address + 2] != 0) continue;
            if (runs.Data[address + 3] != 0) continue;
            if (!DataTypes.CouldBe(runs.Data, _dataLayout, address, addressesList)) continue;
            matchingLayouts.Add(address);
         }

         var counts = new List<double>();
         foreach (var layout in matchingLayouts) {
            int repeatCount = 0;
            int len = DataTypes.FindDynamicLength(runs.Data, layout, _dataLayout);
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
         _count = DataTypes.FindDynamicLength(runs.Data, _layout, _dataLayout);
         DataTypes.SeekChildren(runs, _dataLayout, _layout, _mapper);
         _mapper.Claim(runs, _layout);
      }
   }

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

      public static BitmapSource GetIcon(byte[] data, int index) {
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
         return ImageUtils.Expand16bitImage(dataBytes, palette, width, height);
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
               Interpret = (d, dex) => {
                  var source = GetIcon(data, i);
                  var image = new Image { Source = source, Width = source.Width, Height = source.Height };
                  return image;
               }
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

   /*
   class TrainerData : IRunParser {
      public readonly DataType _string;
      readonly PCS _pcs;

      public TrainerData(PCS pcs) {
         _pcs = pcs;
         _string = new DataType(12, new VariableLengthDataRun(0xFF, 1, Solarized.Brushes.Violet, _pcs._pcsVisuals));
         var children = new IEntry[] {
            new SEntry("pokemonStructureType", DataTypes.@byte),
            new SEntry("trainerClass", DataTypes.@byte),
            new SEntry("introMusic", DataTypes.@byte),
            new SEntry("sprite", DataTypes.@byte),
            new SEntry("name", _string),
            new SEntry("unknown", DataTypes.@unknown4),
            new SEntry("unknown", DataTypes.@unknown4),
            new SEntry("unknown", DataTypes.@unknown4),
            new SEntry("unknown", DataTypes.@unknown4),
            new SEntry("pokecount", DataTypes.@byte),
            new SEntry("_", DataTypes.@byte),
            new SEntry("_", DataTypes.@short),
            null
         };
         _dataLayout = new SEntry("trainer", children);
         children[children.Length - 1] = new VariableEntry(_dataLayout, "pokemonStructureType", _pokemon0.Children, _pokemon1.Children, _pokemon2.Children, _pokemon3.Children);
      }

      public IEnumerable<int> Find(string term) { return null; }

   //   trainer: // there are 2E6 trainers, starting at 23EAF0
   //.pokemonStructureType .trainerClass .introMusic .sprite // 0:none 1:attacks 2:items 3:both
   //.12name
   //? // gender, unknown
   //? // money rate
   //? // items
   //?
   //.pokeCount .? .? .?
   //*opponentPokemon
   //    opponentPokemon0: { -ivSpread -level -species -_ }
   //    opponentPokemon1: { -ivSpread -level -species -attack -attack -attack -attack -_ }
   //    opponentPokemon2: { -ivSpread -level -species -item }
   //    opponentPokemon3: { -ivSpread -level -species -item -attack -attack -attack -attack }

      readonly SEntry _pokemon0 = new SEntry("pokemon0", new SEntry("ivspread", DataTypes.@short), new SEntry("level", DataTypes.@short), new SEntry("species", WildData._species), new SEntry("_", DataTypes.@short));
      readonly SEntry _pokemon1 = new SEntry("pokemon1", new SEntry("ivspread", DataTypes.@short), new SEntry("level", DataTypes.@short), new SEntry("species", WildData._species), new SEntry("attack", DataTypes.@short), new SEntry("attack", DataTypes.@short), new SEntry("attack", DataTypes.@short), new SEntry("attack", DataTypes.@short), new SEntry("_", DataTypes.@short));
      readonly SEntry _pokemon2 = new SEntry("pokemon2", new SEntry("ivspread", DataTypes.@short), new SEntry("level", DataTypes.@short), new SEntry("species", WildData._species), new SEntry("item", DataTypes.@short));
      readonly SEntry _pokemon3 = new SEntry("pokemon3", new SEntry("ivspread", DataTypes.@short), new SEntry("level", DataTypes.@short), new SEntry("species", WildData._species), new SEntry("item", DataTypes.@short), new SEntry("attack", DataTypes.@short), new SEntry("attack", DataTypes.@short), new SEntry("attack", DataTypes.@short), new SEntry("attack", DataTypes.@short));

      readonly IEntry _dataLayout;

      public void Load(IRunStorage runs) {

      }
   }
   //*/
}
