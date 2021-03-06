﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

// TODO keep reference to the home tab at all times

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for MainWindow.xaml
   /// </summary>
   partial class MainWindow : Window, IAppCommands, IDataTabContainer {
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

      readonly IEnumerable<Grid> _bodies;
      readonly IEnumerable<IModelFactory> _factories;
      readonly MultiBoxControl _multiBox;
      readonly MainCommandFactory _commandFactory;
      readonly CursorController _cursorController;

      IDictionary<Key, Action> _keyActions;
      string _filename;
      DateTime _filestamp;
      byte[] _fileBytes;
      byte[] _filehash;
      IDataTab _previousTab;
      IDataTab _homeTab;

      public byte[] Data { get { return _fileBytes; } }
      public IDataTab CurrentTab { get; private set; }

      public MainWindow(IEnumerable<IModelFactory> factories, string fileName, byte[] data) {
         _factories = factories;
         _fileBytes = data;
         _commandFactory = new MainCommandFactory(this);
         InitializeComponent();
         _bodies = new[] { BackgroundBody, Body, EditBody };
         _multiBox = new MultiBoxControl(this);
         MenuDock.Children.Add(_multiBox);
         CommandBindings.AddRange(_multiBox.CommandBindings);
         _cursorController = new CursorController(this, _commandFactory);
         ScrollBar.Minimum = -MaxColumnCount;
         InitializeKeyActions();
         MainFocus();
         LoadBestMatch(fileName);
      }

      #region Public Methods

      public event EventHandler JumpCompleted;
      public void JumpTo(int location, bool addToBreadcrumb = false) {
         // if (addToBreadcrumb) _multiBox.AddLocationToBreadCrumb();
         location = Math.Min(Math.Max(-MaxColumnCount, location), _homeTab.Model.Segment.Length);

         foreach (FrameworkElement element in Body.Children) Recycle(element);
         _bodies.Foreach(body => body.Children.Clear());

         if (addToBreadcrumb) {
            var tab = new DataTab(this, _homeTab.Model, CurrentTab.Columns, CurrentTab.Rows, location);
            _previousTab = tab;
            CurrentTab.Model.MoveToNext -= _cursorController.HandleMoveNext;
            CurrentTab = tab;
            tab.Model.MoveToNext += _cursorController.HandleMoveNext;
            DataTabBar.Children.Add(tab);
            UpdateTabHighlight();
         } else {
            CurrentTab.Offset = location;
         }

         Add(0, CurrentTab.Columns * CurrentTab.Rows);
         ScrollBar.Value = CurrentTab.Offset;
         JumpCompleted(this, EventArgs.Empty);
         UpdateHeaderText();
         _cursorController.UpdateSelection();
      }

      public void JumpTo(string label, bool addToBreadcrumb = false) {
         foreach (MenuItem item in GotoItem.Items) {
            if (item.Header.ToString().ToLower() != label.ToLower()) continue;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            return;
         }
         throw new ArgumentException("There is no '" + label + "' to jump to");
      }

      public IEnumerable<int> Find(string term) { return CurrentTab.Model.Find(term); }

      public void HighlightFromLocation(int combinedLocation) {
         int location = CurrentTab.Offset + combinedLocation;
         Debug.Assert(CurrentTab.Model.IsStartOfDataBlock(location) || CurrentTab.Model.IsWithinDataBlock(location));
         _cursorController.UpdateSelection(CurrentTab.Model, location);
      }

      public void UpdateHeaderText() {
         int cols = CurrentTab.Columns;
         foreach (TextBlock block in Headers.Children) {
            int location = Grid.GetRow(block) * cols + CurrentTab.Offset;
            block.Text = CurrentTab.Model.GetLabel(location);
         }
         _cursorController.UpdateSelection();
      }

      public void ShiftRows(int rows) {
         Debug.Assert(Math.Abs(rows) <= CurrentTab.Rows);
         int all = CurrentTab.Columns * CurrentTab.Rows;
         int add = CurrentTab.Columns * rows;
         if (CurrentTab.Offset - add < -MaxColumnCount || CurrentTab.Offset - add > CurrentTab.Model.Segment.Length) return;

         UpdateRows(Body, rows, Recycle);
         UpdateRows(BackgroundBody, rows, _cursorController.Recycle);
         UpdateRows(EditBody, rows, (element) => { });

         CurrentTab.Offset -= add;
         if (rows > 0) Add(0, add);
         else Add(all + add, -add);
      }

      public void ShiftColumns(Panel panel, int shift, Action<FrameworkElement> removeAction) {
         Debug.Assert(Math.Abs(shift) < CurrentTab.Columns);
         int all = CurrentTab.Rows * CurrentTab.Columns;
         var children = new FrameworkElement[panel.Children.Count];
         for (int i = 0; i < children.Length; i++) children[i] = (FrameworkElement)panel.Children[i];
         foreach (var element in children) {
            int loc = CombineLocation(element, CurrentTab.Columns);
            loc += shift;
            if (loc < 0 || loc >= all) {
               panel.Children.Remove(element);
               removeAction(element);
            } else {
               SplitLocation(element, CurrentTab.Columns, loc);
            }
         }
      }

      public void MainFocus() {
         ResizeGrid.Focus();
      }

      public void RefreshElement(int location) {
         Debug.Assert(0 <= location - CurrentTab.Offset && location - CurrentTab.Offset < CurrentTab.Columns * CurrentTab.Rows);
         var children = new FrameworkElement[Body.Children.Count];
         for (int i = 0; i < children.Length; i++) children[i] = (FrameworkElement)Body.Children[i];
         foreach (var element in children) {
            int loc = CombineLocation(element, CurrentTab.Columns);
            if (loc + CurrentTab.Offset == location) {
               Body.Children.Remove(element);
               Recycle(element);
            }
         }
         Add(location - CurrentTab.Offset, 1);
      }

      public void WriteStatus(string status) {
         Dispatcher.Invoke((Action)(() => { StatusBar.Text = status; }));
      }

      public void Duplicate(int start, int length) {
         IModel model = CurrentTab.Model.Duplicate(start, length);
         var tab = new DuplicateTab(this, _commandFactory, model, CurrentTab.Columns, CurrentTab.Rows, start);
         DataTabBar.Children.Add(tab);
         SelectTab(tab);
      }

      #endregion

      #region DataTabContainer

      public void SelectTab(IDataTab tab) {
         _previousTab = CurrentTab;
         CurrentTab.Model.MoveToNext -= _cursorController.HandleMoveNext;
         CurrentTab = tab;
         tab.Model.MoveToNext += _cursorController.HandleMoveNext;
         UpdateTabHighlight();
         JumpTo(tab.Offset);
         _previousTab = CurrentTab;
      }

      public void RemoveTab(IDataTab tab) {
         var uitab = DataTabBar.Children.Where((FrameworkElement child) => child == tab).First();

         var animation = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(250)));
         animation.Completed += (sender, e) => {
            DataTabBar.Children.Remove(uitab);
            if (CurrentTab == tab) {
               SelectTab(_homeTab);
            }
         };
         uitab.BeginAnimation(WidthProperty, animation);
      }

      public void PushData(DuplicateTab duplicateTab, int newOffset) {
         // get the hometab
         // push the data from the duplicate tab into the hometab
         // access to the original offset, new offset, original length and new length will help delete extra data
         _homeTab.Model.Replace(duplicateTab.OriginalOffset, duplicateTab.OriginalLength, duplicateTab.Model, newOffset);
      }

      public int FindFreeSpace(int length) {
         return _homeTab.Model.FindFreeSpace(length);
      }

      public void Repoint(int originalOffset, int newOffset) {
         // in order for this to be meaningful, the model has to understand pointers.
         _homeTab.Model.Repoint(originalOffset, newOffset);
      }

      void UpdateTabHighlight() { foreach (ToggleButton button in DataTabBar.Children) button.IsChecked = button == CurrentTab; }

      #endregion

      #region Helper Methods

      void UpdateRows(Panel panel, int rows, Action<FrameworkElement> updateAction) {
         IList<FrameworkElement> children = new List<FrameworkElement>();
         foreach (FrameworkElement child in panel.Children) children.Add(child);
         IList<FrameworkElement> toRemove = new List<FrameworkElement>();
         foreach (var element in children) {
            int row = Grid.GetRow(element);
            row += rows;
            if (row < 0 || row >= CurrentTab.Rows) {
               panel.Children.Remove(element);
               updateAction(element);
            } else {
               Grid.SetRow(element, row);
            }
         }
      }

      void Add(int start, int length) {
         int rows = CurrentTab.Rows, cols = CurrentTab.Columns;
         var elements = CurrentTab.Model.CreateElements(_commandFactory, CurrentTab.Offset + start, length).ToArray();
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
            var headerText = CurrentTab.Model.GetLabel(CurrentTab.Offset + i * CurrentTab.Columns);
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
         if (CurrentTab.Offset - shift < -MaxColumnCount || CurrentTab.Offset - shift > CurrentTab.Model.Segment.Length) return;

         ShiftColumns(Body, shift, Recycle);
         ShiftColumns(BackgroundBody, shift, _cursorController.Recycle);
         ShiftColumns(EditBody, shift, (element) => { });

         CurrentTab.Offset -= shift;
         if (shift > 0) Add(0, shift);
         else Add(CurrentTab.Rows * CurrentTab.Columns + shift, -shift);
      }

      void Scroll(int dif) {
         if (dif == 0) return;
         var sign = Math.Sign(dif);
         var magn = Math.Abs(dif);
         magn -= magn % CurrentTab.Columns; // discard the column portion
         int all = CurrentTab.Columns * CurrentTab.Rows;
         if (magn > all) { JumpTo(CurrentTab.Offset - (magn * sign)); return; }

         int rowPart = magn / CurrentTab.Columns;
         ShiftRows(rowPart * sign);
         UpdateHeaderText();
         ScrollBar.Value = CurrentTab.Offset;
      }

      void Recycle(FrameworkElement element) {
         _previousTab.Model.Recycle(_commandFactory, element);
      }

      IModelFactory[] Sort(IList<IModelFactory> factories) {
         var index = new Dictionary<IModelFactory, int>();
         foreach (var a in factories) {
            index[a] = 0;
            foreach (var b in factories) {
               index[a] += a.CompareTo(b);
               index[a] -= b.CompareTo(a);
            }
         }
         return factories.OrderBy(f => index[f]).ToArray();
      }

      void LoadBestMatch(string filename) {
         _filename = filename;
         _filestamp = File.GetLastWriteTime(filename);
         _filehash = new Hashing.Murmur3().ComputeHash(_fileBytes);
         Title = filename.Split('\\').Last();
         var array = _factories.Where(f => f.CanCreateModel(filename, _fileBytes)).ToArray();
         array = Sort(array);
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
         LoadParser(array.Last(), filename, _fileBytes, jump: true);
      }

      void LoadParser(IModelFactory factory, string name, byte[] data, bool jump = false) {
         foreach (FrameworkElement element in Body.Children) Recycle(element);
         AutoTimer.ClearReport();
         // TODO clear tabs
         _bodies.Foreach(body => body.Children.Clear());
         // if (Holder != null) Holder.MoveToNext -= _cursorController.HandleMoveNext;
         _multiBox.ResetScope();
         var model = factory.CreateModel(name, data, _multiBox.ScriptInfo);
         model.MoveToNext += _cursorController.HandleMoveNext;
         ScrollBar.Maximum = model.Segment.Length;
         // JumpTo(jump ? 0 : Offset); // TODO glean from previous current tab
         Parser.IsEnabled = false;
         if (jump) _multiBox.BreadCrumbBar.Children.Clear();
         GotoItem.Items.Clear();
         _loadTimer = AutoTimer.Time("Full Load Time");
         Task.Factory.StartNew(() => model.Load(_commandFactory)).ContinueWith(t => Dispatcher.Invoke((Action)LoadComplete));
         var tab = new DataTab(this, model, Body.ColumnDefinitions.Count, Body.RowDefinitions.Count, 0, isHomeTab: true);
         DataTabBar.Children.Clear();
         _homeTab = tab;
         DataTabBar.Children.Add(tab);
         _previousTab = tab;
         CurrentTab = tab;
         UpdateTabHighlight();
      }

      AutoTimer _loadTimer;
      void LoadComplete() {
         Parser.IsEnabled = true;
         JumpTo(CurrentTab.Offset);
         _loadTimer.Dispose();
         _loadTimer = null;
         _commandFactory.ShowErrors(_multiBox);
      }

      #endregion

      #region Events

      void Resize(object sender, EventArgs e) {
         int oldRows = CurrentTab.Rows, oldCols = CurrentTab.Columns;
         var newSize = DesiredWorkArea(ResizeGrid);
         int oldTotal = CurrentTab.Columns * CurrentTab.Rows;
         {
            int newCols = (int)newSize.Width, newRows = (int)newSize.Height;
            newCols = Math.Min(newCols, MaxColumnCount);
            ScrollBar.SmallChange = newCols;
            ScrollBar.LargeChange = newCols * newRows;

            if (oldCols == newCols && oldRows == newRows) return;

            if (!CurrentTab.Resize(newCols, newRows)) return;
         }

         int newTotal = CurrentTab.Columns * CurrentTab.Rows;
         Action<Grid> updateSize = g => {
            UpdateWidthHeight(g, CurrentTab.Columns, CurrentTab.Rows);
            UpdateColumnsRows(g, CurrentTab.Columns - oldCols, CurrentTab.Rows - oldRows);
         };

         // update container sizes
         updateSize(Body);
         updateSize(BackgroundBody);
         updateSize(EditBody);
         UpdateRows(Headers, CurrentTab.Rows - oldRows);
         Headers.Height = CurrentTab.Rows * ElementHeight;

         // update element locations (remove excess elements)
         IList<FrameworkElement> children = new List<FrameworkElement>();
         foreach (FrameworkElement child in Body.Children) children.Add(child);
         foreach (var element in children) {
            int loc = CombineLocation(element, oldCols);
            if (loc >= newTotal) {
               Body.Children.Remove(element);
               Recycle(element);
            } else {
               SplitLocation(element, CurrentTab.Columns, loc);
            }
         }

         // update header column
         UpdateHeaderRows(oldRows, CurrentTab.Rows);

         if (oldCols != CurrentTab.Columns) UpdateHeaderText();

         // add more elements if needed
         if (oldTotal < newTotal) Add(oldTotal, newTotal - oldTotal);
      }

      void OnScroll(object sender, ScrollEventArgs e) {
         if (ScrollBar.Value < ScrollBar.Minimum || ScrollBar.Value > ScrollBar.Maximum) {
            ScrollBar.Value = 0;
         }

         int dif = CurrentTab.Offset - (int)ScrollBar.Value;
         Scroll(dif);
      }

      void ScrollWheel(object sender, MouseWheelEventArgs e) {
         ShiftRows(Math.Sign(e.Delta));
         ScrollBar.Value = CurrentTab.Offset;
         UpdateHeaderText();
      }

      void InitializeKeyActions() {
         _keyActions = new Dictionary<Key, Action> {
            { Key.Left,     () => ShiftColumns(-1) },
            { Key.Right,    () => ShiftColumns(1) },
            { Key.Down,     () => ShiftRows(1) },
            { Key.Up,       () => ShiftRows(-1) },
            { Key.I,        () => { InterpretItem.IsChecked = !InterpretItem.IsChecked; InterpretClick(InterpretItem, null); } },
         };
      }

      void HandleWindowKey(object sender, KeyEventArgs e) {
         if (EditBody.IsKeyboardFocusWithin) return;
         if (Keyboard.Modifiers == ModifierKeys.Control) {
            e.Handled = true;
            if (_keyActions.ContainsKey(e.Key)) {
               _keyActions[e.Key]();
               UpdateHeaderText();
            } else if (arrowKeys.Contains(e.Key)) {
               ScrollBar.Value = CurrentTab.Offset;
               UpdateHeaderText();
            } else {
               e.Handled = false;
            }
            return;
         }
      }

      void HandleKey(object sender, KeyEventArgs e) {
         if (EditBody.IsKeyboardFocusWithin) return;
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
         _fileBytes = Utils.LoadFile(out _filename, new[] { _filename });
         _filehash = new Hashing.Murmur3().ComputeHash(_fileBytes);
         IModelFactory factory = null;
         foreach (MenuItem item in Parser.Items) if (item.IsChecked) factory = (IModelFactory)item.Tag;
         LoadParser(factory, Title, _fileBytes);
      }

      void SaveData(object sender, EventArgs e) {
         Hashing.Murmur3 hasher = new Hashing.Murmur3();
         byte[] hash = hasher.ComputeHash(_fileBytes);
         if (Enumerable.SequenceEqual(hash, _filehash)) return;
         File.WriteAllBytes(_filename, _fileBytes);
         _filestamp = File.GetLastWriteTime(_filename);
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
               new AboutDialog(_factories).ShowDialog();
               break;
            case "_Online Documentation":
               Process.Start("http://sorcerersoftware.appspot.com");
               break;
            case "About _Solarized":
               Process.Start(Solarized.Theme.Info);
               break;
         }
      }

      void OpenExecuted(object sender, RoutedEventArgs e) {
         CurrentTab.Model.MoveToNext -= _cursorController.HandleMoveNext;
         string fileName;
         var data = Utils.LoadFile(out fileName);
         if (data == null) return;
         _fileBytes = data;
         LoadBestMatch(fileName);
      }

      void SwitchParserClick(object sender, EventArgs e) {
         var element = (FrameworkElement)sender;
         var factory = (IModelFactory)element.Tag;
         foreach (MenuItem item in Parser.Items) item.IsChecked = element == item;
         LoadParser(factory, Title, _fileBytes);
      }

      void CloseExecuted(object sender, RoutedEventArgs e) { Close(); }

      void OpenCanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = Parser.IsEnabled; }

      void Always(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = true; }

      #endregion
   }
}
