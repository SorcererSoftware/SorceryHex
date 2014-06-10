﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IRunParser {
      void Load(IRunStorage runs);
      IEnumerable<int> Find(string term);
   }

   public interface IRunStorage {
      byte[] Data { get; }
      void AddRun(int location, IDataRun run);
      bool IsFree(int location);
   }

   public class RunStorage : IPartialModel, IRunStorage, IEditor {
      readonly SortedList<int, IDataRun> _runs = new SortedList<int, IDataRun>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly IDictionary<int, FrameworkElement> _interpretations = new Dictionary<int, FrameworkElement>();
      readonly HashSet<FrameworkElement> _interpretationLinks = new HashSet<FrameworkElement>();
      readonly HashSet<FrameworkElement> _jumpLinks = new HashSet<FrameworkElement>();
      readonly HashSet<IDataRun> _runSet = new HashSet<IDataRun>();
      readonly IRunParser[] _runParsers;

      List<int> _keys = new List<int>();
      bool _listNeedsUpdate;

      #region Run Storage

      public byte[] Data { get; private set; }

      public RunStorage(byte[] data, params IRunParser[] runParsers) {
         Data = data;
         _runParsers = runParsers;
      }

      public void AddRun(int location, IDataRun run) {
         if (_runs.ContainsKey(location)) {
            Debug.Assert(RunsAreEquivalent(_runs[location], run, location));
         } else {
            Debug.Assert(IsFree(location));
            if (!_runSet.Contains(run) && run.Editor != null) {
               run.Editor.MoveToNext += ChainMoveNext;
               _runSet.Add(run);
            }
            lock (_runs) _runs.Add(location, run);
         }
         _listNeedsUpdate = true;
      }

      public bool IsFree(int location) {
         if (_runs.ContainsKey(location)) return false;
         int keyIndex = ~_keys.BinarySearch(location) - 1;
         if (keyIndex < 0) return true;
         int prev = _keys[keyIndex];
         int prevEnd = prev + _runs[prev].GetLength(Data, prev);
         return prevEnd <= location;
      }

      #endregion

      #region Partial Parser

      public bool CanEdit(int location) {
         int index = _keys.BinarySearch(location);
         if (index < 0) index = Math.Max(~index - 1, 0);
         int startPoint = _keys[index];
         return startPoint + _runs[startPoint].GetLength(Data, startPoint) > location && startPoint <= location && _runs[startPoint].Editor != null;
      }

      public IEditor Editor { get { return this; } }

      public void Load() {
         foreach (var parser in _runParsers) {
            parser.Load(this);
            UpdateList();
         }

#if DEBUG
         int prevEnd = 0;
         foreach (var key in _runs.Keys) {
            var run = _runs[key];
            Debug.Assert(key >= prevEnd);
            prevEnd = key + _runs[key].GetLength(Data, key);
         }
#endif
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
                  var element = currentRun.Provider.ProvideElement(Data, dataIndex, j, runEnd - dataIndex + 1);
                  if (element == null) continue;
                  element.Tag = currentRun.Provider;

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
         ((IElementProvider)element.Tag).Recycle(element);
      }

      public bool IsStartOfDataBlock(int location) {
         lock (_runs) return _runs.ContainsKey(location);
      }

      public bool IsWithinDataBlock(int location) {
         UpdateList();
         var insertionPoint = _keys.BinarySearch(location);
         if (insertionPoint >= 0) return false;
         insertionPoint = ~insertionPoint - 1;
         int startAddress = _keys[insertionPoint];
         return startAddress + _runs[startAddress].GetLength(Data, startAddress) > location;
      }

      public int GetDataBlockStart(int location) {
         var insertionPoint = _keys.BinarySearch(location);
         if (insertionPoint >= 0) return _keys[insertionPoint];
         if (insertionPoint < 0) insertionPoint = ~insertionPoint;
         return _keys[insertionPoint - 1];
      }

      public int GetDataBlockLength(int location) {
         int startAddress = GetDataBlockStart(location);
         return _runs[startAddress].GetLength(Data, startAddress);
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

      #endregion

      #region Editor

      public void Edit(int location, char c) {
         int index = _keys.BinarySearch(location);
         if (index < 0) index = Math.Max(~index - 1, 0);
         int startPoint = _keys[index];
         Debug.Assert(startPoint + _runs[startPoint].GetLength(Data, startPoint) > location && startPoint <= location && _runs[startPoint].Editor != null);
         _runs[startPoint].Editor.Edit(location, c);
      }

      public void CompleteEdit(int location) {
         int index = _keys.BinarySearch(location);
         if (index < 0) index = Math.Max(~index - 1, 0);
         int startPoint = _keys[index];
         Debug.Assert(startPoint + _runs[startPoint].GetLength(Data, startPoint) > location && startPoint <= location && _runs[startPoint].Editor != null);
         _runs[startPoint].Editor.CompleteEdit(location);
      }

      public event EventHandler MoveToNext;

      void ChainMoveNext(object sender, EventArgs e) { MoveToNext(sender, e); }

      #endregion

      #region Helpers

      bool RunsAreEquivalent(IDataRun run1, IDataRun run2, int location) {
         var conditions = new[] {
            run1.Provider.IsEquivalent(run2.Provider),
            run1.Interpret == run2.Interpret,
            run1.Jump == run2.Jump,
            run1.GetLength(Data, location) == run2.GetLength(Data, location)
         };
         return conditions.All(b => b);
      }

      void InterpretData(IDataRun run, int dataIndex) {
         if (run.Interpret == null) return;
         if (!_interpretations.ContainsKey(dataIndex)) {
            var interpretation = run.Interpret(Data, dataIndex);
            if (interpretation == null) return;
            _interpretations[dataIndex] = interpretation;
         }
      }

      void UpdateList() {
         if (!_listNeedsUpdate) return;
         lock (_runs) _keys = _runs.Keys.ToList();
         _listNeedsUpdate = false;
      }

      #endregion
   }

   public delegate int[] JumpRule(byte[] data, int index);
   public delegate FrameworkElement InterpretationRule(byte[] data, int index);

   public interface IElementProvider {
      FrameworkElement ProvideElement(byte[] data, int runStart, int innerIndex, int runLength);
      bool IsEquivalent(IElementProvider other);
      void Recycle(FrameworkElement element);
   }

   public class GeometryElementProvider : IElementProvider {
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly Geometry[] _parser;
      readonly Brush _runColor;
      readonly bool _underline;
      readonly string _hoverText;

      public GeometryElementProvider(Geometry[] parser, Brush runColor, bool underline = false, string hoverText = null) {
         _parser = parser;
         _runColor = runColor;
         _underline = underline;
         _hoverText = hoverText;
      }

      public FrameworkElement ProvideElement(byte[] data, int runStart, int innerIndex, int runLength) {
         var geometry = _parser[data[runStart + innerIndex]];
         if (geometry == null) return null;

         var element = _recycles.Count > 0 ? _recycles.Dequeue() : new Border {
            Child = new Path {
               HorizontalAlignment = HorizontalAlignment.Center,
               VerticalAlignment = VerticalAlignment.Center,
               Margin = new Thickness(3, 3, 3, 1),
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1)
         };
         ((Path)element.Child).Data = geometry;
         ((Path)element.Child).Fill = _runColor;
         element.BorderBrush = _runColor;

         double leftBorder = innerIndex == 0 ? 2 : 0;
         double rightBorder = innerIndex - 1 == runLength ? 2 : 0;
         element.Margin = new Thickness(leftBorder, 0, rightBorder, 1);
         double bottom = _underline ? 1 : 0;
         element.BorderThickness = new Thickness(0, 0, 0, bottom);
         element.ToolTip = _hoverText;

         return element;
      }

      public bool IsEquivalent(IElementProvider other) {
         var that = other as GeometryElementProvider;
         if (that == null) return false;
         if (this._parser != that._parser) return false;
         if (this._runColor != that._runColor) return false;
         if (this._underline != that._underline) return false;
         if (this._hoverText != that._hoverText) return false;
         return true;
      }

      public void Recycle(FrameworkElement element) { _recycles.Enqueue((Border)element); }
   }

   public interface IDataRun {
      InterpretationRule Interpret { get; }
      JumpRule Jump { get; }
      IEditor Editor { get; }
      IElementProvider Provider { get; }

      int GetLength(byte[] data, int startPoint);
   }

   public class SimpleDataRun : IDataRun {
      static readonly IEditor DefaultEditor = new DisableEditor();
      readonly int _length;

      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }
      public IEditor Editor { get; set; }
      public IElementProvider Provider { get; private set; }

      public SimpleDataRun(IElementProvider provider, int length) {
         Provider = provider;
         _length = length;
         Editor = DefaultEditor;
      }

      public int GetLength(byte[] data, int startPoint) { return _length; }
   }
}
