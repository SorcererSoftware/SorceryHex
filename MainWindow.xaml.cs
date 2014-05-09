using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SorceryHex {

   static class Utils {
      public static readonly string Hex = "0123456789ABCDEF";
      public static Func<A, C> Compose<A, B, C>(this Func<A, B> x, Func<B, C> y) { return a => y(x(a)); }

      public static string ToHexString(this int value) {
         if (value < 0) return "";
         if (value < 16) return Hex.Substring(value, 1);
         return ToHexString(value / 16) + Hex.Substring(value % 16, 1);
      }

      public static int ParseAsHex(this string value) {
         value = value.ToUpper();
         Debug.Assert(value.All(c => Hex.Contains(c)));

         int parsed = 0;
         for (int i = 0; i < value.Length; i++) {
            parsed <<= 4;
            parsed |= Hex.IndexOf(value[i]);
         }
         return parsed;
      }

      public static IEnumerable<T> Where<T>(this UIElementCollection collection, Func<T,bool> condition) {
         var list = new List<T>();
         foreach (T element in collection) if (condition(element)) list.Add(element);
         return list;
      }

      static string GetFile() {
         var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Roms (.GBA)|*GBA", Title = "Choose a Rom to open." };
         dialog.ShowDialog(null);
         return dialog.FileName;
      }

      public static byte[] LoadRom(string[] args = null) {
         var file = args != null && args.Length == 1 ? args[0] : GetFile();
         if (file == null) return null;
         if (!File.Exists(file)) return null;

         using (var stream = new FileStream(file, FileMode.Open)) {
            var rom = new byte[stream.Length];
            stream.Read(rom, 0, (int)stream.Length);
            return rom;
         }
      }
   }

   /// <summary>
   /// Interaction logic for MainWindow.xaml
   /// </summary>
   partial class MainWindow : Window {
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

      IElementFactory _holder;
      int _offset = 0;

      public MainWindow(IElementFactory holder) {
         _holder = holder;
         InitializeComponent();
         ScrollBar.Minimum = -MaxColumnCount;
         ScrollBar.Maximum = holder.Length;
      }

      #region Helper Methods

      void MainFocus() {
         DependencyObject scope = FocusManager.GetFocusScope(MultiBox);
         FocusManager.SetFocusedElement(scope, this as IInputElement);
      }

      void Add(int start, int length) {
         int rows = Body.RowDefinitions.Count, cols = Body.ColumnDefinitions.Count;
         var elements = _holder.CreateElements(_offset + start, length).ToArray();
         for (var i = 0; i < elements.Length; i++) {
            SplitLocation(elements[i], cols, start + i);
            Body.Children.Add(elements[i]);
         }
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

         IList<UIElement> children = new List<UIElement>();
         foreach (UIElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int row = Grid.GetRow(element);
            row += rows;
            if (row < 0 || row >= Body.RowDefinitions.Count) {
               Body.Children.Remove(element);
               _holder.Recycle(element);
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

         IList<UIElement> children = new List<UIElement>();
         foreach (UIElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int loc = CombineLocation(element, Body.ColumnDefinitions.Count);
            loc += shift;
            if (loc < 0 || loc >= all) {
               Body.Children.Remove(element);
               _holder.Recycle(element);
            } else {
               SplitLocation(element, Body.ColumnDefinitions.Count, loc);
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
         IList<UIElement> children = new List<UIElement>();
         foreach (UIElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int loc = CombineLocation(element, oldCols);
            if (loc >= newTotal) {
               Body.Children.Remove(element);
               _holder.Recycle(element);
            } else {
               SplitLocation(element, newCols, loc);
            }
         }

         // update header column
         UpdateHeaderColumn(oldRows, newRows);

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
      }

      void HandleKey(object sender, KeyEventArgs e) {
         if (Keyboard.Modifiers == ModifierKeys.Control) {
            switch (e.Key) {
               case Key.Left:  ShiftColumns(-1); break;
               case Key.Right: ShiftColumns(1); break;
               case Key.Down:  ShiftRows(1); break;
               case Key.Up:    ShiftRows(-1); break;
               case Key.G:     GotoClick(null, null); break;
            }

            if (arrowKeys.Contains(e.Key)) {
               ScrollBar.Value = _offset;
               UpdateHeaderText();
            }
         }

         if (e.Key == Key.Escape) {
            // some kind of modal knowledge required here...
         }
      }

      void HandleMultiBoxKey(object sender, KeyEventArgs e) {
         // sanitize for goto
         MultiBox.Text = new string(MultiBox.Text.ToUpper().Where(Utils.Hex.Contains).ToArray());

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
      }

      #endregion

      #region Menu

      void OpenClick(object sender, RoutedEventArgs e) {
         var rom = Utils.LoadRom();
         if (rom == null) return;
         _holder = new DataHolder(rom);
      }

      void ExitClick(object sender, RoutedEventArgs e) { Close(); }

      void GotoClick(object sender, EventArgs e) {
         MultiBoxLabel.Text = "Goto";
         MultiBoxContainer.Visibility = Visibility.Visible;
         Keyboard.Focus(MultiBox);
      }

      void AboutClick(object sender, RoutedEventArgs e) {
         switch (((MenuItem)sender).Header.ToString()) {
            case "_About":
               System.Diagnostics.Process.Start("http://sorcerersoftware.appspot.com");
               break;
         }
      }

      #endregion
   }
}
