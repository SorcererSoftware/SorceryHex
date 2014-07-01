using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SorceryHex {
   public interface ILabeler {
      string GetLabel(int index);
   }

   public interface IRunParser {
      void Load(ICommandFactory commander, IRunStorage runs);
      IEnumerable<int> Find(string term);
   }

   public interface IRunStorage {
      byte[] Data { get; }
      void AddRun(int location, IDataRun run);
      void AddLabeler(ILabeler labeler);
      bool IsFree(int location);
      int NextUsed(int location);
   }

   public class RunStorage : IPartialModel, IRunStorage, IEditor {
      readonly IDictionary<int, IDataRun> _runs = new Dictionary<int, IDataRun>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly IDictionary<int, FrameworkElement> _interpretations = new Dictionary<int, FrameworkElement>();
      readonly HashSet<FrameworkElement> _interpretationLinks = new HashSet<FrameworkElement>();
      readonly HashSet<FrameworkElement> _jumpLinks = new HashSet<FrameworkElement>();
      readonly HashSet<IDataRun> _runSet = new HashSet<IDataRun>();
      readonly IRunParser[] _runParsers;
      IList<ILabeler> _labelers = new List<ILabeler>();

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

      public void AddLabeler(ILabeler labeler) { if (!_labelers.Contains(labeler)) _labelers.Add(labeler); }

      public bool IsFree(int location) {
         if (_runs.ContainsKey(location)) return false;
         int keyIndex = ~_keys.BinarySearch(location) - 1;
         if (keyIndex < 0) return true;
         int prev = _keys[keyIndex];
         int prevEnd = prev + _runs[prev].GetLength(Data, prev);
         return prevEnd <= location;
      }

      public int NextUsed(int location) {
         if (_runs.ContainsKey(location)) return location;
         int index = ~_keys.BinarySearch(location);
         return index >= _keys.Count ? Data.Length : _keys[index];
      }

      #endregion

      #region PartialModel

      public bool CanEdit(int location) {
         int index = _keys.BinarySearch(location);
         if (index < 0) index = Math.Max(~index - 1, 0);
         int startPoint = _keys[index];
         return startPoint + _runs[startPoint].GetLength(Data, startPoint) > location && startPoint <= location && _runs[startPoint].Editor != null;
      }

      public IEditor Editor { get { return this; } }

      public void Load(ICommandFactory commander) {
         foreach (var parser in _runParsers) {
            var type = parser.GetType().ToString().Split('.').Last();
            using (AutoTimer.Time(type)) {
               parser.Load(commander, this);
               UpdateList();
            }
         }

#if DEBUG
         int prevEnd = 0;
         foreach (var key in _keys) {
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
                  var element = currentRun.Provider.ProvideElement(commander, Data, dataIndex, loc - dataIndex + j, runEnd - dataIndex + 1);
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

      public string GetLabel(int location) {
         return _labelers.Select(lbl => lbl.GetLabel(location)).Where(str => !string.IsNullOrEmpty(str)).FirstOrDefault();
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

      public FrameworkElement CreateElementEditor(int location) {
         int index = _keys.BinarySearch(location);
         if (index < 0) index = Math.Max(~index - 1, 0);
         int startPoint = _keys[index];
         if (!(startPoint + _runs[startPoint].GetLength(Data, startPoint) > location && startPoint <= location && _runs[startPoint].Editor != null)) return null;
         return _runs[startPoint].Editor.CreateElementEditor(startPoint);
      }

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

      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void ChainMoveNext(object sender, UpdateLocationEventArgs e) { MoveToNext(sender, e); }

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
         _keys.Sort();
         _listNeedsUpdate = false;
      }

      #endregion
   }
}
