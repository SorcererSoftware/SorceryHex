using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IParser {
      int Length { get; }
      void Load();
      IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsStartOfDataBlock(int location);
      bool IsWithinDataBlock(int location);
      FrameworkElement GetInterpretation(int location);
      IList<int> Find(string term);
   }

   public interface IPartialParser {
      void Load();
      IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsStartOfDataBlock(int location);
      bool IsWithinDataBlock(int location);
      FrameworkElement GetInterpretation(int location);
      IList<int> Find(string term);
   }

   class CompositeParser : IParser {
      readonly IList<IPartialParser> _children;
      readonly byte[] _data;
      readonly Queue<Path> _recycles = new Queue<Path>();
      bool _loaded;

      public int Length { get { return _data.Length; } }

      public CompositeParser(byte[] data, params IPartialParser[] children) {
         _data = data;
         _children = children;
      }

      public void Load() {
         _loaded = false;
         foreach (var child in _children) child.Load();
         _loaded = true;
      }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         Debug.Assert(length < 0x20 * 0x40);
         var list = new List<FrameworkElement>();

         int pre = 0, post = 0;
         if (start < 0) { pre = -start; start = 0; length -= pre; }
         if (length < 0) { pre += length; length = 0; }
         if (start + length >= Length) { post = start + length - Length; length = Length - start; }

         if (pre > 0) list.AddRange(Enumerable.Range(0, pre).Select(i => UseElement(null)));
         if (length > 0) list.AddRange(ChildCheck(commander, start, length));
         if (post > 0) list.AddRange(Enumerable.Range(0, post).Select(i => UseElement(null)));

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag == this) {
            _recycles.Enqueue((Path)element);
         } else {
            var child = (IPartialParser)element.Tag;
            child.Recycle(commander, element);
         }
      }

      public bool IsStartOfDataBlock(int location) {
         if (!_loaded) return false;
         return _children.Any(child => child.IsStartOfDataBlock(location));
      }

      public bool IsWithinDataBlock(int location) {
         if (!_loaded) return false;
         return _children.Any(child => child.IsWithinDataBlock(location));
      }

      public FrameworkElement GetInterpretation(int location) {
         if (!_loaded) return null;
         return _children.Select(child => child.GetInterpretation(location)).Where(interpretation => interpretation != null).FirstOrDefault();
      }

      public IList<int> Find(string term) {
         if (!_loaded) return new int[0];
         var list = _children
            .Select(child => child.Find(term) ?? new int[0])
            .Select(set => (IEnumerable<int>)set)
            .Aggregate(Enumerable.Concat)
            .ToList();

         var sanitized = term.ToUpper().Replace(" ", "");
         if (sanitized.Length % 2 != 0 || !sanitized.All(Utils.Hex.Contains)) return list;
         byte[] searchTerm =
            Enumerable.Range(0, sanitized.Length / 2)
            .Select(i => (byte)sanitized.Substring(i * 2, 2).ParseAsHex())
            .ToArray();

         for (int i = 0, j = 0; i < _data.Length; i++) {
            j = _data[i] == searchTerm[j] ? j + 1 : 0;
            if (j < searchTerm.Length) continue;
            list.Add(i - j + 1);
            j = 0;
         }

         return list;
      }

      IEnumerable<FrameworkElement> ChildCheck(ICommandFactory commander, int start, int length) {
         if (!_loaded) return CreateRawElements(commander, start, length);

         IList<IList<FrameworkElement>> responses = new List<IList<FrameworkElement>>();
         foreach (var child in _children) {
            var elements = child.CreateElements(commander, start, length);
            Debug.Assert(elements.Count == length);
            responses.Add(elements);
         }

         var list = new List<FrameworkElement>();
         for (int i = 0; i < length; i++) {
            FrameworkElement element = null;

            for (int j = 0; j < _children.Count; j++) {
               var response = responses[j];
               var child = _children[j];
               if (response[i] != null) response[i].Tag = child;
               if (element != null) {
                  if (response[i] != null) child.Recycle(commander, response[i]);
               } else {
                  element = response[i];
               }
            }

            int k = start + i;
            if (element == null) element = UseElement(Utils.ByteFlyweights[_data[k]], IsLightweight(_data[k]));
            list.Add(element);
         }
         return list;
      }

      IEnumerable<FrameworkElement> CreateRawElements(ICommandFactory commander, int start, int length) {
         return Enumerable.Range(start, length).Select(i => UseElement(Utils.ByteFlyweights[_data[i]], IsLightweight(_data[i])));
      }

      FrameworkElement UseElement(Geometry data, bool lightweight = false) {
         Path element;
         if (_recycles.Count > 0) element = _recycles.Dequeue();
         else element = new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
            Tag = this
         };

         element.Data = data;
         element.Fill = lightweight ? Solarized.Theme.Instance.Secondary : Solarized.Theme.Instance.Primary;
         element.Opacity = lightweight ? .75 : 1;
         return element;
      }

      bool IsLightweight(byte value) { return value == 0x00 || value == 0xFF; }
   }


   class RunStorage : IPartialParser {
      readonly byte[] _data;
      readonly SortedList<int, DataRun> _runs = new SortedList<int, DataRun>();
      readonly Queue<Path> _recycles = new Queue<Path>();

      List<int> _keys = new List<int>();
      bool _listNeedsUpdate;

      public RunStorage(byte[] data) { _data = data; }

      public void Load() { }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         UpdateList();
         var elements = new FrameworkElement[length];
         var startIndex = _keys.BinarySearch(start);

         if (startIndex == _keys.Count) return elements;

         for (int i = 0; i < length; ) {
            int loc = start + i;
            if (startIndex >= _keys.Count) {
               i = length;
               continue;
            }

            int dataIndex = _keys[startIndex];
            if (dataIndex > loc) {
               var sectionLength = Math.Min(length - i, dataIndex - loc);
               i += sectionLength;
            } else if (dataIndex + _runs[_keys[startIndex]].Length < loc) {
               startIndex++;
            } else {
               int runEnd = dataIndex + _runs[_keys[startIndex]].Length;
               runEnd = Math.Min(runEnd, start + length);
               int lengthInView = runEnd - loc;
               // InterpretData(dataIndex);
               for (int j = 0; j < lengthInView; j++) {
                  var element = UsePath(_runs[_keys[startIndex]], loc + j);
                  // commander.LinkToInterpretation(element, _interpretations[dataIndex]);
                  elements[i + j] = element;
               }
               startIndex++;
               i += lengthInView;
            }
         }

         return elements;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         _recycles.Enqueue((Path)element);
      }

      public bool IsStartOfDataBlock(int location) {
         return _runs.ContainsKey(location);
      }

      public bool IsWithinDataBlock(int location) {
         UpdateList();
         var insertionPoint = _keys.BinarySearch(location);
         if (insertionPoint < 1) return false;
         int startAddress = _keys[insertionPoint - 1];
         return startAddress + _runs[startAddress].Length > location;
      }

      public FrameworkElement GetInterpretation(int location) { return null; }

      public IList<int> Find(string term) { return null; }

      Path UsePath(DataRun run, int location) {
         var geometry = run.Parser[_data[location]];

         var element = _recycles.Count > 0 ? _recycles.Dequeue() : new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
         };
         element.Data = geometry;
         element.Fill = run.Color;
         return element;
      }

      void UpdateList() {
         if (!_listNeedsUpdate) return;
         _listNeedsUpdate = false;
         _keys = _runs.Keys.ToList();
      }
   }

   class DataRun {
      public readonly int Length;
      public readonly Brush Color;
      public readonly IDictionary<byte, Geometry> Parser;
      public DataRun(int length, Brush color, IDictionary<byte, Geometry> parser) {
         Length = length; Color = color; Parser = parser;
      }
   }
}
