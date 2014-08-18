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
      ISegment Segment { get; }
      void AddRun(int location, IDataRun run);
      void AddLabeler(ILabeler labeler);
      bool IsFree(int location);
      int NextUsed(int location);
   }

   public interface IModelOperations {
      int Repoint(int initialLocation, int newLocation);
      void Clear(IModel model, int offset, int length);
      int FindFreeSpace(int length);
   }

   public class RunStorage : IPartialModel, IRunStorage, IEditor {
      // TODO merge _keys and _runs into a thread-safe, quick-add dictionary implementation
      readonly IDictionary<int, IDataRun> _runs = new Dictionary<int, IDataRun>();
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly IDictionary<int, FrameworkElement> _interpretations = new Dictionary<int, FrameworkElement>();
      readonly HashSet<FrameworkElement> _interpretationLinks = new HashSet<FrameworkElement>();
      readonly HashSet<FrameworkElement> _jumpLinks = new HashSet<FrameworkElement>();
      readonly HashSet<IDataRun> _runSet = new HashSet<IDataRun>();
      readonly IRunParser[] _runParsers;
      readonly List<ILabeler> _labelers = new List<ILabeler>();
      readonly int _labelOffset = 0;

      List<int> _keys = new List<int>();
      bool _listNeedsUpdate;

      #region Run Storage

      public ISegment Segment { get; private set; }

      public RunStorage(ISegment segment, params IRunParser[] runParsers) {
         Segment = segment;
         _runParsers = runParsers;
      }

      RunStorage(ISegment segment, int labelOffset, params IRunParser[] runParsers)
         : this(segment, runParsers) {
         _labelOffset = labelOffset;
      }

      public void AddRun(int location, IDataRun run) {
         lock (_runs) {
            if (_runs.ContainsKey(location)) {
               Debug.Assert(RunsAreEquivalent(_runs[location], run, location));
            } else {
               Debug.Assert(IsFree(location));
               if (!_runSet.Contains(run) && run.Editor != null) {
                  run.Editor.MoveToNext += ChainMoveNext;
                  _runSet.Add(run);
               }
               _runs.Add(location, run);
            }
         }
         _listNeedsUpdate = true;
      }

      public void AddLabeler(ILabeler labeler) { if (!_labelers.Contains(labeler)) _labelers.Add(labeler); }

      public bool IsFree(int location) {
         if (_runs.ContainsKey(location)) return false;
         int prev;
         lock (_keys) {
            int keyIndex = ~_keys.BinarySearch(location) - 1;
            if (keyIndex < 0) return true;
            prev = _keys[keyIndex];
         }
         int prevEnd = prev + _runs[prev].GetLength(Segment.Inner(prev)).Length;
         return prevEnd <= location;
      }

      public int NextUsed(int location) {
         if (_runs.ContainsKey(location)) return location;
         lock (_keys) {
            int index = ~_keys.BinarySearch(location);
            return index >= _keys.Count ? Segment.Length : _keys[index];
         }
      }

      #endregion

      #region PartialModel

      public bool CanEdit(ISegment segment) {
         int index;
         int location = segment.Location;
         lock (_keys) index = _keys.BinarySearch(location);
         if (index < 0) index = Math.Max(~index - 1, 0);
         int startPoint = _keys[index];
         return startPoint + _runs[startPoint].GetLength(Segment.Inner(startPoint)).Length > location && startPoint <= location && _runs[startPoint].Editor != null;
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
            prevEnd = key + _runs[key].GetLength(Segment.Inner(key)).Length;
         }
#endif
      }

      public void LoadAppended(ICommandFactory commander, int length) {
         UpdateList();
         lock (_runs) {
            lock (_keys) {
               _keys.Where(key => key >= Segment.Length - length * 2).Foreach(key => _runs[key + length] = _runs[key]);
               _listNeedsUpdate = true;
            }
         }
      }

      public void Unload(int offset, int length) {
         UpdateList();
         lock (_runs) {
            lock (_keys) {
               _keys.Where(key => key >= offset && key < offset + length).Foreach(key => _runs.Remove(key));
               _listNeedsUpdate = true;
            }
         }
      }

      public void LoadCopied(IList<IPartialModel> search, int newOffset, int length) {
         UpdateList();
         foreach (var model in search) {
            var other = model as RunStorage;
            if (other == null) return;
            lock (_runs) {
               lock (_keys) {
                  lock (other._runs) {
                     lock (other._keys) {
                        other._keys.Where(key => key < length).Foreach(key => _runs[key + newOffset] = other._runs[key]);
                        _listNeedsUpdate = true;
                     }
                  }
               }
            }
         }
         // TODO validate that there are no conflicts with other runs already in the storage
      }

      public IPartialModel CreateNew(ISegment segment, int start) {
         Debug.Assert(segment.HasLength);
         UpdateList();
         var newStorage = new RunStorage(segment, start, _runParsers);

         int startIndex;
         lock (_keys) {
            startIndex = _keys.BinarySearch(start);
            if (startIndex < 0) startIndex = ~startIndex - 1; // not in list: give me the one that starts just before here

            if (startIndex == _keys.Count) return newStorage;
         }

         for (int i = 0; i < segment.Length; ) {
            int loc = start + i;
            if (startIndex >= _keys.Count) {
               i = segment.Length;
               continue;
            }

            int dataIndex = _keys[startIndex];
            if (dataIndex > loc) {
               var sectionLength = Math.Min(segment.Length - i, dataIndex - loc);
               i += sectionLength;
            } else if (dataIndex + _runs[_keys[startIndex]].GetLength(Segment.Inner(dataIndex)).Length < loc) {
               startIndex++;
            } else {
               var currentRun = _runs[_keys[startIndex]];
               int runEnd = dataIndex + currentRun.GetLength(Segment.Inner(dataIndex)).Length;
               runEnd = Math.Min(runEnd, start + segment.Length);
               int lengthInView = runEnd - loc;
               newStorage._keys.Add(_keys[startIndex] - start);
               newStorage._runs[_keys[startIndex] - start] = currentRun;
               if (!newStorage._runSet.Contains(currentRun) && currentRun.Editor != null) {
                  currentRun.Editor.MoveToNext += ChainMoveNext;
                  newStorage._runSet.Add(currentRun);
               }
               startIndex++;
               i += lengthInView;
            }
         }
         newStorage._labelers.AddRange(_labelers);

         return newStorage;
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         UpdateList();
         var elements = new FrameworkElement[length];

         int startIndex;
         lock (_keys) {
            startIndex = _keys.BinarySearch(start);
            if (startIndex < 0) startIndex = ~startIndex - 1; // not in list: give me the one that starts just before here

            if (startIndex == _keys.Count) return elements;
         }

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
            } else if (dataIndex + _runs[_keys[startIndex]].GetLength(Segment.Inner(dataIndex)).Length < loc) {
               startIndex++;
            } else {
               var currentRun = _runs[_keys[startIndex]];
               int runEnd = dataIndex + currentRun.GetLength(Segment.Inner(dataIndex)).Length;
               runEnd = Math.Min(runEnd, start + length);
               int lengthInView = runEnd - loc;
               InterpretData(currentRun, dataIndex);
               for (int j = 0; j < lengthInView; j++) {
                  var element = currentRun.Provider.ProvideElement(commander, Segment, dataIndex, loc - dataIndex + j, runEnd - dataIndex + 1);
                  if (element == null) continue;
                  element.Tag = currentRun.Provider;

                  if (_interpretations.ContainsKey(dataIndex)) {
                     _interpretationLinks.Add(element);
                     commander.LinkToInterpretation(element, _interpretations[dataIndex]);
                  }
                  if (currentRun.Jump != null) {
                     _jumpLinks.Add(element);
                     commander.CreateJumpCommand(element, currentRun.Jump(Segment, dataIndex));
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
         return _labelers.Select(lbl => lbl.GetLabel(location + _labelOffset)).Where(str => !string.IsNullOrEmpty(str)).FirstOrDefault();
      }

      public bool IsStartOfDataBlock(int location) {
         lock (_runs) return _runs.ContainsKey(location);
      }

      public bool IsWithinDataBlock(int location) {
         UpdateList();
         int startAddress;
         lock (_keys) {
            var insertionPoint = _keys.BinarySearch(location);
            if (insertionPoint >= 0) return false;
            insertionPoint = ~insertionPoint - 1;
            startAddress = _keys[insertionPoint];
         }
         return startAddress + _runs[startAddress].GetLength(Segment.Inner(startAddress)).Length > location;
      }

      public int GetDataBlockStart(int location) {
         lock (_keys) {
            var insertionPoint = _keys.BinarySearch(location);
            if (insertionPoint >= 0) return _keys[insertionPoint];
            if (insertionPoint < 0) insertionPoint = ~insertionPoint;
            return _keys[insertionPoint - 1];
         }
      }

      public int GetDataBlockLength(int location) {
         int startAddress = GetDataBlockStart(location);
         return _runs[startAddress].GetLength(Segment.Inner(startAddress)).Length;
      }

      public FrameworkElement GetInterpretation(int location) {
         IDataRun run;
         lock (_keys) {
            var index = _keys.BinarySearch(location);
            if (index < 0) return null;
            run = _runs[_keys[index]];
         }
         InterpretData(run, location);
         if (!_interpretations.ContainsKey(location)) return null;
         return _interpretations[location];
      }

      public IEnumerable<int> Find(string term) {
         var lowerTerm = term.ToLower();
         var results = _runParsers.Select(parser => parser.Find(term) ?? new int[0]).Aggregate(Enumerable.Concat);

         var runStringResults = _runs.Keys.Where(key => {
            var length = _runs[key].GetLength(Segment.Inner(key)).Length;
            var str = _runs[key].Provider.ProvideString(Segment, key, length);
            if (string.IsNullOrEmpty(str)) return false;
            return str.ToLower() == lowerTerm;
         });
         return results.Concat(runStringResults);
      }

      #endregion

      #region Editor

      public FrameworkElement CreateElementEditor(ISegment segment) {
         int location = segment.Location;
         int startPoint = GetStart(location);
         if (!(startPoint + _runs[startPoint].GetLength(Segment.Inner(startPoint)).Length > location && startPoint <= location && _runs[startPoint].Editor != null)) return null;
         return _runs[startPoint].Editor.CreateElementEditor(Segment.Inner(startPoint));
      }

      public void Edit(ISegment segment, char c) {
         int location = segment.Location;
         int startPoint = GetStart(location);
         Debug.Assert(startPoint + _runs[startPoint].GetLength(Segment.Inner(startPoint)).Length > location && startPoint <= location && _runs[startPoint].Editor != null);
         _runs[startPoint].Editor.Edit(Segment.Inner(location), c);
      }

      public void CompleteEdit(ISegment segment) {
         int location = segment.Location;
         int startPoint = GetStart(location);
         Debug.Assert(startPoint + _runs[startPoint].GetLength(Segment.Inner(startPoint)).Length > location && startPoint <= location && _runs[startPoint].Editor != null);
         _runs[startPoint].Editor.CompleteEdit(Segment.Inner(location));
      }

      int GetStart(int location) {
         lock (_keys) {
            int index = _keys.BinarySearch(location);
            if (index < 0) index = Math.Max(~index - 1, 0);
            return _keys[index];
         }
      }

      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void ChainMoveNext(object sender, UpdateLocationEventArgs e) { MoveToNext(sender, e); }

      #endregion

      #region Helpers

      bool RunsAreEquivalent(IDataRun run1, IDataRun run2, int location) {
         var innerSeg = Segment.Inner(location);
         var conditions = new[] {
            run1.Provider.IsEquivalent(run2.Provider),
            run1.Interpret == run2.Interpret,
            run1.Jump == run2.Jump,
            run1.GetLength(innerSeg).Length == run2.GetLength(innerSeg).Length
         };
         return conditions.All(b => b);
      }

      void InterpretData(IDataRun run, int dataIndex) {
         if (run.Interpret == null) return;
         if (!_interpretations.ContainsKey(dataIndex)) {
            var interpretation = run.Interpret(run.GetLength(Segment.Inner(dataIndex)));
            if (interpretation == null) return;
            _interpretations[dataIndex] = interpretation;
         }
      }

      void UpdateList() {
         if (!_listNeedsUpdate) return;
         lock (_runs) {
            lock (_keys) {
               _keys = _runs.Keys.ToList();
               _keys.Sort();
            }
         }
         _listNeedsUpdate = false;
      }

      #endregion
   }
}
