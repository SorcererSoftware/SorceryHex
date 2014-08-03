using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IParser {
      ISegment Segment { get; }
      void Load(ICommandFactory commander);
      IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsStartOfDataBlock(int location);
      bool IsWithinDataBlock(int location);
      string GetLabel(int location);
      int GetDataBlockStart(int location);
      int GetDataBlockLength(int location);
      FrameworkElement GetInterpretation(int location);
      IEnumerable<int> Find(string term);
   }

   public class UpdateLocationEventArgs : EventArgs {
      public readonly IEnumerable<int> UpdateList;
      public UpdateLocationEventArgs(params int[] list) { UpdateList = list; }
   }

   public interface IEditor {
      FrameworkElement CreateElementEditor(ISegment segment);
      void Edit(ISegment segment, char c); // called because the user entered an edit key
      void CompleteEdit(ISegment segment); // called because the user entered a key that signifies the end of an edit
      event EventHandler<UpdateLocationEventArgs> MoveToNext; // sent up because the editor realizes it's done with the current edit
   }

   public class DisableEditor : IEditor {
      public FrameworkElement CreateElementEditor(ISegment segment) { return null; }
      public void Edit(ISegment segment, char c) { }
      public void CompleteEdit(ISegment segment) { }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;
   }

   public class InlineTextEditor : IEditor {
      readonly Func<ISegment, string> _convertToString;
      readonly Func<string, ISegment> _convertToValue;
      readonly int _length;

      TextBox _box;
      ISegment _segment;

      public InlineTextEditor(int length, Func<ISegment, string> toString, Func<string, ISegment> toValue) {
         _length = length;
         _convertToString = toString;
         _convertToValue = toValue;
      }

      public FrameworkElement CreateElementEditor(ISegment segment) {
         _segment = segment;
         if (_box != null) _box.KeyDown -= KeyDown;
         _box = new TextBox();
         _box.Text = _convertToString(segment);
         _box.SelectAll();
         _box.KeyDown += KeyDown;
         return _box;
      }

      public void Edit(ISegment segment, char c) { }
      public void CompleteEdit(ISegment segment) { }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void KeyDown(object sender, KeyEventArgs e) {
         if (e.Key != Key.Enter) return;
         e.Handled = true;
         try {
            var result = _convertToValue(_box.Text);
            for (int i = 0; i < result.Length; i++) {
               _segment.Write(i, 1, result[i]);
            }

            MoveToNext(this, new UpdateLocationEventArgs(_segment.Location));
         } catch (Exception) {
            // TODO some kind of error message
         }
      }
   }

   public class InlineComboEditor : IEditor {
      public readonly dynamic[] Names;
      public readonly string HoverText;
      readonly int _stride;
      ISegment _segment;
      ComboBox _box;

      public InlineComboEditor(int stride, dynamic[] names, string hoverText) {
         _stride = stride;
         Names = names;
         HoverText = hoverText;
      }

      public FrameworkElement CreateElementEditor(ISegment segment) {
         _segment = segment;
         if (_box != null) {
            _box.DropDownClosed -= DropDownClosed;
         }
         _box = new ComboBox();
         for (int i = 0; i < Names.Length; i++) {
            var option = EnumElementProvider.AsString(Names, i);
            _box.Items.Add(option);
         }
         _box.SelectedIndex = _segment.Read(0, _stride);
         _box.IsDropDownOpen = true;
         _box.DropDownClosed += DropDownClosed;
         return _box;
      }

      public void Edit(ISegment segment, char c) { }
      public void CompleteEdit(ISegment segment) { }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void DropDownClosed(object sender, EventArgs e) {
         int value = _box.SelectedIndex;
         _segment.Write(0, _stride, value);
         MoveToNext(sender, new UpdateLocationEventArgs(_segment.Location));
      }
   }

   public interface IModel : IParser, IEditor {
      IModel Duplicate(int start, int length);
   }

   public interface IPartialModel {
      bool CanEdit(ISegment segment);
      string GetLabel(int location);
      IEditor Editor { get; }
      void Load(ICommandFactory commander);

      /// <param name="segment">The new data segment for the new partial model</param>
      /// <param name="start">
      ///   The location of the new segment in relation to the current partial model's segment,
      ///   assuming that the new segment is a partial copy of the current segment.
      /// </param>
      /// <returns></returns>
      IPartialModel CreateNew(ISegment segment, int start);
      IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsStartOfDataBlock(int location);
      bool IsWithinDataBlock(int location);
      int GetDataBlockStart(int location);
      int GetDataBlockLength(int location);
      FrameworkElement GetInterpretation(int location);
      IEnumerable<int> Find(string term);
   }

   public class CompositeModel : IModel {
      readonly IList<IPartialModel> _children;
      readonly Queue<Path> _recycles = new Queue<Path>();
      bool _loaded;

      readonly ISegment _segment;
      public ISegment Segment { get { return _segment; } }

      public CompositeModel(ISegment segment, params IPartialModel[] children) {
         _segment = segment;
         _children = children;
         foreach (var child in _children) {
            if (child.Editor == null) continue;
            child.Editor.MoveToNext += ChainMoveToNext;
         }
      }

      public IModel Duplicate(int start, int length) {
         var segment = _segment.Duplicate(start, length);
         var dup = new CompositeModel(segment, _children.Select(child => child.CreateNew(segment, start)).ToArray()) { _loaded = true };
         return dup;
      }

      #region Parser

      public void Load(ICommandFactory commander) {
         _loaded = false;
         foreach (var child in _children) {
            using (AutoTimer.Time(child.GetType().ToString().Split('.').Last())) {
               child.Load(commander);
            }
         }
         _loaded = true;
      }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         Debug.Assert(length < 0x20 * 0x40);
         var list = new List<FrameworkElement>();

         int pre = 0, post = 0;
         if (start < 0) { pre = -start; start = 0; length -= pre; }
         if (length < 0) { pre += length; length = 0; }
         if (start + length >= _segment.Length) { post = start + length - _segment.Length; length = _segment.Length - start; }
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
            var child = (IPartialModel)element.GetCreator();
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

      public string GetLabel(int location) {
         return _children.Select(lbl => lbl.GetLabel(location)).Where(str => !string.IsNullOrEmpty(str)).FirstOrDefault() ?? location.ToHexString();
      }

      public int GetDataBlockStart(int location) {
         var selectedChild = _children.First(child => child.IsStartOfDataBlock(location) || child.IsWithinDataBlock(location));
         return selectedChild.GetDataBlockStart(location);
      }

      public int GetDataBlockLength(int location) {
         var selectedChild = _children.First(child => child.IsStartOfDataBlock(location) || child.IsWithinDataBlock(location));
         return selectedChild.GetDataBlockLength(location);
      }

      public FrameworkElement GetInterpretation(int location) {
         if (!_loaded) return null;
         return _children.Select(child => child.GetInterpretation(location)).Where(interpretation => interpretation != null).FirstOrDefault();
      }

      public IEnumerable<int> Find(string term) {
         if (!_loaded) yield break;

         var sanitized = term.ToUpper().Replace(" ", "");
         if (sanitized.Length % 2 == 0 && sanitized.All(Utils.Hex.Contains)) {
            byte[] searchTerm =
               Enumerable.Range(0, sanitized.Length / 2)
               .Select(i => (byte)sanitized.Substring(i * 2, 2).ParseAsHex())
               .ToArray();

            for (int i = 0, j = 0; i < _segment.Length; i++) {
               j = _segment[i] == searchTerm[j] ? j + 1 : 0;
               if (j < searchTerm.Length) continue;
               yield return i - j + 1;
               j = 0;
            }
         }

         foreach (var childSearchResult in _children
            .Select(child => child.Find(term) ?? new int[0])
            .Aggregate(Enumerable.Concat)) {
            yield return childSearchResult;
         }
      }

      #endregion

      #region Editor

      string _editBuffer = string.Empty;

      public FrameworkElement CreateElementEditor(ISegment segment) {
         return _children.Where(child => child.Editor != null).Select(child => child.Editor.CreateElementEditor(segment)).FirstOrDefault();
      }

      public void Edit(ISegment segment, char c) {
         foreach (var child in _children) {
            if (!child.CanEdit(segment)) continue;
            child.Editor.Edit(segment, c);
            return;
         }

         if (!Utils.Hex.Contains(c) && !Utils.Hex.ToLower().Contains(c)) return;

         _editBuffer += c;
         segment.Write(0, 1, _editBuffer.ParseAsHex());
         if (_editBuffer.Length >= 2) {
            MoveToNext(this, new UpdateLocationEventArgs(segment.Location));
            _editBuffer = string.Empty;
         }
      }

      public void CompleteEdit(ISegment segment) {
         foreach (var child in _children) {
            if (!child.CanEdit(segment)) continue;
            child.Editor.CompleteEdit(segment);
            return;
         }

         _editBuffer = string.Empty;
      }

      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void ChainMoveToNext(object sender, UpdateLocationEventArgs e) { MoveToNext(sender, e); }

      #endregion

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
            if (element == null) element = UseElement(Utils.ByteFlyweights[_segment[k]], IsLightweight(_segment[k]));
            list.Add(element);
         }
         return list;
      }

      IEnumerable<FrameworkElement> CreateRawElements(ICommandFactory commander, int start, int length) {
         return Enumerable.Range(start, length).Select(i => UseElement(Utils.ByteFlyweights[_segment[i]], IsLightweight(_segment[i])));
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
}
