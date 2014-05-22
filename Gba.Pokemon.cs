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
   class PCS : IPartialParser {
      static readonly Geometry Escape = "\\x".ToGeometry();

      readonly byte[] _data;
      readonly IDictionary<byte, string> _pcs = new Dictionary<byte, string>();
      readonly IList<int> _startPoints = new List<int>();
      readonly IList<int> _lengths = new List<int>();
      readonly Queue<Path> _recycles = new Queue<Path>();
      readonly IDictionary<int, FrameworkElement> _interpretations = new Dictionary<int, FrameworkElement>();

      public PCS(byte[] data) { _data = data; }

      public void Load() {
         foreach (var line in System.IO.File.ReadAllLines("PCS3-W.ini")) {
            var sanitized = line.Trim();
            if (sanitized.StartsWith("#") || sanitized.Length == 0) continue;
            Debug.Assert(sanitized.StartsWith("0x"));
            var key = (byte)sanitized.Substring(2, 2).ParseAsHex();
            var value = sanitized.Substring(sanitized.IndexOf("'") + 1);
            value = value.Substring(0, value.Length - 1);
            _pcs[key] = value;
         }
         FindStrings();
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var startIndex = Utils.SearchForStartPoint(start, _startPoints, i => i, Utils.FindOptions.StartOrBefore);
         var list = new List<FrameworkElement>();

         for (int i = 0; i < length; ) {
            Debug.Assert(i >= 0);
            int loc = start + i;
            int dataIndex = _startPoints[startIndex];
            if (startIndex >= _startPoints.Count) {
               list.AddRange(new FrameworkElement[length - i]);
               i = length;
            } else if (dataIndex > loc) {
               var sectionLength = Math.Min(length - i, dataIndex - loc);
               list.AddRange(new FrameworkElement[sectionLength]);
               i += sectionLength;
            } else if (dataIndex + _lengths[startIndex] < loc) {
               startIndex++;
            } else {
               int stringEnd = dataIndex + _lengths[startIndex];
               stringEnd = Math.Min(stringEnd, start + length);
               int lengthInView = stringEnd - loc;
               
               for (int j = 0; j < lengthInView; j++) {
                  var element = CreatePath(_data[loc + j]);
                  list.Add(element);
               }

               startIndex++;
               i += lengthInView;
            }
         }

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) { _recycles.Enqueue((Path)element); }

      // not enough confidence in strings to claim /*_startPoints.Contains(location) */
      public bool IsStartOfDataBlock(int location) { return  false; }
      public bool IsWithinDataBlock(int location) { return false; }

      public FrameworkElement GetInterpretation(int location) {
         int index = _startPoints.IndexOf(location);
         if (index == -1) return null;
         if (_interpretations.ContainsKey(location)) return _interpretations[location];

         string result = string.Empty;
         for (int j = 0; j < _lengths[index]; j++) {
            if (_data[_startPoints[index] + j] == 0x00) {
               result += " ";
            } else if (_data[_startPoints[index] + j] == 0xFD) {
               result += "\\x" + Utils.ToHexString(_data[_startPoints[index] + j + 1]);
               j++;
            } else {
               result += _pcs[_data[_startPoints[index] + j]];
            }
         }
         _interpretations[location] = new TextBlock { Text = result, Foreground = Solarized.Theme.Instance.Primary, TextWrapping = TextWrapping.Wrap };

         return _interpretations[location];
      }

      public IList<int> Find(string term) {
         if (!term.Select(c => new string(c, 1)).All(s => _pcs.Values.Contains(s) || s == " ")) return null;

         var lower = term.ToLower();
         var upper = term.ToUpper();

         // two searchTerms, one with caps and one with lowercase
         var list = new List<int>();
         byte[] searchTerm1 =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : _pcs.Keys.First(key => _pcs[key] == lower.Substring(i, 1)))
            .ToArray();

         byte[] searchTerm2 =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : _pcs.Keys.First(key => _pcs[key] == upper.Substring(i, 1)))
            .ToArray();

         for (int i = 0, j = 0; i < _data.Length; i++) {
            j = _data[i] == searchTerm1[j] || _data[i] == searchTerm2[j] ? j + 1 : 0;
            if (j < searchTerm1.Length) continue;
            list.Add(i - j + 1);
            j = 0;
         }
         return list;
      }

      void FindStrings() {
         _startPoints.Clear();
         _lengths.Clear();

         int currentLength = 0;
         for (int i = 0x200; i < _data.Length; i++) {
            if (_pcs.ContainsKey(_data[i])) {
               currentLength++;
               continue;
            }
            if (_data[i] == 0x00 && currentLength > 0) { // accept 0x00 if we've already started
               currentLength++;
               continue;
            } else if (_data[i] == 0xFD) { // accept 0xFD as the escape character
               i++;
               currentLength += 2;
               continue;
            } else if (_data[i] == 0xFF && currentLength >= 3) {
               _startPoints.Add(i - currentLength);
               _lengths.Add(currentLength);
            }
            currentLength = 0;
         }
      }

      Path CreatePath(byte value) {
         bool translate = _pcs.ContainsKey(value);
         var geometry = translate ? _pcs[value].ToGeometry() :
            value == 0x00 ? null :
            value == 0xFD ? Escape :
            Utils.ByteFlyweights[value];

         if (_recycles.Count > 0) {
            var element = _recycles.Dequeue();
            element.Data = geometry;
            element.Fill = translate ? Solarized.Brushes.Violet : Solarized.Theme.Instance.Secondary;
            return element;
         }

         return new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = translate ? Solarized.Brushes.Violet : Solarized.Theme.Instance.Secondary,
            Data = geometry,
            ClipToBounds = false,
            Margin = new Thickness(4, 2, 4, 2),
            Tag = this
         };
      }
   }

   class Maps : IPartialParser {

      #region Setup

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
               new Entry("?", @unknown4),
               new Entry("dynamic", new DataType(4)) // *script || -itemID .hiddenID .amount //*/
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

      readonly byte[] _data;
      SortedSet<int> _mapLocations;
      SortedSet<int> _mapBankAddress;
      SortedSet<int> _allSubsetAddresses;
      int _masterMapAddress;

      public Maps(byte[] data) { _data = data; }

      public string GameCode {
         get {
            var chars = Enumerable.Range(0, 4).Select(i => (char)_data[0xAC + i]).ToArray();
            return new string(chars);
         }
      }

      public void Load() {
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

         // first pass for addresses.
         var pointerSet = new SortedList<int, int>();
         var addressesSet = new HashSet<int>();
         for (int i = 3; i < _data.Length; i += 4) {
            if (_data[i] != 0x08) continue;
            var address = _data.ReadPointer(i - 3);
            if (address % 4 != 0) continue;
            if (addressesSet.Contains(address)) continue;
            pointerSet.Add(address, i - 3);
            addressesSet.Add(address);
         }
         var addressesList = addressesSet.ToList();
         addressesList.Sort();

         // second pass for matching the nested layout
         _allSubsetAddresses = new SortedSet<int>();
         var matchingLayouts = new List<int>();
         var matchingPointers = new List<int>();
         foreach (var address in addressesList) {
            if (!CouldBe(DataLayout, address, addressesList)) continue;
            matchingPointers.Add(pointerSet[address]);
            matchingLayouts.Add(address);
            SeekChildren(DataLayout, address);
         }

         // third pass: find which of the pointers in matchingPointers have references to them (those are the heads of the lists)
         _mapBankAddress = new SortedSet<int>();
         var mapBankPointers = new List<int>();
         foreach (var mapPointer in matchingPointers) {
            if (!pointerSet.ContainsKey(mapPointer)) continue;
            _mapBankAddress.Add(mapPointer);
            mapBankPointers.Add(pointerSet[mapPointer]);
         }

         _masterMapAddress = pointerSet.Keys.First(mapBankPointers.Contains);
         _mapLocations = new SortedSet<int>(matchingLayouts);
         foreach (var location in matchingLayouts) _allSubsetAddresses.Add(location);
         foreach (var location in _mapBankAddress) _allSubsetAddresses.Add(location);
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new FrameworkElement[length];
         // TODO
         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         // TODO
      }

      public bool IsStartOfDataBlock(int location) { return _allSubsetAddresses.Contains(location); }
      public bool IsWithinDataBlock(int location) { return false; }

      Dictionary<int, FrameworkElement> _interpretations = new Dictionary<int,FrameworkElement>();
      public FrameworkElement GetInterpretation(int location) {
         if (_mapBankAddress.Contains(location)) {
            if (!_interpretations.ContainsKey(location)) _interpretations[location] = new TextBlock { Text = "MapBank", Foreground = Solarized.Theme.Instance.Emphasis };
            return _interpretations[location];
         }

         if (!_mapLocations.Contains(location)) return null;
         if (!_interpretations.ContainsKey(location)) _interpretations[location] = new TextBlock { Text = "Map", Foreground = Solarized.Theme.Instance.Emphasis };
         return _interpretations[location];
      }
      public IList<int> Find(string term) {
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

      void SeekChildren(Entry entry, int address) {
         int currentOffset = 0;
         foreach (var child in entry.Children) {
            if (child.DataType == @pointer || child.DataType == @nullablepointer) {
               if (child.DataType == @nullablepointer && _data[address + currentOffset + 3] == 0x00) {
                  currentOffset+=child.DataType.Length;
                  continue;
               } else {
                  var next = _data.ReadPointer(address + currentOffset);
                  if (child.Children != null && child.Children.Length > 0) SeekChildren(child, next);
                  _allSubsetAddresses.Add(next);
               }
            }
            currentOffset += child.DataType.Length;
         }
      }
   }

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
}
