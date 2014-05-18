using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PCS : IElementFactory {
      readonly byte[] _data;
      readonly IElementFactory _next;
      readonly IDictionary<byte, string> _pcs = new Dictionary<byte, string>();
      readonly IList<int> _startPoints = new List<int>();
      readonly IList<int> _lengths = new List<int>();
      readonly Queue<Path> _recycles = new Queue<Path>();

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
            if (_data[i] == 0x00 && currentLength > 1) { // accept 0x00 if we've already started
               currentLength++;
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
         if (_startPoints.Contains(location)) {
            string result = string.Empty;
            while (_data[location] != 0xFF) {
               if (_data[location] == 0x00) result += " ";
               else result += _pcs[_data[location]];
               location++;
            }
            return new TextBlock { Text = result, Foreground = Solarized.Theme.Instance.Primary, TextWrapping = TextWrapping.Wrap };
         }
         return _next.GetInterpretation(location);
      }

      public IList<int> Find(string term) {
         // TODO
         return _next.Find(term);
      }

      Path CreatePath(byte value) {
         var geometry = value == 0x00 ? null : _pcs[value].ToGeometry();

         if (_recycles.Count > 0) {
            var element = _recycles.Dequeue();
            element.Data = geometry;
            return element;
         }

         return new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Solarized.Brushes.Violet,
            Data = geometry,
            ClipToBounds = false,
            Margin = new Thickness(4, 3, 4, 3),
            Tag = this
         };
      }
   }
}
