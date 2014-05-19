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
