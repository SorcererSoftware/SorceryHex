using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
   partial class MainWindow : Window, IAppCommands {
      #region Utils

      const int MaxColumnCount = 0x30;

      public const int ElementWidth = 26, ElementHeight = 20;
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

      readonly IEnumerable<IModelFactory> _factories;
      readonly MultiBoxControl _multiBox;
      readonly MainCommandFactory _commandFactory;
      readonly CursorController _cursorController;
      IDictionary<Key, Action> KeyActions;

      string _filename;
      DateTime _filestamp;
      bool _fileedit;
      byte[] _filehash;

      public int Offset { get; private set; }
      public IModel Holder { get; private set; }
      public byte[] Data { get; private set; }

      public int CurrentColumnCount { get; private set; }
      public int CurrentRowCount { get; private set; }

      public MainWindow(IEnumerable<IModelFactory> factories, string fileName, byte[] data) {
         _factories = factories;
         Data = data;
         _commandFactory = new MainCommandFactory(this);
         InitializeComponent();
         _multiBox = new MultiBoxControl(this);
         MenuDock.Children.Add(_multiBox);
         CommandBindings.AddRange(_multiBox.CommandBindings);
         _cursorController = new CursorController(this, _commandFactory);
         ScrollBar.Minimum = -MaxColumnCount;
         InitializeKeyActions();
         MainFocus();
         LoadBestMatch(fileName, data);
      }

      #region Public Methods

      public event EventHandler JumpCompleted;
      public void JumpTo(int location, bool addToBreadcrumb = false) {
         if (addToBreadcrumb) _multiBox.AddLocationToBreadCrumb();
         location = Math.Min(Math.Max(-MaxColumnCount, location), Holder.Length);

         foreach (FrameworkElement element in Body.Children) Recycle(element);
         Body.Children.Clear();

         Offset = location;
         Add(0, CurrentColumnCount * CurrentRowCount);
         ScrollBar.Value = Offset;
         JumpCompleted(this, EventArgs.Empty);
         UpdateHeaderText();
      }

      public int[] Find(string term) { return this.Holder.Find(term).ToArray(); }

      public void HighlightFromLocation(int combinedLocation) {
         int location = Offset + combinedLocation;
         Debug.Assert(Holder.IsStartOfDataBlock(location) || Holder.IsWithinDataBlock(location));
         _cursorController.UpdateSelection(Holder, location);
      }

      public void UpdateHeaderText() {
         int cols = CurrentColumnCount;
         foreach (TextBlock block in Headers.Children) {
            int location = Grid.GetRow(block) * cols + Offset;
            block.Text = location.ToHexString();
         }
         _cursorController.UpdateSelection();
      }

      public void ShiftRows(int rows) {
         Debug.Assert(Math.Abs(rows) <= CurrentRowCount);
         int all = CurrentColumnCount * CurrentRowCount;
         int add = CurrentColumnCount * rows;
         if (Offset - add < -MaxColumnCount || Offset - add > Holder.Length) return;

         UpdateRows(Body, rows, Recycle);
         UpdateRows(BackgroundBody, rows, _cursorController.Recycle);

         Offset -= add;
         if (rows > 0) Add(0, add);
         else Add(all + add, -add);
      }

      public void ShiftColumns(Panel panel, int shift, Action<FrameworkElement> removeAction) {
         Debug.Assert(Math.Abs(shift) < CurrentColumnCount);
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

      public void MainFocus() {
         ResizeGrid.Focus();
      }

      public void RefreshElement(int location) {
         Debug.Assert(0 <= location - Offset && location - Offset < CurrentColumnCount * CurrentRowCount);
         var children = new FrameworkElement[Body.Children.Count];
         for (int i = 0; i < children.Length; i++) children[i] = (FrameworkElement)Body.Children[i];
         foreach (var element in children) {
            int loc = CombineLocation(element, CurrentColumnCount);
            if (loc + Offset == location) {
               Body.Children.Remove(element);
               Recycle(element);
            }
         }
         Add(location - Offset, 1);
      }

      public void WriteStatus(string status) {
         Dispatcher.Invoke((Action)(() => { StatusBar.Text = status; }));
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

      void Add(int start, int length) {
         int rows = CurrentRowCount, cols = CurrentColumnCount;
         var elements = Holder.CreateElements(_commandFactory, Offset + start, length).ToArray();
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
            var headerText = (Offset + i * CurrentColumnCount).ToHexString();
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

      void ShiftColumns(int shift) {
         if (Offset - shift < -MaxColumnCount || Offset - shift > Holder.Length) return;

         ShiftColumns(Body, shift, Recycle);
         ShiftColumns(BackgroundBody, shift, _cursorController.Recycle);

         Offset -= shift;
         if (shift > 0) Add(0, shift);
         else Add(CurrentRowCount * CurrentColumnCount + shift, -shift);
      }

      void Scroll(int dif) {
         if (dif == 0) return;
         var sign = Math.Sign(dif);
         var magn = Math.Abs(dif);
         magn -= magn % CurrentColumnCount; // discard the column portion
         int all = CurrentColumnCount * CurrentRowCount;
         if (magn > all) { JumpTo(Offset - (magn * sign)); return; }

         int rowPart = magn / CurrentColumnCount;
         ShiftRows(rowPart * sign);
         UpdateHeaderText();
         ScrollBar.Value = Offset;
      }

      void Recycle(FrameworkElement element) {
         Holder.Recycle(_commandFactory, element);
      }

      void LoadBestMatch(string filename, byte[] data) {
         _filename = filename;
         _filestamp = File.GetLastWriteTime(filename);
         _filehash = new Hashing.Murmur3().ComputeHash(data);
         Title = filename.Split('\\').Last();
         var array = _factories.Where(f => f.CanCreateModel(filename, data)).ToArray();
         Array.Sort(array);
         foreach (MenuItem item in Parser.Items) item.Click -= SwitchParserClick;
         Parser.Items.Clear();
         foreach (var factory in array) {
            var item = new MenuItem { Header = factory.DisplayName, IsCheckable = true };
            item.Click += SwitchParserClick;
            item.Tag = factory;
            Parser.Items.Add(item);
         }
         if (Parser.Items.Count > 1) {
            Parser.Visibility = Visibility.Visible;
            ((MenuItem)Parser.Items[Parser.Items.Count - 1]).IsChecked = true;
         }
         LoadParser(array.Last(), filename, data, jump: true);
      }

      void LoadParser(IModelFactory factory, string name, byte[] data, bool jump = false) {
         foreach (FrameworkElement element in Body.Children) Recycle(element);
         Body.Children.Clear();
         if (Holder != null) Holder.MoveToNext -= _cursorController.HandleMoveNext;
         Holder = factory.CreateModel(name, data, _multiBox.ScriptInfo);
         Holder.MoveToNext += _cursorController.HandleMoveNext;
         ScrollBar.Maximum = Holder.Length;
         if (jump) JumpTo(0);
         Parser.IsEnabled = false;
         _loadTimer = AutoTimer.Time("Full Load Time");
         Task.Factory.StartNew(Holder.Load).ContinueWith(t => Dispatcher.Invoke(LoadComplete));
      }

      AutoTimer _loadTimer;
      void LoadComplete() {
         Parser.IsEnabled = true;
         JumpTo(Offset);
         _loadTimer.Dispose();
         _loadTimer = null;
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

         int dif = Offset - (int)ScrollBar.Value;
         Scroll(dif);
      }

      void ScrollWheel(object sender, MouseWheelEventArgs e) {
         ShiftRows(Math.Sign(e.Delta));
         ScrollBar.Value = Offset;
         UpdateHeaderText();
      }

      void InitializeKeyActions() {
         KeyActions = new Dictionary<Key, Action> {
            { Key.Left,     () => ShiftColumns(-1) },
            { Key.Right,    () => ShiftColumns(1) },
            { Key.Down,     () => ShiftRows(1) },
            { Key.Up,       () => ShiftRows(-1) },
            { Key.I,        () => { InterpretItem.IsChecked = !InterpretItem.IsChecked; InterpretClick(InterpretItem, null); } },
         };
      }

      void HandleWindowKey(object sender, KeyEventArgs e) {
         if (Keyboard.Modifiers == ModifierKeys.Control) {
            e.Handled = true;
            if (KeyActions.ContainsKey(e.Key)) {
               KeyActions[e.Key]();
               UpdateHeaderText();
            } else if (arrowKeys.Contains(e.Key)) {
               ScrollBar.Value = Offset;
               UpdateHeaderText();
            } else {
               e.Handled = false;
            }
            return;
         }
      }

      void HandleKey(object sender, KeyEventArgs e) {
         if (e.Key == Key.Escape) {
            // some kind of modal knowledge required here...
         }

         switch (e.Key) {
            default:
               e.Handled = _cursorController.HandleKey(e.Key);
               break;
         }
      }

      void LoadData(object sender, EventArgs e) {
         var newstamp = File.GetLastWriteTime(_filename);
         if (newstamp == _filestamp) return;
         _filestamp = newstamp;
         Data = Utils.LoadFile(out _filename, new[] { _filename });
         _filehash = new Hashing.Murmur3().ComputeHash(Data);
         _fileedit = false;
         IModelFactory factory = null;
         foreach (MenuItem item in Parser.Items) if (item.IsChecked) factory = (IModelFactory)item.Tag;
         LoadParser(factory, Title, Data);
      }

      void SaveData(object sender, EventArgs e) {
         Hashing.Murmur3 hasher = new Hashing.Murmur3();
         byte[] hash = hasher.ComputeHash(Data);
         _fileedit = !Enumerable.SequenceEqual(hash, _filehash);
         if (!_fileedit) return;
         File.WriteAllBytes(_filename, Data);
         _filestamp = File.GetLastWriteTime(_filename);
         _fileedit = false;
         _filehash = hash;
      }

      #endregion

      #region Menu

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

      void OpenExecuted(object sender, RoutedEventArgs e) {
         Holder.MoveToNext -= _cursorController.HandleMoveNext;
         string fileName;
         var data = Utils.LoadFile(out fileName);
         if (data == null) return;
         LoadBestMatch(fileName, data);
      }

      void SwitchParserClick(object sender, EventArgs e) {
         var element = (FrameworkElement)sender;
         var factory = (IModelFactory)element.Tag;
         foreach (MenuItem item in Parser.Items) item.IsChecked = element == item;
         LoadParser(factory, Title, Data);
      }

      void CloseExecuted(object sender, RoutedEventArgs e) { Close(); }

      void OpenCanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = Parser.IsEnabled; }

      void Always(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = true; }

      #endregion
   }
}
