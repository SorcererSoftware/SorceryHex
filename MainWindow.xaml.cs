using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for MainWindow.xaml
   /// </summary>
   partial class MainWindow : Window {
      #region Utils

      const int MaxColumnCount = 0x30;

      static readonly int ElementWidth = 26, ElementHeight = 20;
      static readonly IEnumerable<Key> arrowKeys = new[] { Key.Left, Key.Right, Key.Up, Key.Down };

      public static int CombineLocation(UIElement ui, int columns) {
         return Grid.GetColumn(ui) + Grid.GetRow(ui) * columns;
      }
      public static void SplitLocation(UIElement ui, int columns, int location) {
         Grid.SetRow(ui, location / columns);
         Grid.SetColumn(ui, location % columns);
      }

      static void UpdateDefinitions<T>(Func<T> elementFactory, IList<T> definitions, int delta) {
         for (int i = 0; i < delta; i++) definitions.Add(elementFactory());
         for (int i = 0; i > delta; i--) definitions.RemoveAt(definitions.Count - 1);
      }
      static void UpdateRows(Grid grid, int delta) { UpdateDefinitions(() => new RowDefinition { Height = new GridLength(ElementHeight) }, grid.RowDefinitions, delta); }
      static void UpdateColumns(Grid grid, int delta) { UpdateDefinitions(() => new ColumnDefinition { Width = new GridLength(ElementWidth) }, grid.ColumnDefinitions, delta); }
      static void UpdateColumnsRows(Grid grid, int cdelta, int rdelta) { UpdateColumns(grid, cdelta); UpdateRows(grid, rdelta); }

      static void UpdateWidthHeight(FrameworkElement element, int x, int y) { element.Width = x * ElementWidth; element.Height = y * ElementHeight; }
      static Size DesiredWorkArea(FrameworkElement container) {
         return new Size((int)(container.ActualWidth / ElementWidth), (int)(container.ActualHeight / ElementHeight));
      }

      #endregion

      Func<string, byte[], IParser> _create;
      IParser _holder;
      MainCommandFactory _commandFactory;
      int _offset = 0;

      public int CurrentColumnCount{get;private set;}
      public int CurrentRowCount{get;private set;}

      public MainWindow(Func<string, byte[], IParser> create, string fileName, byte[] data) {
         _create = create;
         _holder = _create(fileName, data);
         _commandFactory = new MainCommandFactory(this);
         InitializeComponent();
         ScrollBar.Minimum = -MaxColumnCount;
         ScrollBar.Maximum = _holder.Length;
         Title = fileName.Split('\\').Last();
         Task.Factory.StartNew(_holder.Load).ContinueWith(t => Dispatcher.Invoke(() => JumpTo(_offset)));
      }

      #region Public Methods

      public void JumpTo(int location) {
         location = Math.Min(Math.Max(-MaxColumnCount, location), _holder.Length);

         foreach (FrameworkElement element in Body.Children) Recycle(element);
         Body.Children.Clear();

         _offset = location;
         Add(0, CurrentColumnCount * CurrentRowCount);
         ScrollBar.Value = _offset;
         UpdateHeaderText();
      }

      public void AddLocationToBreadCrumb() {
         if (BreadCrumbBar.Children.Count >= 5) {
            ((Button)BreadCrumbBar.Children[0]).Click -= NavigateBackClick;
            BreadCrumbBar.Children.RemoveAt(0);
         }
         var hex = _offset.ToHexString();
         while (hex.Length < 6) hex = "0" + hex;
         var button = new Button { Content = hex };
         button.Click += NavigateBackClick;
         BreadCrumbBar.Children.Add(button);
      }

      #endregion

      #region Helper Methods

      void UpdateRows(Panel panel, int rows, Action<FrameworkElement> updateAction) {
         IList<FrameworkElement> children = new List<FrameworkElement>();
         foreach (FrameworkElement child in panel.Children) children.Add(child);
         IList<FrameworkElement> toRemove = new List<FrameworkElement>();
         foreach (var element in children) {
            int row = Grid.GetRow(element);
            row += rows;
            if (row < 0 || row >= CurrentRowCount) {
               panel.Children.Remove(element);
               updateAction(element);
            } else {
               Grid.SetRow(element, row);
            }
         }
      }

      void MainFocus() {
         DependencyObject scope = FocusManager.GetFocusScope(MultiBox);
         FocusManager.SetFocusedElement(scope, this as IInputElement);
      }

      void Add(int start, int length) {
         int rows = CurrentRowCount, cols = CurrentColumnCount;
         var elements = _holder.CreateElements(_commandFactory, _offset + start, length).ToArray();
         Debug.Assert(elements.Length == length);
         for (var i = 0; i < elements.Length; i++) {
            SplitLocation(elements[i], cols, start + i);
            Body.Children.Add(elements[i]);
         }

         _commandFactory.SortInterpretations();
      }

      void UpdateHeaderRows(int oldRows, int newRows) {
         if (newRows == oldRows) return;

         // add new header rows
         for (int i = oldRows; i < newRows; i++) {
            var headerText = (_offset + i * CurrentColumnCount).ToHexString();
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
         int cols = CurrentColumnCount;
         foreach (TextBlock block in Headers.Children) {
            int location = Grid.GetRow(block) * cols + _offset;
            block.Text = location.ToHexString();
         }
      }

      void ShiftRows(int rows) {
         int all = CurrentColumnCount * CurrentRowCount;
         int add = CurrentColumnCount * rows;
         if (_offset - add < -MaxColumnCount || _offset - add > _holder.Length) return;

         UpdateRows(Body, rows, Recycle);
         UpdateRows(BackgroundBody, rows, _commandFactory.Recycle);

         _offset -= add;
         if (rows > 0) Add(0, add);
         else Add(all + add, -add);
      }

      void ShiftColumns(Panel panel, int shift, Action<FrameworkElement> removeAction) {
         int all = CurrentRowCount * CurrentColumnCount;
         var children = new FrameworkElement[panel.Children.Count];
         for (int i = 0; i < children.Length; i++) children[i] = (FrameworkElement)panel.Children[i];
         foreach (var element in children) {
            int loc = CombineLocation(element, CurrentColumnCount);
            loc += shift;
            if (loc < 0 || loc >= all) {
               panel.Children.Remove(element);
               removeAction(element);
            } else {
               SplitLocation(element, CurrentColumnCount, loc);
            }
         }
      }

      void ShiftColumns(int shift) {
         if (_offset - shift < -MaxColumnCount || _offset - shift > _holder.Length) return;

         ShiftColumns(Body, shift, Recycle);
         ShiftColumns(BackgroundBody, shift, _commandFactory.Recycle);

         _offset -= shift;
         if (shift > 0) Add(0, shift);
         else Add(CurrentRowCount * CurrentColumnCount + shift, -shift);
      }

      void Scroll(int dif) {
         if (dif == 0) return;
         var sign = Math.Sign(dif);
         var magn = Math.Abs(dif);
         magn -= magn % CurrentColumnCount; // discard the column portion
         int all = CurrentColumnCount * CurrentRowCount;
         if (magn > all) { JumpTo(_offset - (magn * sign)); return; }

         int rowPart = magn / CurrentColumnCount;
         ShiftRows(rowPart * sign);
         UpdateHeaderText();
         ScrollBar.Value = _offset;
      }

      void Recycle(FrameworkElement element) {
         _holder.Recycle(_commandFactory, element);
      }

      #endregion

      #region Events

      void Resize(object sender, EventArgs e) {
         int oldRows = CurrentRowCount, oldCols = CurrentColumnCount;
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
         CurrentColumnCount = newCols; CurrentRowCount = newRows;
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
         UpdateHeaderRows(oldRows, newRows);

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
            _commandFactory.CheckJumpForMouseOver();
         }
      }

      void HandleKey(object sender, KeyEventArgs e) {
         if (Keyboard.Modifiers == ModifierKeys.Control) {
            switch (e.Key) {
               case Key.Subtract: case Key.OemMinus: NavigateBackClick(null, null); break;
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
               BreadCrumbBar.Visibility = Visibility.Visible;
               MainFocus();
            }
            if (e.Key == Key.Enter) {
               int hex = MultiBox.Text.ParseAsHex();
               AddLocationToBreadCrumb();
               JumpTo(hex);
               MultiBoxContainer.Visibility = Visibility.Hidden;
               BreadCrumbBar.Visibility = Visibility.Visible;
               MainFocus();
            }

            // only allow hex keys
            if (!HexKeys.Contains(e.Key)) e.Handled = true;
         } else if (MultiBoxLabel.Text == "Find") {
            // dumb find: make it smarter later

            // check for special keys
            if (e.Key == Key.Escape) {
               MultiBoxContainer.Visibility = Visibility.Hidden;
               BreadCrumbBar.Visibility = Visibility.Visible;
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
               BreadCrumbBar.Visibility = Visibility.Visible;
               MainFocus();
            }
         }
      }

      IList<int> _findPositions;
      int _findIndex;

      #endregion

      #region Menu

      void OpenClick(object sender, RoutedEventArgs e) {
         string fileName;
         var data = Utils.LoadFile(out fileName);
         if (data == null) return;
         _holder = _create(fileName, data);
         ScrollBar.Maximum = _holder.Length;
         Body.Children.Clear();
         JumpTo(0);
         Title = fileName.Split('\\').Last();
         Task.Factory.StartNew(_holder.Load).ContinueWith(t => Dispatcher.Invoke(() => JumpTo(_offset)));
      }

      void ExitClick(object sender, RoutedEventArgs e) { Close(); }

      void InterpretClick(object sender, EventArgs e) {
         var item = sender as MenuItem;
         InterpretationPane.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
         if (item.IsChecked) {
            this.Width += InterpretationPane.Width;
         } else {
            this.Width -= InterpretationPane.Width;
         }
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
         BreadCrumbBar.Visibility = Visibility.Hidden;
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
         BreadCrumbBar.Visibility = Visibility.Hidden;
         Keyboard.Focus(MultiBox);
         MultiBox.SelectAll();
      }

      void NavigateBackClick(object sender, EventArgs e) {
         if (sender == null || sender is MenuItem) {
            sender = BreadCrumbBar.Children[BreadCrumbBar.Children.Count - 1];
         }
         var button = sender as Button;
         Debug.Assert(BreadCrumbBar.Children.Contains(button));
         int address = button.Content.ToString().ParseAsHex();
         JumpTo(address);
         BreadCrumbBar.Children.Remove(button);
         button.Click -= NavigateBackClick;
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
   }
}
