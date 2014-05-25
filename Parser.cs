using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
         if (length < 0) { post += length; length = 0; }

         if (pre > 0) list.AddRange(Enumerable.Range(0, pre).Select(i => UseElement(null)));
         if (length > 0) list.AddRange(ChildCheck(commander, start, length));
         if (post > 0) list.AddRange(Enumerable.Range(0, post).Select(i => UseElement(null)));

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.GetCreator() == this) {
            _recycles.Enqueue((Path)element);
         } else {
            var child = (IPartialParser)element.GetCreator();
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
               if (response[i] != null) response[i].SetCreator(child);
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
         };

         element.SetCreator(this);
         element.Data = data;
         element.Fill = lightweight ? Solarized.Theme.Instance.Secondary : Solarized.Theme.Instance.Primary;
         element.Opacity = lightweight ? .75 : 1;
         return element;
      }

      bool IsLightweight(byte value) { return value == 0x00 || value == 0xFF; }
   }

   public delegate int[] JumpRule(byte[] data, int index);
   public delegate FrameworkElement InterpretationRule(byte[] data, int index);
   
   public interface IRunParser {
      void Load(RunStorage runs);
      IEnumerable<int> Find(string term);
   }

   public class RunStorage : IPartialParser {
      public readonly byte[] Data;
      readonly SortedList<int, IDataRun> _runs = new SortedList<int, IDataRun>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly IDictionary<int, FrameworkElement> _interpretations = new Dictionary<int, FrameworkElement>();
      readonly HashSet<FrameworkElement> _interpretationLinks = new HashSet<FrameworkElement>();
      readonly HashSet<FrameworkElement> _jumpLinks = new HashSet<FrameworkElement>();
      readonly IRunParser[] _runParsers;

      List<int> _keys = new List<int>();
      bool _listNeedsUpdate;

      public RunStorage(byte[] data, params IRunParser[] runParsers) {
         Data = data;
         _runParsers = runParsers;
      }

      public void AddRun(int location, IDataRun run) {
         _runs.Add(location, run);
         _listNeedsUpdate = true;
      }

      public void Load() {
         foreach (var parser in _runParsers) parser.Load(this);
         _keys = _runs.Keys.ToList();
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         UpdateList();
         var elements = new FrameworkElement[length];
         var startIndex = _keys.BinarySearch(start);
         if (startIndex < 0) startIndex = ~startIndex - 1; // not in list: give me the one that starts just before here

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
            } else if (dataIndex + _runs[_keys[startIndex]].GetLength(Data, dataIndex) < loc) {
               startIndex++;
            } else {
               var currentRun = _runs[_keys[startIndex]];
               int runEnd = dataIndex + currentRun.GetLength(Data, dataIndex);
               runEnd = Math.Min(runEnd, start + length);
               int lengthInView = runEnd - loc;
               InterpretData(currentRun, dataIndex);
               for (int j = 0; j < lengthInView; j++) {
                  if (currentRun.Parser[Data[loc + j]] == null) continue; // use the parents parser for this byte

                  var element = UseTemplate(currentRun, dataIndex, loc + j);
                  if (_interpretations.ContainsKey(dataIndex)) {
                     _interpretationLinks.Add(element);
                     commander.LinkToInterpretation(element, _interpretations[dataIndex]);
                  }
                  if (currentRun.Jump != null) {
                     _jumpLinks.Add(element);
                     commander.CreateJumpCommand(element, currentRun.Jump(Data, dataIndex));
                  }
                  elements[i + j] = element;
               }
               startIndex++;
               i += lengthInView;
            }
         }

         return elements;
      }
      
      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (_interpretationLinks.Contains(element)) {
            _interpretationLinks.Remove(element);
            commander.UnlinkFromInterpretation(element);
         }

         if (_jumpLinks.Contains(element)) {
            _jumpLinks.Remove(element);
            commander.RemoveJumpCommand(element);
         }

         _recycles.Enqueue((Border)element);
      }

      public bool IsStartOfDataBlock(int location) {
         return _runs.ContainsKey(location);
      }

      public bool IsWithinDataBlock(int location) {
         UpdateList();
         var insertionPoint = _keys.BinarySearch(location);
         if (insertionPoint < 1) return false;
         int startAddress = _keys[insertionPoint - 1];
         return startAddress + _runs[startAddress].GetLength(Data, startAddress) > location;
      }

      public FrameworkElement GetInterpretation(int location) {
         var index = _keys.BinarySearch(location);
         if (index < 0) return null;
         var run = _runs[_keys[index]];
         InterpretData(run, location);
         if (!_interpretations.ContainsKey(location)) return null;
         return _interpretations[location];
      }

      public IList<int> Find(string term) {
         return _runParsers.Select(parser => parser.Find(term) ?? new int[0]).Aggregate(Enumerable.Concat).ToList();
      }

      void InterpretData(IDataRun run, int dataIndex) {
         if (run.Interpret == null) return;
         if (!_interpretations.ContainsKey(dataIndex)) {
            var interpretation = run.Interpret(Data, dataIndex);
            if (interpretation == null) return;
            _interpretations[dataIndex] = interpretation;
         }
      }

      FrameworkElement UseTemplate(IDataRun run, int dataIndex, int location) {
         var geometry = run.Parser[Data[location]];

         var element = _recycles.Count > 0 ? _recycles.Dequeue() : new Border {
            Child = new Path {
               HorizontalAlignment = HorizontalAlignment.Center,
               VerticalAlignment = VerticalAlignment.Center,
               Margin = new Thickness(3, 3, 3, 1),
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1)
         };
         element.SetCreator(this);
         ((Path)element.Child).Data = geometry;
         ((Path)element.Child).Fill = run.Color;
         element.BorderBrush = run.Color;

         double leftBorder = dataIndex == location ? 2 : 0;
         double rightBorder = dataIndex + run.GetLength(Data, dataIndex) - 1 == location ? 2 : 0;
         element.Margin = new Thickness(leftBorder, 0, rightBorder, 1);
         double bottom = run.Underlined ? 1 : 0;
         element.BorderThickness = new Thickness(0, 0, 0, bottom);
         element.ToolTip = run.HoverText;

         return element;
      }

      void UpdateList() {
         if (!_listNeedsUpdate) return;
         _listNeedsUpdate = false;
         _keys = _runs.Keys.ToList();
      }
   }

   public interface IDataRun {
      Brush Color { get; }
      Geometry[] Parser { get; }

      string HoverText { get; }
      bool Underlined { get; }
      InterpretationRule Interpret { get; }
      JumpRule Jump { get; }

      int GetLength(byte[] data, int startPoint);
   }

   public class SimpleDataRun : IDataRun {
      readonly int _length;

      public Brush Color { get; set; }
      public Geometry[] Parser { get; set; }

      public string HoverText { get; set; }
      public bool Underlined { get; set; }
      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }

      public SimpleDataRun(int length, Brush color, Geometry[] parser) {
         _length = length; Color = color; Parser = parser;
      }

      public int GetLength(byte[] data, int startPoint) { return _length; }
   }

   public class VariableLengthDataRun : IDataRun {
      readonly byte _endCharacter;
      readonly int _stride;

      public Brush Color { get; set; }
      public Geometry[] Parser { get; set; }

      public string HoverText { get; set; }
      public bool Underlined { get; set; }
      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }

      public VariableLengthDataRun(byte endCharacter, int stride, Brush color, Geometry[] parser) {
         _endCharacter = endCharacter; _stride = stride;
         Color = color; Parser = parser;
      }

      public int GetLength(byte[] data, int startPoint) {
         int len = 0;
         while (data[startPoint + len] != _endCharacter) len += _stride;
         return len;
      }
   }
}
