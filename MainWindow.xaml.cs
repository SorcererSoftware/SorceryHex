using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for MainWindow.xaml
   /// </summary>
   partial class MainWindow : Window, ICommandFactory {
      #region Utils

      const int MaxColumnCount = 0x20;

      static readonly int ElementWidth = 26, ElementHeight = 20;
      static readonly IEnumerable<Key> arrowKeys = new[] { Key.Left, Key.Right, Key.Up, Key.Down };

      static int CombineLocation(UIElement ui, int columns) { return Grid.GetColumn(ui) + Grid.GetRow(ui) * columns; }
      static void SplitLocation(UIElement ui, int columns, int location) {
         Grid.SetRow(ui, location / columns);
         Grid.SetColumn(ui, location % columns);
      }

      static void UpdateDefinitions<T>(Func<T> elementFactory, IList<T> definitions, int delta) {
         for (int i = 0; i < delta; i++) definitions.Add(elementFactory());
         for (int i = 0; i > delta; i--) definitions.RemoveAt(definitions.Count - 1);
      }
      static void UpdateRows(Grid grid, int delta) { UpdateDefinitions(() => new RowDefinition(), grid.RowDefinitions, delta); }
      static void UpdateColumns(Grid grid, int delta) { UpdateDefinitions(() => new ColumnDefinition(), grid.ColumnDefinitions, delta); }
      static void UpdateColumnsRows(Grid grid, int cdelta, int rdelta) { UpdateColumns(grid, cdelta); UpdateRows(grid, rdelta); }

      static void UpdateWidthHeight(FrameworkElement element, int x, int y) { element.Width = x * ElementWidth; element.Height = y * ElementHeight; }
      static Size DesiredWorkArea(FrameworkElement container) {
         return new Size((int)(container.ActualWidth / ElementWidth), (int)(container.ActualHeight / ElementHeight));
      }

      #endregion

      Func<byte[], IElementFactory> _create;
      IElementFactory _holder;
      int _offset = 0;

      public MainWindow(Func<byte[], IElementFactory> create, byte[] data) {
         _create = create;
         _holder = _create(data);
         InitializeComponent();
         ScrollBar.Minimum = -MaxColumnCount;
         ScrollBar.Maximum = _holder.Length;
      }

      #region Helper Methods

      void MainFocus() {
         DependencyObject scope = FocusManager.GetFocusScope(MultiBox);
         FocusManager.SetFocusedElement(scope, this as IInputElement);
      }

      void Add(int start, int length) {
         int rows = Body.RowDefinitions.Count, cols = Body.ColumnDefinitions.Count;
         var elements = _holder.CreateElements(this, _offset + start, length).ToArray();
         Debug.Assert(elements.Length == length);
         for (var i = 0; i < elements.Length; i++) {
            SplitLocation(elements[i], cols, start + i);
            Body.Children.Add(elements[i]);
         }

         if (_sortInterpretations) {
            var interpretations = _interpretations.Values.Distinct().OrderBy(KeyElementLocation);
            InterpretationPane.Children.Clear();
            foreach (var element in interpretations) InterpretationPane.Children.Add(element);
            _sortInterpretations = false;
         }
      }

      int KeyElementLocation(FrameworkElement interpretation) {
         var keysForInterpretation = _interpretations.Keys.Where(key => _interpretations[key] == interpretation);
         Debug.Assert(keysForInterpretation.Count() == _interpretationReferenceCounts[interpretation]);
         // wrapped elements are not directly in the body and don't have a row/column.
         keysForInterpretation = keysForInterpretation.Select(FindElementInBody);
         return keysForInterpretation.Select(key => CombineLocation(key, Body.ColumnDefinitions.Count)).Min();
      }

      void UpdateHeaderColumn(int oldRows, int newRows) {
         if (newRows == oldRows) return;

         // add new header rows
         for (int i = oldRows; i < newRows; i++) {
            var headerText = (_offset + i * Body.ColumnDefinitions.Count).ToHexString();
            var block = new TextBlock { Text = headerText, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(block, i);
            Headers.Children.Add(block);
         }

         // remove excess header rows
         if (newRows < oldRows) {
            foreach (var item in Headers.Children.Where<TextBlock>(block => Grid.GetRow(block) >= newRows)) {
               Headers.Children.Remove(item);
            }
         }
      }

      void UpdateHeaderText() {
         int cols = Body.ColumnDefinitions.Count;
         foreach (TextBlock block in Headers.Children) {
            int location = Grid.GetRow(block) * cols + _offset;
            block.Text = location.ToHexString();
         }
      }

      void JumpTo(int location) {
         location = Math.Min(Math.Max(-MaxColumnCount, location), _holder.Length);

         foreach (FrameworkElement element in Body.Children) Recycle(element);
         Body.Children.Clear();

         _offset = location;
         Add(0, Body.RowDefinitions.Count * Body.ColumnDefinitions.Count);
         ScrollBar.Value = _offset;
         UpdateHeaderText();
      }

      void ShiftRows(int rows) {
         int all = Body.RowDefinitions.Count * Body.ColumnDefinitions.Count;
         int add = Body.ColumnDefinitions.Count * rows;
         if (_offset - add < -MaxColumnCount || _offset - add > _holder.Length) return;

         IList<FrameworkElement> children = new List<FrameworkElement>();
         foreach (FrameworkElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int row = Grid.GetRow(element);
            row += rows;
            if (row < 0 || row >= Body.RowDefinitions.Count) {
               Body.Children.Remove(element);
               Recycle(element);
            } else {
               Grid.SetRow(element, row);
            }
         }

         children.Clear();
         foreach (FrameworkElement child in BackgroundBody.Children) children.Add(child);
         foreach (var element in children) {
            int row = Grid.GetRow(element);
            row += rows;
            if (row < 0 || row >= BackgroundBody.RowDefinitions.Count) {
               BackgroundBody.Children.Remove(element);
               _interpretationBackgrounds.Enqueue(element);
            } else {
               Grid.SetRow(element, row);
            }
         }

         _offset -= add;
         if (rows > 0) Add(0, add);
         else Add(all + add, -add);
      }

      void ShiftColumns(int shift) {
         int all = Body.RowDefinitions.Count * Body.ColumnDefinitions.Count;
         if (_offset - shift < -MaxColumnCount || _offset - shift > _holder.Length) return;

         IList<FrameworkElement> children = new List<FrameworkElement>();
         foreach (FrameworkElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int loc = CombineLocation(element, Body.ColumnDefinitions.Count);
            loc += shift;
            if (loc < 0 || loc >= all) {
               Body.Children.Remove(element);
               Recycle(element);
            } else {
               SplitLocation(element, Body.ColumnDefinitions.Count, loc);
            }
         }

         children.Clear();
         foreach (FrameworkElement child in BackgroundBody.Children) children.Add(child);
         foreach (var element in children) {
            int loc = CombineLocation(element, BackgroundBody.ColumnDefinitions.Count);
            loc += shift;
            if (loc < 0 || loc >= all) {
               Body.Children.Remove(element);
               _interpretationBackgrounds.Enqueue(element);
            } else {
               SplitLocation(element, BackgroundBody.ColumnDefinitions.Count, loc);
            }
         }

         _offset -= shift;
         if (shift > 0) Add(0, shift);
         else Add(all + shift, -shift);
      }

      void Scroll(int dif) {
         if (dif == 0) return;
         var sign = Math.Sign(dif);
         var magn = Math.Abs(dif);
         magn -= magn % Body.ColumnDefinitions.Count; // discard the column portion
         int all = Body.RowDefinitions.Count * Body.ColumnDefinitions.Count;
         if (magn > all) { JumpTo(_offset - (magn * sign)); return; }

         int rowPart = magn / Body.ColumnDefinitions.Count;
         ShiftRows(rowPart * sign);
         UpdateHeaderText();
         ScrollBar.Value = _offset;
      }

      void Recycle(FrameworkElement element) {
         _holder.Recycle(this, element);
      }

      #endregion

      #region Events

      void Resize(object sender, EventArgs e) {
         int oldRows = Body.RowDefinitions.Count, oldCols = Body.ColumnDefinitions.Count;
         var newSize = DesiredWorkArea(ResizeGrid);
         int newCols = (int)newSize.Width, newRows = (int)newSize.Height;
         newCols = Math.Min(newCols, MaxColumnCount);
         ScrollBar.SmallChange = newCols;
         ScrollBar.LargeChange = newCols * newRows;

         int oldTotal = oldCols * oldRows, newTotal = newCols * newRows;
         if (oldTotal == newTotal) return;

         Action<Grid> updateSize = g => {
            UpdateWidthHeight(g, newCols, newRows);
            UpdateColumnsRows(g, newCols - oldCols, newRows - oldRows);
         };

         // update container sizes
         updateSize(Body);
         updateSize(BackgroundBody);
         UpdateRows(Headers, newRows - oldRows);
         Headers.Height = newRows * ElementHeight;

         // update element locations (remove excess elements)
         IList<FrameworkElement> children = new List<FrameworkElement>();
         foreach (FrameworkElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int loc = CombineLocation(element, oldCols);
            if (loc >= newTotal) {
               Body.Children.Remove(element);
               Recycle(element);
            } else {
               SplitLocation(element, newCols, loc);
            }
         }

         // update header column
         UpdateHeaderColumn(oldRows, newRows);

         if (oldCols != newCols) UpdateHeaderText();

         // add more elements if needed
         if (oldTotal < newTotal) Add(oldTotal, newTotal - oldTotal);
      }

      void OnScroll(object sender, ScrollEventArgs e) {
         if (ScrollBar.Value < ScrollBar.Minimum || ScrollBar.Value > ScrollBar.Maximum) {
            ScrollBar.Value = 0;
         }

         int dif = _offset - (int)ScrollBar.Value;
         Scroll(dif);
      }

      void ScrollWheel(object sender, MouseWheelEventArgs e) {
         ShiftRows(Math.Sign(e.Delta));
         ScrollBar.Value = _offset;
         UpdateHeaderText();
      }

      void MouseClick(object sender, MouseButtonEventArgs e) {
         MainFocus();
         if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
            // check for jump
            var element = _jumpers.Keys.FirstOrDefault(jumper => jumper.IsMouseOver);
            if (element != null) {
               var list = _jumpers[element];
               if (list.Length > 1) {
                  BodyContextMenu.Items.Clear();
                  foreach (var dest in list) {
                     var header = dest.ToHexString();
                     while (header.Length < 6) header = "0" + header;
                     var item = new MenuItem { Header = header };
                     BodyContextMenu.Items.Add(item);
                     item.Click += (s, e1) => JumpTo(item.Header.ToString().ParseAsHex());
                  }
                  BodyContextMenu.IsOpen = true;
               } else JumpTo(list[0]);
            }
         }
      }

      void HandleKey(object sender, KeyEventArgs e) {
         if (Keyboard.Modifiers == ModifierKeys.Control) {
            switch (e.Key) {
               case Key.Left:  ShiftColumns(-1); break;
               case Key.Right: ShiftColumns(1); break;
               case Key.Down:  ShiftRows(1); break;
               case Key.Up:    ShiftRows(-1); break;
               case Key.G:     GotoClick(null, null); break;
               case Key.F:     FindClick(null, null); break;
               case Key.O:     OpenClick(null, null); break;
               case Key.I:     InterpretItem.IsChecked = !InterpretItem.IsChecked; InterpretClick(InterpretItem, null); break;
               case Key.B:     break; // only for testing
            }

            if (arrowKeys.Contains(e.Key)) {
               ScrollBar.Value = _offset;
               UpdateHeaderText();
            }
         } else {
            switch (e.Key) {
               case Key.F3:
                  if (Keyboard.Modifiers == ModifierKeys.Shift) FindPrevious(null, null);
                  else FindNext(null, null);
                  break;
            }
         }

         if (e.Key == Key.Escape) {
            // some kind of modal knowledge required here...
         }
      }

      static readonly Key[] HexKeys = new[] {
         Key.NumPad0, Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4,
         Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9,
         Key.D0, Key.D1, Key.D2, Key.D3, Key.D4,
         Key.D5, Key.D6, Key.D7, Key.D8, Key.D9,
         Key.A, Key.B, Key.C, Key.D, Key.E, Key.F
      };
      void HandleMultiBoxKey(object sender, KeyEventArgs e) {
         if (MultiBoxLabel.Text == "Goto") {
            // sanitize for goto
            int caret = MultiBox.CaretIndex;
            int selection = MultiBox.SelectionLength;
            MultiBox.Text = new string(MultiBox.Text.ToUpper().Where(Utils.Hex.Contains).ToArray());
            MultiBox.CaretIndex = Math.Min(caret, MultiBox.Text.Length);
            MultiBox.SelectionLength = selection;

            // check for special keys
            if (e.Key == Key.Escape) {
               MultiBoxContainer.Visibility = Visibility.Hidden;
               MainFocus();
            }
            if (e.Key == Key.Enter) {
               int hex = MultiBox.Text.ParseAsHex();
               JumpTo(hex);
               MultiBoxContainer.Visibility = Visibility.Hidden;
               MainFocus();
            }

            // only allow hex keys
            if (!HexKeys.Contains(e.Key)) e.Handled = true;
         } else if (MultiBoxLabel.Text == "Find") {
            // dumb find: make it smarter later

            // check for special keys
            if (e.Key == Key.Escape) {
               MultiBoxContainer.Visibility = Visibility.Hidden;
               MainFocus();
            }
            if (e.Key == Key.Enter) {
               _findPositions = _holder.Find(MultiBox.Text);
               if (_findPositions.Count == 0) {
                  MessageBox.Show("No matches found for: " + MultiBox.Text);
                  return;
               }
               _findIndex = 0;
               JumpTo(_findPositions[_findIndex]);
               MultiBoxContainer.Visibility = Visibility.Hidden;
               MainFocus();
            }
         }
      }

      IList<int> _findPositions;
      int _findIndex;

      #endregion

      #region Menu

      void OpenClick(object sender, RoutedEventArgs e) {
         var data = Utils.LoadRom();
         if (data == null) return;
         _holder = _create(data);
         ScrollBar.Maximum = _holder.Length;
         Body.Children.Clear();
         JumpTo(0);
      }

      void ExitClick(object sender, RoutedEventArgs e) { Close(); }

      void InterpretClick(object sender, EventArgs e) {
         var item = sender as MenuItem;
         InterpretationPane.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
      }

      void ThemeClick(object sender, EventArgs e) {
         var theme = Solarized.Theme.Instance;
         if (theme.CurrentVariant == Solarized.Theme.Variant.Light) {
            theme.CurrentVariant = Solarized.Theme.Variant.Dark;
         } else {
            theme.CurrentVariant = Solarized.Theme.Variant.Light;
         }
      }

      void FindClick(object sender, EventArgs e) {
         MultiBoxLabel.Text = "Find";
         MultiBoxContainer.Visibility = Visibility.Visible;
         Keyboard.Focus(MultiBox);
         MultiBox.SelectAll();
      }

      void FindPrevious(object sender, EventArgs e) {
         if (_findPositions == null || _findPositions.Count == 0) return;
         _findIndex--;
         if (_findIndex < 0) _findIndex = _findPositions.Count - 1;
         JumpTo(_findPositions[_findIndex]);
      }

      void FindNext(object sender, EventArgs e) {
         if (_findPositions == null || _findPositions.Count == 0) return;
         _findIndex++;
         if (_findIndex >= _findPositions.Count) _findIndex = 0;
         JumpTo(_findPositions[_findIndex]);
      }

      void GotoClick(object sender, EventArgs e) {
         MultiBoxLabel.Text = "Goto";
         MultiBoxContainer.Visibility = Visibility.Visible;
         Keyboard.Focus(MultiBox);
         MultiBox.SelectAll();
      }

      void AboutClick(object sender, RoutedEventArgs e) {
         switch (((MenuItem)sender).Header.ToString()) {
            case "_About":
               Process.Start("http://sorcerersoftware.appspot.com");
               break;
            case "About _Solarized":
               Process.Start(Solarized.Theme.Info);
               break;
         }
      }

      #endregion

      #region Command Factory

      readonly Dictionary<FrameworkElement, int[]> _jumpers = new Dictionary<FrameworkElement, int[]>();
      public void CreateJumpCommand(FrameworkElement element, params int[] jumpLocation) {
         _jumpers[element] = jumpLocation;
      }
      public void RemoveJumpCommand(FrameworkElement element) {
         _jumpers.Remove(element);
      }

      bool _sortInterpretations;
      readonly Dictionary<FrameworkElement, FrameworkElement> _interpretations = new Dictionary<FrameworkElement, FrameworkElement>();
      readonly Dictionary<FrameworkElement, int> _interpretationReferenceCounts = new Dictionary<FrameworkElement, int>();

      public void LinkToInterpretation(FrameworkElement element, FrameworkElement visual) {
         _interpretations[element] = visual;
         visual.Margin = new Thickness(5);
         if (!_interpretationReferenceCounts.ContainsKey(visual)) _interpretationReferenceCounts[visual] = 0;
         _interpretationReferenceCounts[visual]++;
         if (_interpretationReferenceCounts[visual] == 1) {
            _sortInterpretations = true;
            visual.MouseEnter += MouseEnterInterpretation;
            visual.MouseLeave += MouseLeaveInterpretation;
         }
      }
      public void UnlinkFromInterpretation(FrameworkElement element) {
         var visual = _interpretations[element];
         _interpretations.Remove(element);
         _interpretationReferenceCounts[visual]--;
         if (_interpretationReferenceCounts[visual] == 0) {
            InterpretationPane.Children.Remove(visual);
            _interpretationReferenceCounts.Remove(visual);
            visual.MouseEnter -= MouseEnterInterpretation;
            visual.MouseLeave -= MouseLeaveInterpretation;
         }
      }

      #region Interpretation Helpers

      readonly Queue<FrameworkElement> _interpretationBackgrounds = new Queue<FrameworkElement>();

      void MouseEnterInterpretation(object sender, EventArgs e) {
         var visual = (FrameworkElement)sender;

         foreach (var element in _interpretations.Keys.Where(key => _interpretations[key] == visual).Select(FindElementInBody)) {
            var rectangle = _interpretationBackgrounds.Count > 0 ? _interpretationBackgrounds.Dequeue() : new Border {
               Background = Solarized.Theme.Instance.Backlight,
               Tag = this
            };
            Grid.SetColumn(rectangle, Grid.GetColumn(element));
            Grid.SetRow(rectangle, Grid.GetRow(element));
            BackgroundBody.Children.Add(rectangle);
         }
      }

      FrameworkElement FindElementInBody(FrameworkElement element) {
         while (!Body.Children.Contains(element)) element = (FrameworkElement)element.Parent;
         return element;
      }

      void MouseLeaveInterpretation(object sender, EventArgs e) {
         var children = new List<FrameworkElement>();
         foreach (FrameworkElement child in BackgroundBody.Children) children.Add(child);
         foreach (var child in children.Where(c => c.Tag == this)) {
            BackgroundBody.Children.Remove(child);
            _interpretationBackgrounds.Enqueue(child);
         }
      }

      #endregion

      #endregion
   }
}
