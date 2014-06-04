using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
         foreach (var line in System.IO.File.ReadAllLines("PCS3-W.ini")) {
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
         if (!term.All(s => s == ' ' || _pcs.Contains(new string(s, 1)))) return null;

         var lower = term.ToLower();
         var upper = term.ToUpper();
         var byteRange = Enumerable.Range(0, 0x100).Select(i => (byte)i);

         // two searchTerms, one with caps and one with lowercase
         var list = new List<int>();
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
            list.Add(i - j + 1);
            j = 0;
         }
         return list;
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

      static IDataRun MediaRun(int length, string text = null) { return new SimpleDataRun(length, Solarized.Brushes.Cyan, Utils.ByteFlyweights) { HoverText = text }; }

      class DataType { public readonly int Length; public DataType(int length) { Length = length; } }

      static readonly DataType @byte = new DataType(1);
      static readonly DataType @short = new DataType(2);
      static readonly DataType @word = new DataType(4);
      static readonly DataType @unknown4 = new DataType(4);
      static readonly DataType @pointer = new DataType(4);
      static readonly DataType @nullablepointer = new DataType(4);

      class Entry {
         public readonly string Name;
         public readonly DataType DataType;
         public readonly Entry[] Children;

         public Entry(string name, params Entry[] children) {
            Name = name; DataType = @pointer; Children = children;
         }

         public Entry(string name, DataType type, params Entry[] children) {
            Name = name; DataType = type; Children = children;
         }
      }

      static readonly Entry DataLayout = new Entry("map",
         new Entry("mapTileData", @pointer),
         new Entry("mapEventData",
            new Entry("personCount", @byte), new Entry("warpCount", @byte), new Entry("scriptCount", @byte), new Entry("signpostCount", @byte),
            new Entry("persons", @nullablepointer
               ,
               new Entry("?", @byte), new Entry("picture", @byte), new Entry("?", @byte), new Entry("?", @byte),
               new Entry("x", @short), new Entry("y", @short),
               new Entry("?", @byte), new Entry("movementType", @byte), new Entry("movement", @byte), new Entry("?", @byte),
               new Entry("isTrainer", @byte), new Entry("?", @byte), new Entry("viewRadius", @short),
               new Entry("script", @nullablepointer),
               new Entry("id", @short), new Entry("?", @byte), new Entry("?", @byte)
            ),
            new Entry("warps", @nullablepointer
               ,
               new Entry("x", @short), new Entry("y", @short),
               new Entry("?", @byte), new Entry("warp", @byte), new Entry("map", @byte), new Entry("bank", @byte)
            ),
            new Entry("scripts", @nullablepointer
               ,
               new Entry("x", @short), new Entry("y", @short),
               new Entry("?", @short), new Entry("scriptVariable", @short),
               new Entry("scriptVariableValue", @short), new Entry("?", @short),
               new Entry("script", @nullablepointer)
            ),
            new Entry("signposts", @nullablepointer
               ,
               new Entry("x", @short), new Entry("y", @short),
               new Entry("talkingLevel", @byte), new Entry("signpostType", @byte), new Entry("?", @short),
               new Entry("?", @unknown4)
               // new Entry("dynamic", new DataType(4)) // *script || -itemID .hiddenID .amount || <missing>? //*/
            )
         ),
         new Entry("script", @nullablepointer),
         new Entry("connections", @nullablepointer,
            new Entry("count", @word),
            new Entry("data", @pointer)
         ),
         new Entry("song", @short), new Entry("map", @short),
         new Entry("label_id", @byte), new Entry("flash", @byte), new Entry("weather", @byte), new Entry("type", @byte),
         new Entry("_", @short), new Entry("labelToggle", @byte), new Entry("_", @byte)
      );

      #endregion

      readonly PointerMapper _mapper;
      byte[] _data;
      SortedSet<int> _mapLocations;
      SortedSet<int> _mapBankAddress;
      SortedSet<int> _allSubsetAddresses;
      int _masterMapAddress;

      public Maps(PointerMapper mapper) { _mapper = mapper; }

      string GameCode {
         get {
            var chars = Enumerable.Range(0, 4).Select(i => (char)_data[0xAC + i]).ToArray();
            return new string(chars);
         }
      }

      public void Load(IRunStorage runs) {
         _data = runs.Data;
         var code = GameCode;

         //           ruby            sapphire          emerald
         if (code == "AXVE" || code == "AXPE" || code == "BPEE") {
            DataLayout.Children[0] = new Entry("mapTileData",
               new Entry("width", @word), new Entry("height", @word),
               new Entry("borderTile", @pointer),
               new Entry("tiles", @pointer),
               new Entry("tileset", @pointer),
               new Entry("tileset", @pointer)
            );
         } else { // FR / LG
            DataLayout.Children[0] = new Entry("mapTileData",
               new Entry("width", @word), new Entry("height", @word),
               new Entry("borderTile", @pointer),
               new Entry("tiles", @pointer),
               new Entry("tileset", @pointer),
               new Entry("tileset", @pointer),
               new Entry("borderWidth", @byte), new Entry("borderHeight", @byte), new Entry("_", @byte), new Entry("_", @byte)
            );
         }

         // match the nested layout
         _allSubsetAddresses = new SortedSet<int>();
         var matchingLayouts = new List<int>();
         var matchingPointers = new List<int>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            if (!CouldBe(DataLayout, address, addressesList)) continue;
            SeekChildren(DataLayout, runs, address);
            _mapper.Claim(runs, address);
            matchingPointers.Add(_mapper.PointersFromDestination(address).First()); // can I do without the first?
            matchingLayouts.Add(address);
         }

         // third pass: find which of the pointers in matchingPointers have references to them (those are the heads of the lists)
         //_mapBankAddress = new SortedSet<int>();
         //var mapBankPointers = new List<int>();
         //foreach (var mapPointer in matchingPointers) {
         //   var bankPointer = _mapper.PointersFromDestination(mapPointer);
         //   if (bankPointer == null || bankPointer.Length == 0) continue;
         //   _mapBankAddress.Add(mapPointer);
         //   mapBankPointers.Add(bankPointer.First()); // can I do without the first?
         //}

         //_masterMapAddress = _mapper.OpenDestinations.First(mapBankPointers.Contains);
         //_mapLocations = new SortedSet<int>(matchingLayouts);
         //foreach (var location in matchingLayouts) _allSubsetAddresses.Add(location);
         //foreach (var location in _mapBankAddress) _allSubsetAddresses.Add(location);
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

      public IEnumerable<int> Find(string term) {
         if (term == "magic") return _mapLocations.ToList();
         return null;
      }

      bool CouldBe(Entry entry, int address, IList<int> addresses) {
         int currentOffset = 0;
         foreach (var child in entry.Children) {
            if (child.DataType == @pointer || child.DataType == @nullablepointer) {
               if (child.DataType == @nullablepointer && _data[address + currentOffset + 3] == 0x00) {
                  // if it's nullable and null, make sure it's all null
                  if ((_data[address + currentOffset] | _data[address + currentOffset + 1] | _data[address + currentOffset + 2]) != 0x00) return false;
               } else  {
                  // it's a pointer and it's not null (or nullable)
                  if (_data[address + currentOffset + 3] != 0x08) return false;
                  if (child.Children != null && child.Children.Length > 0) {
                     var childAddress = _data.ReadPointer(address + currentOffset);
                     if (childAddress % 4 != 0) return false;
                     if (!CouldBe(child, childAddress, addresses)) return false;
                  }
               }
            }
            if (child.DataType == @word) {
               // words never use the 4th byte - they're just not big enough
               if (_data[address + currentOffset + 3] != 0x00) return false;
            }
            currentOffset += child.DataType.Length;
         }

         return true;
      }

      void SeekChildren(Entry entry, IRunStorage runs, int address) {
         int currentOffset = 0;
         foreach (var child in entry.Children) {
            if (child.DataType == @pointer || child.DataType == @nullablepointer) {
               if (child.DataType == @nullablepointer && _data[address + currentOffset + 3] == 0x00) {
                  currentOffset+=child.DataType.Length;
                  continue;
               } else {
                  var next = _data.ReadPointer(address + currentOffset);
                  if (child.Children != null && child.Children.Length > 0) SeekChildren(child, runs, next);
                  _mapper.Claim(runs, address + currentOffset, next);
                  _allSubsetAddresses.Add(next);
               }
            } else {
               runs.AddRun(address + currentOffset, MediaRun(child.DataType.Length, child.Name));
            }
            currentOffset += child.DataType.Length;
         }
      }
   }

   /*
   class Thumbnails : IPartialParser {

      readonly byte[] _data;
      readonly IList<int> _iconStartPoints = new List<int>();
      readonly IList<int> _paletteIndex = new List<int>();


      public Thumbnails(byte[] data) {
         _data = data;
      }

      public void Load() {
         int icons = _data.ReadPointer(0x0138);
         int palettePointers = _data.ReadPointer(0x13C);
         int paletteIndex = _data.ReadPointer(0x140);
         while (_data[icons + 3] == 0x08) {
            _iconStartPoints.Add(_data.ReadPointer(icons));
            _paletteIndex.Add(_data[paletteIndex]);
            icons += 4;
            paletteIndex++;
         }
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         return new FrameworkElement[length];
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         throw new NotImplementedException();
      }

      public bool IsStartOfDataBlock(int location) { return _iconStartPoints.Contains(location); }

      public bool IsWithinDataBlock(int location) {
         return false; // TODO
      }

      public FrameworkElement GetInterpretation(int location) {
         return null; // TODO
      }

      public IList<int> Find(string term) { return null; }

   }
   //*/
}
