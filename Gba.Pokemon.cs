using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PCS : IElementFactory {
      readonly byte[] _data;
      readonly IElementFactory _next;
      readonly IDictionary<byte, string> _pcs = new Dictionary<byte, string>();
      readonly IList<int> _startPoints = new List<int>();
      readonly IList<int> _lengths = new List<int>();
      readonly Queue<Path> _recycles = new Queue<Path>();
      readonly IDictionary<int, FrameworkElement> _interpretations = new Dictionary<int, FrameworkElement>();

      public int Length { get { return _data.Length; } }

      public PCS(IElementFactory next, byte[] data) {
         _data = data;
         _next = next;
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

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var startIndex = Utils.SearchForStartPoint(start, _startPoints, i => i, Utils.FindOptions.StartOrBefore);
         var list = new List<FrameworkElement>();

         for (int i = 0; i < length; ) {
            Debug.Assert(i >= 0);
            int loc = start + i;
            int dataIndex = _startPoints[startIndex];
            if (startIndex >= _startPoints.Count) {
               list.AddRange(_next.CreateElements(commander, loc, length - i));
               i = length;
            } else if (dataIndex > loc) {
               var sectionLength = Math.Min(length - i, dataIndex - loc);
               list.AddRange(_next.CreateElements(commander, loc, sectionLength));
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

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag == this) _recycles.Enqueue((Path)element);
         else _next.Recycle(commander, element);
      }

      public bool IsStartOfDataBlock(int location) {
         // not enough confidence in strings to claim
         return /*_startPoints.Contains(location) ||*/ _next.IsStartOfDataBlock(location);
      }

      public bool IsWithinDataBlock(int location) {
         // not enough confidence in strings to claim
         return _next.IsWithinDataBlock(location);
      }

      public FrameworkElement GetInterpretation(int location) {
         int index = _startPoints.IndexOf(location);
         if (index == -1) return _next.GetInterpretation(location);
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

         if (!term.Select(c => new string(c, 1)).All(s => _pcs.Values.Contains(s) || s == " ")) return _next.Find(term);

         var list = new List<int>();
         byte[] searchTerm =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : _pcs.Keys.First(key => _pcs[key] == term.Substring(i, 1)))
            .ToArray();

         for (int i = 0, j = 0; i < _data.Length; i++) {
            j = _data[i] == searchTerm[j] ? j + 1 : 0;
            if (j < searchTerm.Length) continue;
            list.Add(i - j + 1);
            j = 0;
         }
         list.AddRange(_next.Find(term));
         return list;
      }

      static readonly Geometry Escape = "\\x".ToGeometry();

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
}
