using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PCS : IPartialElementFactory {
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

   class Maps : IPartialElementFactory {

      #region Setup

      class DataType { public readonly int Length; public DataType(int length) { Length = length; } }

      static readonly DataType @byte = new DataType(1);
      static readonly DataType @short = new DataType(2);
      static readonly DataType @word = new DataType(4);
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

      #endregion

      static readonly Entry DataLayout = new Entry("map",
         new Entry("mapTileData",
            new Entry("width", @word), new Entry("height", @word),
            new Entry("borderTile", @pointer),
            new Entry("tiles", @pointer),
            new Entry("tileset", @pointer),
            new Entry("tileset", @pointer),
            new Entry("borderWidth", @byte), new Entry("borderHeight", @byte), new Entry("_", @byte), new Entry("_", @byte)
         ),
         new Entry("mapEventData",
            new Entry("personCount", @byte), new Entry("warpCount", @byte), new Entry("scriptCount", @byte), new Entry("signpostCount", @byte),
            new Entry("persons", @nullablepointer), // TODO expand
            new Entry("warps", @nullablepointer),   // TODO expand
            new Entry("scripts", @nullablepointer), // TODO expand
            new Entry("signposts", @nullablepointer)// TODO expand
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

      /*
         map:
         *mapTileData
            width height
            *borderTile // 8 bytes, no pointers
            *tile[width*height] // 2 bytes: logical for 6 bits, visual for 10
            *tileset
            *tileset
            .borderWidth .borderHeight ._ ._
         *mapEventData
            .personCount .warpCount .scriptCount .signpostCount
            *persons[personCount]
               .? .picture .? .?
               -x -y
               .? .movementType .movement .?
               .isTrainer .? -viewRadius
               *script
               -id .? .?
            *warps[warpCount] { -x -y .? .warp .map .bank }
            *scripts[scriptCount] { -x -y -? -scriptVaribale -scriptVariableValue -? *script }
            *signposts[signpostCount]
               -x -y
               .talkingLevel .signpostType -?
               ?
               *script // || -itemID .hiddenID .amount
         *script
         *connections
            count
            *data[count] { type offset .bank .map -_ }
         -song -map
         .label_id .flash .weather .type
         -_ .labelToggle ._
       */

      readonly byte[] _data;

      public Maps(byte[] data) { _data = data; }

      public void Load() {
         // TODO find maps based on the nested DataLayout
         
         // first pass for pointers and addresses
         var addresses = new List<int>();
         for (int i = 3; i < _data.Length; i += 4) {
            if (_data[i] != 0x08) continue;
            var address = _data.ReadPointer(i - 3);
            if (address % 4 != 0) continue;
            if (addresses.Contains(address)) continue;
            addresses.Add(address);
         }
         addresses.Sort();

         // second pass for matching the nested layout
         var matchingLayouts = new List<int>();
         for (int i = 0; i < addresses.Count; i++) {
            if (!CouldBe(DataLayout, addresses[i], addresses)) continue;
            matchingLayouts.Add(addresses[i]);
         }
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new FrameworkElement[length];
         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         // TODO
      }

      public bool IsStartOfDataBlock(int location) { return false; }
      public bool IsWithinDataBlock(int location) { return false; }
      public FrameworkElement GetInterpretation(int location) { return null; }
      public IList<int> Find(string term) { return null; }

      bool CouldBe(Entry entry, int address, IList<int> addresses) {
         int length = entry.Children.Sum(child => child.DataType.Length);
         int index = addresses.IndexOf(address);
         var nextAddress = index < addresses.Count - 1 ? addresses[index + 1] : 0x1000000;
         int availableLength = nextAddress - address;
         if (availableLength < length) return false;

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

   }

   class Thumbnails : IPartialElementFactory {

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
