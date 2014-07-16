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
      int Length { get; }
      void Load(ICommandFactory commander);
      IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsStartOfDataBlock(int location);
      bool IsWithinDataBlock(int location);
      string GetLabel(int location);
      int GetDataBlockStart(int location);
      int GetDataBlockLength(int location);
      FrameworkElement GetInterpretation(int location);
      IList<int> Find(string term);
   }

   public class UpdateLocationEventArgs : EventArgs {
      public readonly IEnumerable<int> UpdateList;
      public UpdateLocationEventArgs(params int[] list) { UpdateList = list; }
   }

   public interface IEditor {
      FrameworkElement CreateElementEditor(int location);
      void Edit(int location, char c); // called because the user entered an edit key
      void CompleteEdit(int location); // called because the user entered a key that signifies the end of an edit
      event EventHandler<UpdateLocationEventArgs> MoveToNext; // sent up because the editor realizes it's done with the current edit
   }

   public class DisableEditor : IEditor {
      public FrameworkElement CreateElementEditor(int location) { return null; }
      public void Edit(int location, char c) { }
      public void CompleteEdit(int location) { }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;
   }

   public class InlineTextEditor : IEditor {
      readonly byte[] _data;
      readonly Func<byte[], string> _convertToString;
      readonly Func<string, byte[]> _convertToValue;
      readonly int _length;

      TextBox _box;
      int _location;

      public InlineTextEditor(byte[] data, int length, Func<byte[], string> toString, Func<string, byte[]> toValue) {
         _data = data; _length = length;
         _convertToString = toString;
         _convertToValue = toValue;
      }

      public FrameworkElement CreateElementEditor(int location) {
         _location = location;
         if (_box != null) _box.KeyDown -= KeyDown;
         _box = new TextBox();
         byte[] temp = new byte[_length];
         Array.Copy(_data, location, temp, 0, _length);
         _box.Text = _convertToString(temp);
         _box.SelectAll();
         _box.KeyDown += KeyDown;
         return _box;
      }

      public void Edit(int location, char c) { }
      public void CompleteEdit(int location) { }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void KeyDown(object sender, KeyEventArgs e) {
         if (e.Key != Key.Enter) return;
         e.Handled = true;
         try {
            var result = _convertToValue(_box.Text);
            Array.Copy(result, 0, _data, _location, _length);
            MoveToNext(this, new UpdateLocationEventArgs(_location));
         } catch (Exception) {
            // TODO some kind of error message
         }
      }
   }

   public class InlineComboEditor : IEditor {
      public readonly dynamic[] Names;
      public readonly string HoverText;
      readonly byte[] _data;
      readonly int _stride;
      ComboBox _box;
      int _location;

      public InlineComboEditor(byte[] data, int stride, dynamic[] names, string hoverText) {
         _data = data;
         _stride = stride;
         Names = names;
         HoverText = hoverText;
      }

      public FrameworkElement CreateElementEditor(int location) {
         _location = location;
         if (_box != null) {
            _box.DropDownClosed -= DropDownClosed;
         }
         _box = new ComboBox();
         for (int i = 0; i < Names.Length; i++) {
            var option = EnumElementProvider.AsString(Names, i);
            _box.Items.Add(option);
         }
         _box.SelectedIndex = _data.ReadData(_stride, location);
         _box.IsDropDownOpen = true;
         _box.DropDownClosed += DropDownClosed;
         return _box;
      }

      public void Edit(int location, char c) { }
      public void CompleteEdit(int location) { }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      void DropDownClosed(object sender, EventArgs e) {
         int value = _box.SelectedIndex;
         for (int i = 0; i < _stride; i++) {
            _data[_location + i] = (byte)(value % 0x100);
            value >>= 8;
         }
         MoveToNext(sender, new UpdateLocationEventArgs(_location));
      }
   }

   public interface IModel : IParser, IEditor { }

   public interface IPartialModel {
      bool CanEdit(int location);
      string GetLabel(int location);
      IEditor Editor { get; }
      void Load(ICommandFactory commander);
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
      readonly byte[] _data;
      readonly Queue<Path> _recycles = new Queue<Path>();
      bool _loaded;

      public int Length { get { return _data.Length; } }

      public CompositeModel(byte[] data, params IPartialModel[] children) {
         _data = data;
         _children = children;
         foreach (var child in _children) {
            if (child.Editor == null) continue;
            child.Editor.MoveToNext += ChainMoveToNext;
         }
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

      #endregion

      #region Editor

      string _editBuffer = string.Empty;

      public FrameworkElement CreateElementEditor(int location) {
         return _children.Where(child => child.Editor != null).Select(child => child.Editor.CreateElementEditor(location)).FirstOrDefault();
      }

      public void Edit(int location, char c) {
         foreach (var child in _children) {
            if (!child.CanEdit(location)) continue;
            child.Editor.Edit(location, c);
            return;
         }

         if (!Utils.Hex.Contains(c) && !Utils.Hex.ToLower().Contains(c)) return;

         _editBuffer += c;
         _data[location] = (byte)_editBuffer.ParseAsHex();
         if (_editBuffer.Length >= 2) {
            MoveToNext(this, new UpdateLocationEventArgs(location));
            _editBuffer = string.Empty;
         }
      }

      public void CompleteEdit(int location) {
         foreach (var child in _children) {
            if (!child.CanEdit(location)) continue;
            child.Editor.CompleteEdit(location);
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
}
